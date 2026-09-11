using System.Text.Json;

using DsaWuerfelApp.Services.Application.Import;
using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

/// <summary>
/// Authoritative encounter state for a game session. The browser only submits
/// commands; ordering, action bookkeeping and initiative rolls are resolved here.
/// </summary>
public sealed class CombatSessionStateService(
    SessionRuntimeState runtimeState,
    SessionRecordStore recordStore,
    DiceService diceService,
    IServiceScopeFactory scopeFactory)
{
    private const int MaxAppliedRequestIds = 128;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public async Task<CombatSessionSnapshotDto> GetAsync(
        string sessionId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var session = runtimeState.GetMemberSession(sessionId, userId);
            var persisted = Load(session);
            var normalized = Normalize(session, persisted.Current, userId);
            var snapshot = normalized with
            {
                UndoAvailable = persisted.Undo is not null && !string.IsNullOrWhiteSpace(persisted.UndoOwnerUserId)
            };
            if (!ReferenceEquals(normalized, persisted.Current) || session.CombatStateJson is null)
            {
                Save(session, persisted with { Current = snapshot });
            }

            return snapshot;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<CombatSessionMutationResultDto> MutateAsync(
        CombatSessionMutationRequestDto request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestId == Guid.Empty)
        {
            throw Validation("Jede Kampfänderung braucht eine RequestId.");
        }

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw Validation("Für die Kampfverwaltung muss eine Session ausgewählt sein.");
        }

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var session = runtimeState.GetMemberSession(request.SessionId, userId);
            var persisted = Load(session);
            var current = Normalize(session, persisted.Current, userId) with
            {
                UndoAvailable = persisted.Undo is not null && !string.IsNullOrWhiteSpace(persisted.UndoOwnerUserId)
            };

            if (persisted.AppliedRequestIds.Contains(request.RequestId))
            {
                return Result(request, current, applied: false, alreadyApplied: true, stale: false,
                    "Diese Änderung wurde bereits verarbeitet.");
            }

            if (request.ExpectedRevision.HasValue && request.ExpectedRevision.Value != current.Revision)
            {
                return Result(request, current, applied: false, alreadyApplied: false, stale: true,
                    $"Der Kampfstand ist inzwischen bei Revision {current.Revision}. Bitte den aktuellen Stand laden.");
            }

            if (!Enum.IsDefined(request.Kind))
            {
                throw Validation("Unbekannte Kampfänderung.");
            }

            ValidateRuntimeState(request.RuntimeState);

            var authorization = ResolveAuthorization(session, current, request, userId);
            if (!authorization.Allowed)
            {
                throw new RequestRejectedException(RequestRejectionReason.Forbidden, authorization.Message);
            }

            var previous = current;
            var rolls = Array.Empty<DiceRollDto>();
            CombatSessionSnapshotDto next;
            string description;

            switch (request.Kind)
            {
                case CombatSessionMutationKind.RollInitiative:
                    (next, rolls, description) = await RollInitiativeAsync(session, current, request, userId, cancellationToken);
                    break;
                case CombatSessionMutationKind.SetInitiative:
                    (next, description) = await SetInitiativeAsync(session, current, request, userId, cancellationToken);
                    break;
                case CombatSessionMutationKind.SyncRuntimeState:
                    (next, description) = await SyncRuntimeStateAsync(current, request, userId, cancellationToken);
                    break;
                case CombatSessionMutationKind.CompleteAction:
                    (next, description) = await CompleteActionAsync(current, request, userId, cancellationToken);
                    break;
                case CombatSessionMutationKind.ConsumeReaction:
                    (next, description) = ConsumeReaction(current, request);
                    break;
                case CombatSessionMutationKind.HoldAction:
                    (next, description) = ChangeActionState(current, request, CombatActionEntryState.Held,
                        "Handlung gehalten");
                    break;
                case CombatSessionMutationKind.ExecuteHeldAction:
                    (next, description) = ChangeActionState(current, request, CombatActionEntryState.Open,
                        "Gehaltene Handlung bereit");
                    break;
                case CombatSessionMutationKind.AddAction:
                    (next, description) = AddAction(current, request);
                    break;
                case CombatSessionMutationKind.AddOpponent:
                    EnsureMaster(session, userId);
                    (next, description) = AddOpponent(current, request);
                    break;
                case CombatSessionMutationKind.NewRound:
                    EnsureMaster(session, userId);
                    (next, description) = NewRound(current, request);
                    break;
                case CombatSessionMutationKind.Undo:
                    (next, description) = Undo(current, persisted, userId);
                    break;
                case CombatSessionMutationKind.SetAnnouncement:
                    (next, description) = SetAnnouncement(current, request);
                    break;
                case CombatSessionMutationKind.Orient:
                    (next, description) = await AddOrientationActionAsync(
                        current,
                        request,
                        userId,
                        cancellationToken);
                    break;
                case CombatSessionMutationKind.ResolveOrientation:
                    (next, rolls, description) = await ResolveOrientationAsync(
                        current,
                        request,
                        userId,
                        cancellationToken);
                    break;
                default:
                    throw Validation("Diese Kampfänderung wird nicht unterstützt.");
            }

            next = FinalizeSnapshot(next with
            {
                SessionId = session.SessionId,
                Revision = current.Revision + 1,
                LastMutationId = request.RequestId,
                LastMutationDescription = description,
                LastMutationUserId = userId
            });

            var appliedRequestIds = persisted.AppliedRequestIds
                .Append(request.RequestId)
                .TakeLast(MaxAppliedRequestIds)
                .ToArray();
            var undo = request.Kind == CombatSessionMutationKind.Undo ? null : previous;
            var undoOwner = request.Kind == CombatSessionMutationKind.Undo ? null : userId;
            next = next with { UndoAvailable = undo is not null };
            Save(session, new PersistedState(next, undo, undoOwner, appliedRequestIds));
            return Result(request, next, applied: true, alreadyApplied: false, stale: false, description, rolls);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task<(CombatSessionSnapshotDto Snapshot, DiceRollDto[] Rolls, string Description)> RollInitiativeAsync(
        GameSession session,
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request,
        string userId,
        CancellationToken cancellationToken)
    {
        var participant = FindParticipant(current, request.ParticipantId, request.HeroId)
                          ?? throw Validation("Der Initiative-Teilnehmer wurde nicht gefunden.");
        var initiativeInfo = await ResolveInitiativeInfoAsync(participant, request, userId, cancellationToken);
        if (!initiativeInfo.BaseValue.HasValue)
        {
            throw Validation("Für diesen Teilnehmer ist kein Initiative-Basiswert vorhanden.");
        }

        var roll = diceService.RollDice([new DiceRollGroupDto(6, initiativeInfo.DiceCount)]);
        var rollTotal = roll.Sum(result => result.Value);
        var updatedParticipant = participant with
        {
            InitiativeBase = initiativeInfo.BaseValue,
            InitiativeSetId = initiativeInfo.SetId,
            InitiativeDiceCount = initiativeInfo.DiceCount,
            StartRoll = rollTotal,
            InitiativeCorrection = 0,
            InitiativeRuntimeModifier = initiativeInfo.RuntimeModifier,
            InitiativeRuntimeNotes = initiativeInfo.RuntimeNotes,
            RuntimeState = request.RuntimeState ?? participant.RuntimeState,
            RecoverableInitiativeLoss = 0,
            CurrentInitiative = initiativeInfo.BaseValue.Value + rollTotal + initiativeInfo.RuntimeModifier,
            ActionAvailable = true,
            ReactionAvailable = true,
            IsOriented = false
        };
        var participants = ReplaceParticipant(current.Participants, updatedParticipant);
        var actions = current.Actions;
        if (actions.All(action => action.ParticipantId != participant.Id || action.Round != current.Round))
        {
            actions = actions.Append(CreateNormalAction(updatedParticipant, current.Round)).ToArray();
        }

        return (current with { IsStarted = true, Participants = participants, Actions = actions }, roll,
            current.Participants.Any(existing => existing.Id == participant.Id && existing.CurrentInitiative.HasValue)
                ? $"Startwurf für {participant.Name} ersetzt"
                : $"Startwurf für {participant.Name} gespeichert");
    }

    private async Task<(CombatSessionSnapshotDto Snapshot, string Description)> SetInitiativeAsync(
        GameSession session,
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request,
        string userId,
        CancellationToken cancellationToken)
    {
        var participant = FindParticipant(current, request.ParticipantId, request.HeroId)
                          ?? throw Validation("Der Initiative-Teilnehmer wurde nicht gefunden.");
        if (!request.Initiative.HasValue)
        {
            throw Validation("Bitte einen konkreten aktuellen Initiativewert eingeben.");
        }

        var initiativeInfo = await ResolveInitiativeInfoAsync(participant, request, userId, cancellationToken);
        var baseValue = initiativeInfo.BaseValue ?? participant.InitiativeBase;

        var usesNewInitiativeInputs = request.RuntimeState is not null ||
                                      !string.IsNullOrWhiteSpace(request.SetId);
        var runtimeModifier = !usesNewInitiativeInputs
            ? participant.InitiativeRuntimeModifier
            : initiativeInfo.RuntimeModifier;
        var runtimeNotes = !usesNewInitiativeInputs
            ? participant.InitiativeRuntimeNotes
            : initiativeInfo.RuntimeNotes;

        var correction = baseValue.HasValue && participant.StartRoll.HasValue
            ? request.Initiative.Value -
              (baseValue.Value + participant.StartRoll.Value + runtimeModifier - participant.RecoverableInitiativeLoss)
            : request.Initiative.Value - (baseValue ?? 0) - runtimeModifier;
        var updatedParticipant = participant with
        {
            InitiativeBase = baseValue,
            InitiativeSetId = initiativeInfo.SetId ?? participant.InitiativeSetId,
            InitiativeRuntimeModifier = runtimeModifier,
            InitiativeRuntimeNotes = runtimeNotes,
            RuntimeState = request.RuntimeState ?? participant.RuntimeState,
            CurrentInitiative = request.Initiative,
            InitiativeCorrection = correction,
            ActionAvailable = true,
            ReactionAvailable = participant.ReactionAvailable
        };
        var actions = current.Actions;
        if (actions.All(action => action.ParticipantId != participant.Id || action.Round != current.Round))
        {
            actions = actions.Append(CreateNormalAction(updatedParticipant, current.Round)).ToArray();
        }

        return (current with { IsStarted = true, Participants = ReplaceParticipant(current.Participants, updatedParticipant), Actions = actions },
            $"INI von {participant.Name} manuell korrigiert");
    }

    private async Task<(CombatSessionSnapshotDto Snapshot, string Description)> SyncRuntimeStateAsync(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request,
        string userId,
        CancellationToken cancellationToken)
    {
        if (request.RuntimeState is null)
        {
            throw Validation("Zum Aktualisieren des laufenden INI-Zustands muss ein Kampfzustand übertragen werden.");
        }

        var participant = FindParticipant(current, request.ParticipantId, request.HeroId)
                          ?? throw Validation("Der Teilnehmer für den laufenden Kampfzustand wurde nicht gefunden.");
        var initiativeInfo = await ResolveInitiativeInfoAsync(participant, request, userId, cancellationToken);
        var updatedParticipant = ApplyInitiativeInputs(participant, request, initiativeInfo, out var initiativeDelta);

        var description = initiativeDelta == 0
            ? $"Laufender Kampfzustand von {participant.Name} gespeichert"
            : $"Laufender Kampfzustand von {participant.Name} gespeichert; INI {FormatSigned(initiativeDelta)} angepasst";
        return (current with { Participants = ReplaceParticipant(current.Participants, updatedParticipant) }, description);
    }

    private async Task<(CombatSessionSnapshotDto Snapshot, string Description)> CompleteActionAsync(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request,
        string userId,
        CancellationToken cancellationToken)
    {
        var action = FindAction(current, request.ActionId, request.ParticipantId)
                     ?? throw Validation("Die offene Handlung wurde nicht gefunden.");
        if (action.IsReaction || action.State != CombatActionEntryState.Open)
        {
            throw Validation("Nur eine offene eigene Handlung kann abgeschlossen werden.");
        }

        if (action.Label.Equals("Orientieren", StringComparison.OrdinalIgnoreCase) && action.RequiresCheck)
        {
            throw Validation("Für dieses Orientieren muss zuerst die IN-Probe gewürfelt werden.");
        }

        var participant = current.Participants.FirstOrDefault(item => item.Id == action.ParticipantId)
                          ?? throw Validation("Der Teilnehmer für die Handlung wurde nicht gefunden.");
        if (request.RuntimeState is not null || !string.IsNullOrWhiteSpace(request.SetId))
        {
            var initiativeInfo = await ResolveInitiativeInfoAsync(
                participant,
                request,
                userId,
                cancellationToken);
            participant = ApplyInitiativeInputs(participant, request, initiativeInfo, out _);
            current = current with { Participants = ReplaceParticipant(current.Participants, participant) };
        }

        var actions = ReplaceAction(current.Actions, action with
        {
            State = CombatActionEntryState.Completed,
            CompletedAt = DateTimeOffset.UtcNow
        });
        var participants = current.Participants;
        if (action.Label.Equals("Orientieren", StringComparison.OrdinalIgnoreCase) &&
            action.OrientationUninterrupted)
        {
            participants = ReplaceParticipant(participants, ApplyOrientation(participant));
        }

        var description = action.Label.Equals("Orientieren", StringComparison.OrdinalIgnoreCase) &&
                          !action.OrientationUninterrupted
            ? "Orientieren abgeschlossen, aber nicht ungestört möglich; INI bleibt unverändert"
            : $"{action.Label} abgeschlossen";
        return (current with { Actions = actions, Participants = participants }, description);
    }

    private static CombatSessionParticipantDto ApplyInitiativeInputs(
        CombatSessionParticipantDto participant,
        CombatSessionMutationRequestDto request,
        InitiativeProfileInfo initiativeInfo,
        out int initiativeDelta)
    {
        initiativeDelta = initiativeInfo.RuntimeModifier - participant.InitiativeRuntimeModifier;
        var initiativeBase = participant.InitiativeBase;
        var setChanged = initiativeInfo.SetId is not null &&
                         !string.Equals(initiativeInfo.SetId, participant.InitiativeSetId, StringComparison.Ordinal);
        if (setChanged && initiativeInfo.BaseValue.HasValue && initiativeBase.HasValue)
        {
            initiativeDelta += initiativeInfo.BaseValue.Value - initiativeBase.Value;
            initiativeBase = initiativeInfo.BaseValue;
        }

        return participant with
        {
            InitiativeBase = initiativeBase,
            InitiativeSetId = initiativeInfo.SetId ?? participant.InitiativeSetId,
            RuntimeState = request.RuntimeState ?? participant.RuntimeState,
            InitiativeRuntimeModifier = initiativeInfo.RuntimeModifier,
            InitiativeRuntimeNotes = initiativeInfo.RuntimeNotes,
            CurrentInitiative = participant.CurrentInitiative.HasValue
                ? participant.CurrentInitiative.Value + initiativeDelta
                : null
        };
    }

    private async Task<(CombatSessionSnapshotDto Snapshot, DiceRollDto[] Rolls, string Description)> ResolveOrientationAsync(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request,
        string userId,
        CancellationToken cancellationToken)
    {
        var action = FindAction(current, request.ActionId, request.ParticipantId)
                     ?? throw Validation("Die Orientieren-Handlung wurde nicht gefunden.");
        if (!action.Label.Equals("Orientieren", StringComparison.OrdinalIgnoreCase) || !action.RequiresCheck)
        {
            throw Validation("Für diese Orientieren-Handlung ist keine IN-Probe erforderlich.");
        }

        if (action.State != CombatActionEntryState.Open)
        {
            throw Validation("Die Orientieren-Handlung ist nicht mehr offen.");
        }

        var participant = FindParticipant(current, action.ParticipantId, null)
                          ?? throw Validation("Der Teilnehmer für Orientieren wurde nicht gefunden.");
        var completedAction = action with
        {
            State = CombatActionEntryState.Completed,
            CompletedAt = DateTimeOffset.UtcNow
        };
        var actions = ReplaceAction(current.Actions, completedAction);

        if (!action.OrientationUninterrupted)
        {
            return (current with { Actions = actions }, [],
                $"Orientieren von {participant.Name} nicht möglich: nicht ungestört; INI bleibt unverändert");
        }

        var initiativeInfo = await ResolveInitiativeInfoAsync(
            participant,
            request,
            userId,
            cancellationToken);
        var intuition = initiativeInfo.Profile?.Attributes
            .FirstOrDefault(attribute => string.Equals(attribute.Key, "IN", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        if (!intuition.HasValue)
        {
            throw Validation($"Für {participant.Name} ist kein importierter IN-Wert verfügbar.");
        }

        var target = Math.Clamp(intuition.Value + action.OrientationRelief, 0, 20);
        var roll = diceService.RollDice([new DiceRollGroupDto(20, 1)])[0];
        if (roll.Value > target)
        {
            return (current with { Actions = actions }, [roll],
                $"IN-Probe für {participant.Name}: {roll.Value} gegen {target} misslungen; INI bleibt unverändert");
        }

        var orientedParticipant = request.RuntimeState is null
            ? participant
            : participant with
            {
                RuntimeState = request.RuntimeState,
                InitiativeRuntimeModifier = initiativeInfo.RuntimeModifier,
                InitiativeRuntimeNotes = initiativeInfo.RuntimeNotes
            };
        orientedParticipant = ApplyOrientation(orientedParticipant);
        return (current with
        {
            Actions = actions,
            Participants = ReplaceParticipant(current.Participants, orientedParticipant)
        }, [roll],
            $"IN-Probe für {participant.Name}: {roll.Value} gegen {target} gelungen; INI auf Orientieren gesetzt");
    }

    private static CombatSessionParticipantDto ApplyOrientation(CombatSessionParticipantDto participant)
    {
        if (!participant.InitiativeBase.HasValue)
        {
            throw Validation("Orientieren braucht einen bekannten Initiative-Basiswert.");
        }

        var initiativeDiceMaximum = participant.InitiativeDiceCount >= 2 ? 12 : 6;
        return participant with
        {
            StartRoll = initiativeDiceMaximum,
            RecoverableInitiativeLoss = 0,
            CurrentInitiative = participant.InitiativeBase.Value + initiativeDiceMaximum +
                                participant.InitiativeRuntimeModifier + participant.InitiativeCorrection,
            IsOriented = true
        };
    }

    private static (CombatSessionSnapshotDto Snapshot, string Description) ConsumeReaction(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request)
    {
        var participant = FindParticipant(current, request.ParticipantId, request.HeroId)
                          ?? throw Validation("Der Teilnehmer für die Reaktion wurde nicht gefunden.");
        if (!participant.ReactionAvailable)
        {
            throw Validation($"Für {participant.Name} ist keine Reaktion mehr verfügbar.");
        }

        return (current with
        {
            Participants = ReplaceParticipant(current.Participants, participant with { ReactionAvailable = false })
        }, $"Reaktion von {participant.Name} verbraucht");
    }

    private static (CombatSessionSnapshotDto Snapshot, string Description) ChangeActionState(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request,
        CombatActionEntryState state,
        string description)
    {
        var action = FindAction(current, request.ActionId, request.ParticipantId)
                     ?? throw Validation("Die Handlung wurde nicht gefunden.");
        if (state == CombatActionEntryState.Held && action.State != CombatActionEntryState.Open)
        {
            throw Validation("Nur eine offene Handlung kann gehalten werden.");
        }

        if (state == CombatActionEntryState.Open && action.State != CombatActionEntryState.Held)
        {
            throw Validation("Nur eine gehaltene Handlung kann ausgeführt werden.");
        }

        return (current with { Actions = ReplaceAction(current.Actions, action with { State = state }) },
            $"{action.Label}: {description}");
    }

    private static (CombatSessionSnapshotDto Snapshot, string Description) AddAction(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request)
    {
        var participant = FindParticipant(current, request.ParticipantId, request.HeroId)
                          ?? throw Validation("Der Teilnehmer für die Handlung wurde nicht gefunden.");
        if (!participant.CurrentInitiative.HasValue)
        {
            throw Validation("Eine Handlung ist erst nach dem Initiativewurf verfügbar.");
        }

        var label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
        if (label is null)
        {
            throw Validation("Zusätzliche Handlungen brauchen eine kurze Bezeichnung.");
        }

        var action = new CombatSessionActionDto
        {
            Id = Guid.NewGuid().ToString("N"),
            ParticipantId = participant.Id,
            Label = label,
            ActionKind = request.ActionKind,
            Round = current.Round,
            PhaseInitiative = request.PhaseInitiative ?? participant.CurrentInitiative,
            ActionCost = Math.Clamp(request.ActionCost, 1, 3),
            IsReaction = request.IsReaction,
            IsAdditional = true,
            State = CombatActionEntryState.Open,
            Announcement = request.Announcement?.Trim()
        };
        return (current with { Actions = current.Actions.Append(action).ToArray() },
            $"Zusätzliche Handlung für {participant.Name} angelegt");
    }

    private static (CombatSessionSnapshotDto Snapshot, string Description) AddOpponent(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request)
    {
        var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        if (name is null)
        {
            throw Validation("Gegner brauchen mindestens einen Namen.");
        }

        var initiative = request.Initiative ?? request.InitiativeBase;
        var participant = new CombatSessionParticipantDto
        {
            Id = $"opponent:{Guid.NewGuid():N}",
            Name = name,
            Kind = CombatParticipantKind.Opponent,
            Affiliation = string.IsNullOrWhiteSpace(request.Affiliation) ? "Gegner" : request.Affiliation.Trim(),
            InitiativeBase = request.InitiativeBase,
            CurrentInitiative = initiative,
            InitiativeCorrection = request.Initiative.HasValue && request.InitiativeBase.HasValue
                ? request.Initiative.Value - request.InitiativeBase.Value
                : 0,
            ActionAvailable = initiative.HasValue,
            ReactionAvailable = true
        };
        var actions = initiative.HasValue
            ? current.Actions.Append(CreateNormalAction(participant, current.Round)).ToArray()
            : current.Actions;
        return (current with
        {
            IsStarted = current.IsStarted || initiative.HasValue,
            Participants = current.Participants.Append(participant).ToArray(),
            Actions = actions
        }, $"Gegner {name} hinzugefügt");
    }

    private static (CombatSessionSnapshotDto Snapshot, string Description) NewRound(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto _)
    {
        var openActions = current.Actions.Any(action => action.Round == current.Round &&
                                                         !action.IsReaction &&
                                                         action.State == CombatActionEntryState.Open);
        if (openActions)
        {
            throw Validation("Die offene Runde ist noch nicht abgeschlossen oder gehalten.");
        }

        var round = current.Round + 1;
        var participants = current.Participants
            .Select(participant => participant with
            {
                ActionAvailable = participant.CurrentInitiative.HasValue,
                ReactionAvailable = true,
                IsOriented = false
            })
            .ToArray();
        var participantsById = participants.ToDictionary(participant => participant.Id, StringComparer.Ordinal);
        var actions = current.Actions
            .Select(action => action.State == CombatActionEntryState.Held &&
                              participantsById.TryGetValue(action.ParticipantId, out var participant)
                ? action with
                {
                    Round = round,
                    PhaseInitiative = participant.CurrentInitiative ?? action.PhaseInitiative
                }
                : action)
            .ToList();
        foreach (var participant in participants.Where(item => item.CurrentInitiative.HasValue))
        {
            actions.Add(CreateNormalAction(participant, round));
        }

        return (current with { Round = round, Participants = participants, Actions = actions.ToArray() },
            $"Runde {round} begonnen");
    }

    private static (CombatSessionSnapshotDto Snapshot, string Description) Undo(
        CombatSessionSnapshotDto current,
        PersistedState persisted,
        string userId)
    {
        if (persisted.Undo is null || !string.Equals(persisted.UndoOwnerUserId, userId, StringComparison.Ordinal) ||
            !string.Equals(current.LastMutationUserId, userId, StringComparison.Ordinal))
        {
            throw Validation("Die letzte Änderung kann wegen eines fremden oder fehlenden Folgeschritts nicht sicher zurückgenommen werden.");
        }

        return (persisted.Undo, "Letzte eigene Kampfänderung zurückgenommen");
    }

    private static (CombatSessionSnapshotDto Snapshot, string Description) SetAnnouncement(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request)
    {
        var participant = FindParticipant(current, request.ParticipantId, request.HeroId)
                          ?? throw Validation("Der Teilnehmer für die Ansage wurde nicht gefunden.");
        var announcement = string.IsNullOrWhiteSpace(request.Announcement) ? null : request.Announcement.Trim();
        var announcementEnabled = current.Participants.Any(item =>
            item.Id == participant.Id
                ? announcement is not null
                : !string.IsNullOrWhiteSpace(item.Announcement));
        return (current with
        {
            AnnouncementEnabled = announcementEnabled,
            Participants = ReplaceParticipant(current.Participants, participant with { Announcement = announcement })
        }, announcement is null ? $"Ansage von {participant.Name} entfernt" : $"Ansage von {participant.Name} gespeichert");
    }

    private async Task<(CombatSessionSnapshotDto Snapshot, string Description)> AddOrientationActionAsync(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request,
        string userId,
        CancellationToken cancellationToken)
    {
        var participant = FindParticipant(current, request.ParticipantId, request.HeroId)
                          ?? throw Validation("Der Teilnehmer für Orientieren wurde nicht gefunden.");
        if (!current.IsStarted || !participant.CurrentInitiative.HasValue)
        {
            throw Validation("Orientieren ist erst nach dem ersten Initiativewurf verfügbar.");
        }

        if (current.Actions.Any(action => action.ParticipantId == participant.Id &&
                                          action.Round == current.Round &&
                                          action.Label.Equals("Orientieren", StringComparison.OrdinalIgnoreCase) &&
                                          action.State != CombatActionEntryState.Completed))
        {
            throw Validation("Für diesen Teilnehmer ist in der aktuellen Runde bereits Orientieren offen.");
        }

        var initiativeInfo = await ResolveInitiativeInfoAsync(
            participant,
            request,
            userId,
            cancellationToken);
        var hasAttention = participant.Kind == CombatParticipantKind.Hero
            ? initiativeInfo.HasAttention
            : request.HasAttention;
        var importedRelief = initiativeInfo.KriegskunstValue is { } kriegskunst
            ? Math.Max(0, kriegskunst / 2)
            : 0;
        var orientationRelief = Math.Clamp(request.OrientationRelief ?? importedRelief, 0, 20);

        var action = new CombatSessionActionDto
        {
            Id = Guid.NewGuid().ToString("N"),
            ParticipantId = participant.Id,
            Label = "Orientieren",
            Round = current.Round,
            PhaseInitiative = participant.CurrentInitiative,
            ActionCost = hasAttention ? 1 : 2,
            RequiresCheck = !hasAttention,
            OrientationRelief = orientationRelief,
            OrientationUninterrupted = request.OrientationUninterrupted,
            State = CombatActionEntryState.Open,
            Announcement = hasAttention
                ? request.OrientationUninterrupted
                    ? "Aufmerksamkeit: 1 Aktion · ungestört möglich"
                    : "Aufmerksamkeit: 1 Aktion · nicht ungestört"
                : request.OrientationUninterrupted
                    ? $"2 Aktionen · IN-Probe · Kriegskunst-Erleichterung +{orientationRelief}"
                    : "2 Aktionen · nicht ungestört"
        };
        return (current with { Actions = current.Actions.Append(action).ToArray() },
            hasAttention
                ? $"Orientieren für {participant.Name} als 1 Aktion angelegt"
                : $"Orientieren für {participant.Name} als 2 Aktionen mit IN-Probe angelegt");
    }

    private async Task<InitiativeProfileInfo> ResolveInitiativeInfoAsync(
        CombatSessionParticipantDto participant,
        CombatSessionMutationRequestDto request,
        string userId,
        CancellationToken cancellationToken)
    {
        if (participant.Kind == CombatParticipantKind.Opponent)
        {
        return new InitiativeProfileInfo(
            request.InitiativeBase ?? participant.InitiativeBase,
            Math.Clamp(participant.InitiativeDiceCount, 1, 2),
            request.HasAttention,
            null,
            null,
            participant.InitiativeSetId,
            0,
            []);
        }

        if (!participant.HeroId.HasValue || string.IsNullOrWhiteSpace(participant.OwnerUserId))
        {
            return new InitiativeProfileInfo(null, 1, false, null, null, null, 0, []);
        }

        var profile = await ReadCombatProfileAsync(participant, cancellationToken);
        var selectedSet = string.IsNullOrWhiteSpace(request.SetId)
            ? profile?.Sets.FirstOrDefault(set => set.Id == participant.InitiativeSetId)
              ?? profile?.Sets
                  .Where(set => set.IsInUse)
                  .OrderByDescending(set => set.IsDefault)
                  .FirstOrDefault()
              ?? profile?.Sets.FirstOrDefault(set => set.IsDefault)
              ?? profile?.Sets.FirstOrDefault()
            : profile?.Sets.FirstOrDefault(set => set.Id == request.SetId);
        if (!string.IsNullOrWhiteSpace(request.SetId) && selectedSet is null)
        {
            throw Validation("Das ausgewählte Kampfset ist im importierten Profil nicht vorhanden.");
        }
        var runtime = CombatRuntimeModifierRules.ResolveInitiative(
            profile,
            selectedSet,
            request.RuntimeState ?? participant.RuntimeState);
        return new InitiativeProfileInfo(
            selectedSet?.Initiative,
            profile?.HasKlingentaenzer == true ? 2 : 1,
            profile?.HasAttention == true,
            profile?.KriegskunstValue,
            profile,
            selectedSet?.Id,
            runtime.Modifier,
            runtime.RuleNotes);
    }

    private async Task<CombatProfileDto?> ReadCombatProfileAsync(
        CombatSessionParticipantDto participant,
        CancellationToken cancellationToken)
    {
        if (!participant.HeroId.HasValue || string.IsNullOrWhiteSpace(participant.OwnerUserId))
        {
            return null;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var profileReader = scope.ServiceProvider.GetRequiredService<HeroCombatProfileReader>();
        return await profileReader.ReadAsync(participant.HeroId.Value, participant.OwnerUserId, cancellationToken);
    }

    private AuthorizationResult ResolveAuthorization(
        GameSession session,
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request,
        string userId)
    {
        if (request.Kind is CombatSessionMutationKind.NewRound or CombatSessionMutationKind.AddOpponent)
        {
            return new AuthorizationResult(string.Equals(session.MasterUserId, userId, StringComparison.Ordinal),
                "Nur der Sitzungsleiter darf die Runde und Gegnerverwaltung ändern.");
        }

        if (request.Kind == CombatSessionMutationKind.Undo && string.Equals(session.MasterUserId, userId, StringComparison.Ordinal))
        {
            return new AuthorizationResult(true, string.Empty);
        }

        if (request.Kind == CombatSessionMutationKind.Undo && current.LastMutationUserId == userId)
        {
            return new AuthorizationResult(true, string.Empty);
        }

        var requestedAction = string.IsNullOrWhiteSpace(request.ActionId)
            ? null
            : current.Actions.FirstOrDefault(action =>
                string.Equals(action.Id, request.ActionId, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(request.ActionId) && requestedAction is null)
        {
            return new AuthorizationResult(false, "Die angeforderte Handlung wurde nicht gefunden.");
        }

        if (requestedAction is not null &&
            !string.IsNullOrWhiteSpace(request.ParticipantId) &&
            !string.Equals(requestedAction.ParticipantId, request.ParticipantId, StringComparison.Ordinal))
        {
            return new AuthorizationResult(false, "Handlung und Teilnehmer passen nicht zusammen.");
        }

        var participant = requestedAction is null
            ? FindParticipant(current, request.ParticipantId, request.HeroId)
            : current.Participants.FirstOrDefault(item =>
                string.Equals(item.Id, requestedAction.ParticipantId, StringComparison.Ordinal));
        if (participant is null)
        {
            return new AuthorizationResult(false, "Der Kampfteilnehmer wurde nicht gefunden.");
        }

        return new AuthorizationResult(
            string.Equals(session.MasterUserId, userId, StringComparison.Ordinal) ||
            string.Equals(participant.OwnerUserId, userId, StringComparison.Ordinal),
            "Du darfst nur den eigenen Teilnehmer ändern.");
    }

    private CombatSessionSnapshotDto Normalize(
        GameSession session,
        CombatSessionSnapshotDto? snapshot,
        string currentUserId)
    {
        var normalized = snapshot is null || !string.Equals(snapshot.SessionId, session.SessionId, StringComparison.Ordinal)
            ? new CombatSessionSnapshotDto { SessionId = session.SessionId }
            : snapshot;

        var participants = normalized.Participants.ToList();
        var activeHeroIds = session.Players
            .Where(player => player.ActiveHeroId.HasValue)
            .Select(player => player.ActiveHeroId!.Value)
            .ToHashSet();

        foreach (var player in session.Players.Where(player => player.ActiveHeroId.HasValue))
        {
            var heroId = player.ActiveHeroId!.Value;
            var participantId = HeroParticipantId(heroId);
            var existing = participants.FirstOrDefault(item => item.Id == participantId);
            if (existing is null)
            {
                participants.Add(new CombatSessionParticipantDto
                {
                    Id = participantId,
                    Name = player.ActiveHeroName ?? player.Name,
                    Kind = CombatParticipantKind.Hero,
                    HeroId = heroId,
                    OwnerUserId = player.UserId,
                    Affiliation = "Helden",
                    IsOnline = string.Equals(player.UserId, currentUserId, StringComparison.Ordinal),
                    ReactionAvailable = true
                });
            }
            else
            {
                var index = participants.IndexOf(existing);
                participants[index] = existing with
                {
                    Name = player.ActiveHeroName ?? player.Name,
                    HeroId = heroId,
                    OwnerUserId = player.UserId,
                    Kind = CombatParticipantKind.Hero,
                    IsOnline = existing.IsOnline || string.Equals(player.UserId, currentUserId, StringComparison.Ordinal)
                };
            }
        }

        participants = participants
            .Where(item => item.Kind == CombatParticipantKind.Opponent ||
                           (item.HeroId.HasValue && activeHeroIds.Contains(item.HeroId.Value)))
            .ToList();

        var actionParticipants = participants.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var actions = normalized.Actions
            .Where(action => actionParticipants.Contains(action.ParticipantId))
            .Select(action => action with
            {
                ActionCost = Math.Clamp(action.ActionCost, 1, 3),
                OrientationRelief = Math.Clamp(action.OrientationRelief, 0, 20),
                Label = string.IsNullOrWhiteSpace(action.Label) ? "Handlung" : action.Label,
                State = Enum.IsDefined(action.State) ? action.State : CombatActionEntryState.Open
            })
            .ToArray();

        return FinalizeSnapshot(normalized with
        {
            SessionId = session.SessionId,
            Round = Math.Max(1, normalized.Round),
            Participants = participants
                .Select(participant => participant with
                {
                    InitiativeDiceCount = Math.Clamp(participant.InitiativeDiceCount, 1, 2),
                    RecoverableInitiativeLoss = Math.Max(0, participant.RecoverableInitiativeLoss),
                    InitiativeRuntimeNotes = participant.InitiativeRuntimeNotes ?? []
                })
                .ToArray(),
            Actions = actions,
            CurrentActionId = normalized.CurrentActionId,
            CurrentActionIds = normalized.CurrentActionIds ?? []
        });
    }

    private static CombatSessionSnapshotDto FinalizeSnapshot(CombatSessionSnapshotDto snapshot)
    {
        var participantsById = snapshot.Participants.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var openActions = snapshot.Actions
            .Where(action => action.Round == snapshot.Round && !action.IsReaction &&
                             action.State == CombatActionEntryState.Open)
            .OrderByDescending(action => EffectiveInitiative(action, participantsById))
            .ThenByDescending(action => BaseInitiative(action, participantsById))
            .ThenBy(action => action.Id, StringComparer.Ordinal)
            .ToArray();
        var current = openActions.FirstOrDefault();
        var currentValue = current is null ? (int?)null : EffectiveInitiative(current, participantsById);
        var currentBase = current is null ? (int?)null : BaseInitiative(current, participantsById);
        var currentIds = current is null
            ? []
            : openActions
                .Where(action => EffectiveInitiative(action, participantsById) == currentValue &&
                                 BaseInitiative(action, participantsById) == currentBase)
                .Select(action => action.Id)
                .ToArray();

        var participants = snapshot.Participants
            .Select(participant => participant with
            {
                ActionAvailable = openActions.Any(action => action.ParticipantId == participant.Id),
                ReactionAvailable = participant.ReactionAvailable
            })
            .ToArray();

        var orderedActions = snapshot.Actions
            .OrderByDescending(action => action.Round == snapshot.Round && action.State == CombatActionEntryState.Open)
            .ThenByDescending(action => EffectiveInitiative(action, participantsById))
            .ThenByDescending(action => action.State == CombatActionEntryState.Held)
            .ThenByDescending(action => action.Round)
            .ThenBy(action => action.Id, StringComparer.Ordinal)
            .ToArray();

        return snapshot with
        {
            CurrentActionId = current?.Id,
            CurrentActionIds = currentIds,
            Participants = participants,
            Actions = orderedActions,
            SpecialResultsEnabled = snapshot.SpecialResultsEnabled
        };
    }

    private static int EffectiveInitiative(CombatSessionActionDto action,
        IReadOnlyDictionary<string, CombatSessionParticipantDto> participants)
    {
        if (participants.TryGetValue(action.ParticipantId, out var participant) && participant.CurrentInitiative.HasValue)
        {
            return participant.CurrentInitiative.Value;
        }

        return action.PhaseInitiative ?? int.MinValue;
    }

    private static int BaseInitiative(CombatSessionActionDto action,
        IReadOnlyDictionary<string, CombatSessionParticipantDto> participants)
    {
        return participants.TryGetValue(action.ParticipantId, out var participant)
            ? participant.InitiativeBase ?? int.MinValue
            : int.MinValue;
    }

    private static CombatSessionActionDto CreateNormalAction(
        CombatSessionParticipantDto participant,
        int round) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            ParticipantId = participant.Id,
            Label = "Normale Handlung",
            Round = round,
            PhaseInitiative = participant.CurrentInitiative,
            ActionCost = 1,
            State = CombatActionEntryState.Open
        };

    private static CombatSessionParticipantDto? FindParticipant(
        CombatSessionSnapshotDto snapshot,
        string? participantId,
        Guid? heroId)
    {
        if (!string.IsNullOrWhiteSpace(participantId))
        {
            return snapshot.Participants.FirstOrDefault(item =>
                string.Equals(item.Id, participantId, StringComparison.Ordinal));
        }

        return heroId.HasValue
            ? snapshot.Participants.FirstOrDefault(item => item.HeroId == heroId)
            : snapshot.Participants.FirstOrDefault(item => item.Kind == CombatParticipantKind.Hero);
    }

    private static CombatSessionActionDto? FindAction(
        CombatSessionSnapshotDto snapshot,
        string? actionId,
        string? participantId)
    {
        if (!string.IsNullOrWhiteSpace(actionId))
        {
            return snapshot.Actions.FirstOrDefault(item =>
                string.Equals(item.Id, actionId, StringComparison.Ordinal));
        }

        return snapshot.Actions.FirstOrDefault(item =>
            item.Round == snapshot.Round &&
            item.ParticipantId == participantId &&
            !item.IsReaction &&
            item.State == CombatActionEntryState.Open);
    }

    private static CombatSessionParticipantDto[] ReplaceParticipant(
        IEnumerable<CombatSessionParticipantDto> participants,
        CombatSessionParticipantDto replacement) => participants
        .Select(item => item.Id == replacement.Id ? replacement : item)
        .ToArray();

    private static CombatSessionActionDto[] ReplaceAction(
        IEnumerable<CombatSessionActionDto> actions,
        CombatSessionActionDto replacement) => actions
        .Select(item => item.Id == replacement.Id ? replacement : item)
        .ToArray();

    private PersistedState Load(GameSession session)
    {
        if (string.IsNullOrWhiteSpace(session.CombatStateJson))
        {
            return new PersistedState(new CombatSessionSnapshotDto { SessionId = session.SessionId }, null, null, []);
        }

        try
        {
            return JsonSerializer.Deserialize<PersistedState>(session.CombatStateJson, JsonOptions) ??
                   new PersistedState(new CombatSessionSnapshotDto { SessionId = session.SessionId }, null, null, []);
        }
        catch (JsonException)
        {
            return new PersistedState(new CombatSessionSnapshotDto { SessionId = session.SessionId }, null, null, []);
        }
    }

    private void Save(GameSession session, PersistedState state)
    {
        session.CombatStateJson = JsonSerializer.Serialize(state, JsonOptions);
        recordStore.SaveCombatState(session.SessionId, session.CombatStateJson);
    }

    private static CombatSessionMutationResultDto Result(
        CombatSessionMutationRequestDto request,
        CombatSessionSnapshotDto snapshot,
        bool applied,
        bool alreadyApplied,
        bool stale,
        string message,
        IReadOnlyList<DiceRollDto>? rolls = null) => new(
        request.RequestId,
        applied,
        alreadyApplied,
        stale,
        message,
        snapshot)
    {
        Rolls = rolls?.ToArray() ?? []
    };

    private static string HeroParticipantId(Guid heroId) => $"hero:{heroId:N}";

    private static void EnsureMaster(GameSession session, string userId)
    {
        if (!string.Equals(session.MasterUserId, userId, StringComparison.Ordinal))
        {
            throw new RequestRejectedException(RequestRejectionReason.Forbidden,
                "Nur der Sitzungsleiter darf diese Kampfänderung ausführen.");
        }
    }

    private static RequestRejectedException Validation(string message) =>
        new(RequestRejectionReason.Validation, message);

    private static string FormatSigned(int value) => value > 0 ? $"+{value}" : value.ToString();

    private static void ValidateRuntimeState(CombatRuntimeStateDto? state)
    {
        if (state is null)
        {
            return;
        }

        if (state.CurrentLeP is < -1_000_000 or > 1_000_000)
        {
            throw Validation("Der aktuelle LeP-Wert liegt außerhalb des zulässigen Bereichs.");
        }

        if (state.CurrentAuP is < 0 or > 1_000_000)
        {
            throw Validation("Der aktuelle AuP-Wert liegt außerhalb des zulässigen Bereichs.");
        }

        foreach (var wound in state.Wounds ?? [])
        {
            if (!Enum.IsDefined(wound.Key) || wound.Value is < 0 or > 3)
            {
                throw Validation("Der übertragene Wundstand ist ungültig.");
            }
        }
    }

    private sealed record PersistedState(
        CombatSessionSnapshotDto Current,
        CombatSessionSnapshotDto? Undo,
        string? UndoOwnerUserId,
        Guid[] AppliedRequestIds);

    private sealed record InitiativeProfileInfo(
        int? BaseValue,
        int DiceCount,
        bool HasAttention,
        int? KriegskunstValue,
        CombatProfileDto? Profile,
        string? SetId,
        int RuntimeModifier,
        string[] RuntimeNotes);

    private sealed record AuthorizationResult(bool Allowed, string Message);
}
