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
    IServiceScopeFactory scopeFactory,
    CombatEnemyProfileAdapter enemyAdapter)
{
    private const int MaxAppliedRequestIds = 128;
    private const int MaxCachedRollResults = 128;
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

    public async Task<CombatRollResultDto?> GetCachedRollAsync(
        string sessionId,
        Guid requestId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || requestId == Guid.Empty)
        {
            return null;
        }

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var session = runtimeState.GetMemberSession(sessionId, userId);
            var persisted = Load(session);
            return (persisted.CachedRolls ?? [])
                .FirstOrDefault(result => result.RequestId == requestId);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task CacheRollResultAsync(
        CombatRollRequestDto request,
        CombatRollResultDto result,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) || request.RequestId == Guid.Empty)
        {
            return;
        }

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var session = runtimeState.GetMemberSession(request.SessionId, userId);
            var persisted = Load(session);
            if ((persisted.CachedRolls ?? []).Any(cached => cached.RequestId == request.RequestId))
            {
                return;
            }

            var cachedRolls = (persisted.CachedRolls ?? [])
                .Append(result)
                .TakeLast(MaxCachedRollResults)
                .ToArray();
            Save(session, persisted with { CachedRolls = cachedRolls });
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task EnsureRollAvailabilityAsync(
        CombatRollRequestDto request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SessionId) ||
            (!CombatActionBudgetRules.RequiresNormalAction(request.Action) &&
             !CombatActionBudgetRules.RequiresReaction(request.Action)))
        {
            return;
        }

        if (IsAttackAction(request.Action))
        {
            await EnsureAttackRollAvailabilityAsync(request, userId, cancellationToken);
            return;
        }

        if (CombatActionBudgetRules.RequiresReaction(request.Action))
        {
            await EnsureDefenseRollAvailabilityAsync(request, userId, cancellationToken);
            return;
        }

        var snapshot = await GetAsync(request.SessionId, userId, cancellationToken);
        var participant = FindParticipant(snapshot, request.ParticipantId, request.HeroId)
                          ?? throw Validation("Der eigene Kampfteilnehmer wurde nicht gefunden.");
        if (!IsCombatEligible(participant))
        {
            throw Validation("Dieser Teilnehmer ist nicht mehr kampffaehig.");
        }
        var budget = participant.ActionBudget ?? new CombatActionBudgetDto();
        if (CombatActionBudgetRules.RequiresNormalAction(request.Action) && !budget.HasNormalAction)
        {
            throw Validation($"Für {participant.Name} ist keine normale Aktion mehr verfügbar.");
        }

        if (CombatActionBudgetRules.RequiresReaction(request.Action) && !budget.HasReaction)
        {
            throw Validation($"Für {participant.Name} ist keine Reaktion mehr verfügbar.");
        }
    }

    public async Task EnsureParticipantRollAccessAsync(
        CombatRollRequestDto request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.ParticipantId))
        {
            return;
        }

        var snapshot = await GetAsync(request.SessionId, userId, cancellationToken);
        var participant = snapshot.Participants.FirstOrDefault(item =>
            string.Equals(item.Id, request.ParticipantId.Trim(), StringComparison.Ordinal));
        if (participant is null || !MatchesRequestedParticipant(participant, request))
        {
            throw Validation("Der Kampfwurf gehÃ¶rt nicht zum ausgewÃ¤hlten Teilnehmer.");
        }

        var session = runtimeState.GetMemberSession(request.SessionId, userId);
        if (!string.Equals(session.MasterUserId, userId, StringComparison.Ordinal) &&
            !string.Equals(participant.OwnerUserId, userId, StringComparison.Ordinal))
        {
            throw new RequestRejectedException(RequestRejectionReason.Forbidden,
                "Du darfst nur den eigenen Kampfwurf ausfÃ¼hren.");
        }
    }

    public async Task EnsureAttackRollAvailabilityAsync(
        CombatRollRequestDto request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SessionId) || !IsAttackAction(request.Action))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ExchangeId))
        {
            throw Validation("Ein Session-AT-Wurf braucht eine offene ExchangeId.");
        }

        var snapshot = await GetAsync(request.SessionId, userId, cancellationToken);
        var exchange = snapshot.ActiveExchange;
        if (exchange is null || !string.Equals(exchange.ExchangeId, request.ExchangeId.Trim(), StringComparison.Ordinal))
        {
            throw Validation("Für diesen AT-Wurf gibt es keinen passenden offenen Angriffsaustausch.");
        }

        if (exchange.Status != CombatExchangeStatus.Declared || exchange.AttackResult is not null)
        {
            throw Validation("Der AT-Wurf für diesen Angriff wurde bereits ausgeführt oder ist nicht mehr offen.");
        }

        if (request.ExpectedRevision.HasValue && request.ExpectedRevision.Value != snapshot.Revision)
        {
            throw Validation($"Der Kampfstand ist inzwischen bei Revision {snapshot.Revision}. Bitte den aktuellen Stand laden.");
        }

        if (exchange.AttackKind != request.Action ||
            !string.Equals(exchange.SetId, request.SetId, StringComparison.Ordinal) ||
            !string.Equals(exchange.WeaponId, request.WeaponId, StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(request.TargetParticipantId) &&
             !string.Equals(exchange.TargetParticipantId, request.TargetParticipantId, StringComparison.Ordinal)) ||
            (!string.IsNullOrWhiteSpace(exchange.ActionId) &&
             !string.IsNullOrWhiteSpace(request.ActionId) &&
             !string.Equals(exchange.ActionId, request.ActionId, StringComparison.Ordinal)))
        {
            throw Validation("Der AT-Wurf passt nicht zur deklarierten Attacke.");
        }

        var attacker = snapshot.Participants.FirstOrDefault(participant =>
            string.Equals(participant.Id, exchange.AttackerParticipantId, StringComparison.Ordinal));
        if (attacker is null || !IsCombatEligible(attacker) || !MatchesRequestedParticipant(attacker, request))
        {
            throw Validation("Der AT-Wurf gehört nicht zum deklarierten Angreifer.");
        }

        var session = runtimeState.GetMemberSession(request.SessionId, userId);
        if (!string.Equals(session.MasterUserId, userId, StringComparison.Ordinal) &&
            !string.Equals(attacker.OwnerUserId, userId, StringComparison.Ordinal))
        {
            throw new RequestRejectedException(RequestRejectionReason.Forbidden,
                "Du darfst nur den eigenen Angriff würfeln.");
        }
    }

    public async Task EnsureDefenseRollAvailabilityAsync(
        CombatRollRequestDto request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SessionId) || !IsDefenseAction(request.Action))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ExchangeId))
        {
            throw Validation("Eine Session-Abwehr braucht eine offene ExchangeId.");
        }

        var snapshot = await GetAsync(request.SessionId, userId, cancellationToken);
        var exchange = snapshot.ActiveExchange;
        if (exchange is null || !string.Equals(exchange.ExchangeId, request.ExchangeId.Trim(), StringComparison.Ordinal))
        {
            throw Validation("Für diese Abwehr gibt es keinen passenden Angriffsaustausch.");
        }

        if (exchange.Status != CombatExchangeStatus.DefenseOpen || exchange.DefenseResult is not null)
        {
            throw Validation("Für diesen Angriff ist keine Abwehrentscheidung mehr offen.");
        }

        if (request.ExpectedRevision.HasValue && request.ExpectedRevision.Value != snapshot.Revision)
        {
            throw Validation($"Der Kampfstand ist inzwischen bei Revision {snapshot.Revision}. Bitte den aktuellen Stand laden.");
        }

        if (!(exchange.AllowedDefenseActions ?? []).Contains(request.Action))
        {
            throw Validation("Diese Reaktion ist für den offenen Angriff nicht zulässig.");
        }

        if ((!string.IsNullOrWhiteSpace(exchange.ActionId) &&
             !string.IsNullOrWhiteSpace(request.ActionId) &&
             !string.Equals(exchange.ActionId, request.ActionId, StringComparison.Ordinal)) ||
            (!string.IsNullOrWhiteSpace(request.TargetParticipantId) &&
             !string.Equals(exchange.AttackerParticipantId, request.TargetParticipantId, StringComparison.Ordinal)))
        {
            throw Validation("Die Abwehr ist nicht dem deklarierten Angriff zugeordnet.");
        }

        var target = snapshot.Participants.FirstOrDefault(participant =>
            string.Equals(participant.Id, exchange.TargetParticipantId, StringComparison.Ordinal));
        if (target is null || !IsCombatEligible(target) || !MatchesRequestedParticipant(target, request))
        {
            throw Validation("Die Abwehr gehört nicht zum Ziel des offenen Angriffs.");
        }

        if (!(target.ActionBudget ?? new CombatActionBudgetDto()).HasReaction)
        {
            throw Validation($"Für {target.Name} ist keine Reaktion mehr verfügbar.");
        }

        var session = runtimeState.GetMemberSession(request.SessionId, userId);
        if (!string.Equals(session.MasterUserId, userId, StringComparison.Ordinal) &&
            !string.Equals(target.OwnerUserId, userId, StringComparison.Ordinal))
        {
            throw new RequestRejectedException(RequestRejectionReason.Forbidden,
                "Du darfst nur die Reaktion des eigenen Ziels würfeln.");
        }
    }

    public async Task<CombatSessionSnapshotDto> BindDefenseRollAsync(
        CombatRollRequestDto request,
        CombatRollResultDto result,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(request.SessionId) || !IsDefenseAction(request.Action))
        {
            throw Validation("Nur eine Session-Abwehr kann an einen Angriffsaustausch gebunden werden.");
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
            var exchange = current.ActiveExchange;
            if (exchange is null || !string.Equals(exchange.ExchangeId, request.ExchangeId?.Trim(), StringComparison.Ordinal))
            {
                throw Validation("Für diese Abwehr gibt es keinen passenden Angriffsaustausch.");
            }

            if (exchange.DefenseResult is not null)
            {
                if (exchange.DefenseRollRequestId == request.RequestId)
                {
                    return current;
                }

                throw Validation("Für diesen Angriff wurde bereits eine andere Reaktion gespeichert.");
            }

            if (exchange.Status != CombatExchangeStatus.DefenseOpen ||
                !(exchange.AllowedDefenseActions ?? []).Contains(request.Action) ||
                result.RequestId != request.RequestId ||
                result.Snapshot.RequestId != request.RequestId ||
                result.Snapshot.ExchangeId != request.ExchangeId ||
                result.Snapshot.SessionId != request.SessionId ||
                result.Snapshot.Action != request.Action ||
                (!string.IsNullOrWhiteSpace(exchange.ActionId) &&
                 !string.IsNullOrWhiteSpace(request.ActionId) &&
                 !string.Equals(exchange.ActionId, request.ActionId, StringComparison.Ordinal)) ||
                (!string.IsNullOrWhiteSpace(request.TargetParticipantId) &&
                 !string.Equals(exchange.AttackerParticipantId, request.TargetParticipantId, StringComparison.Ordinal)))
            {
                throw Validation("Die Abwehr passt nicht zum aktuellen Angriffsaustausch.");
            }

            var target = current.Participants.FirstOrDefault(participant =>
                string.Equals(participant.Id, exchange.TargetParticipantId, StringComparison.Ordinal));
            if (target is null || !IsCombatEligible(target) || !MatchesRequestedParticipant(target, request) ||
                (target.OwnerUserId != userId && !string.Equals(session.MasterUserId, userId, StringComparison.Ordinal)))
            {
                throw new RequestRejectedException(RequestRejectionReason.Forbidden,
                    "Du darfst nur die Reaktion des eigenen Ziels würfeln.");
            }

            var budget = target.ActionBudget ?? new CombatActionBudgetDto();
            if (!budget.HasReaction)
            {
                throw Validation($"Für {target.Name} ist keine Reaktion mehr verfügbar.");
            }

            var attack = exchange.AttackResult
                         ?? throw Validation("Für diesen Angriff fehlt das AT-Ergebnis.");
            var defenseEvaluation = CreateAttackEvaluation(result.Snapshot);
            var attackDecision = CombatRollRules.ResolveAttackDecision(
                attack,
                exchange.AllowedDefenseActions,
                allowUnopposedHit: false);
            var decision = CombatRollRules.ResolveDefenseDecision(attackDecision, defenseEvaluation);
            if (!decision.IsValid || !CombatAttackExchangeRules.CanTransition(exchange.Status, decision.Status))
            {
                throw Validation(decision.ValidationMessage ?? "Die Abwehr konnte nicht ausgewertet werden.");
            }

            var nextRevision = current.Revision + 1;
            var updatedExchange = exchange with
            {
                Revision = nextRevision,
                DefenseRollRequestId = request.RequestId,
                Status = decision.Status,
                SelectedDefenseAction = request.Action,
                DefenseResult = defenseEvaluation,
                HistoryEntryIds = exchange.HistoryEntryIds
                    .Append(result.Snapshot.EntryId)
                    .Distinct()
                    .ToArray(),
                RuleNote = decision.StatusLabel
            };
            var targetBudget = defenseEvaluation.Outcome == CombatOutcome.Lucky
                ? budget
                : budget.ConsumeReaction();
            var next = FinalizeSnapshot(current with
            {
                Revision = nextRevision,
                Participants = ReplaceParticipant(current.Participants, target with
                {
                    ActionBudget = targetBudget
                }),
                ActiveExchange = updatedExchange,
                LastMutationId = request.RequestId,
                LastMutationDescription = $"{request.Action} für {exchange.ExchangeId} gespeichert",
                LastMutationUserId = userId
            });
            var appliedRequestIds = AppendAppliedRequestId(persisted.AppliedRequestIds, request.RequestId);
            next = next with { UndoAvailable = true };
            Save(session, CreateExchangePersistedState(
                next,
                current,
                persisted,
                exchange.ExchangeId,
                userId,
                appliedRequestIds));
            return next;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<CombatSessionSnapshotDto> ApplyDamageAsync(
        CombatRollRequestDto request,
        CombatRollResultDto result,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(request.SessionId) || request.Action != CombatActionKind.Damage)
        {
            throw Validation("Nur ein Session-TP-Wurf kann einen offenen Treffer abschließen.");
        }

        if (string.IsNullOrWhiteSpace(request.ExchangeId))
        {
            throw Validation("Ein Session-TP-Wurf braucht eine offene ExchangeId.");
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
            var exchange = current.ActiveExchange;
            if (exchange is null || !string.Equals(exchange.ExchangeId, request.ExchangeId.Trim(), StringComparison.Ordinal))
            {
                throw Validation("Für diesen TP-Wurf gibt es keinen passenden Angriffsaustausch.");
            }

            if (exchange.Damage is not null)
            {
                return current;
            }

            if (exchange.Status is not (CombatExchangeStatus.Hit or CombatExchangeStatus.DamageOpen) ||
                result.RequestId != request.RequestId ||
                result.Snapshot.RequestId != request.RequestId ||
                !string.Equals(result.Snapshot.SessionId, request.SessionId, StringComparison.Ordinal) ||
                !string.Equals(result.Snapshot.ExchangeId, request.ExchangeId, StringComparison.Ordinal) ||
                result.Snapshot.Action != CombatActionKind.Damage ||
                result.Snapshot.Damage is null ||
                (exchange.Zone is null && result.Snapshot.Zone is not { WoundZone: not null }) ||
                (exchange.Zone is not null && result.Snapshot.Zone is { WoundZone: null }))
            {
                throw Validation("Der TP-Wurf enthält keine vollständige Trefferfolge für diesen Angriff.");
            }

            var attacker = current.Participants.FirstOrDefault(participant =>
                string.Equals(participant.Id, exchange.AttackerParticipantId, StringComparison.Ordinal));
            if (attacker is null ||
                !IsCombatEligible(attacker) ||
                !MatchesRequestedParticipant(attacker, request) ||
                (attacker.OwnerUserId != userId && !string.Equals(session.MasterUserId, userId, StringComparison.Ordinal)))
            {
                throw new RequestRejectedException(RequestRejectionReason.Forbidden,
                    "Du darfst nur den eigenen Treffer anwenden.");
            }

            var target = current.Participants.FirstOrDefault(participant =>
                string.Equals(participant.Id, exchange.TargetParticipantId, StringComparison.Ordinal));
            if (target is null)
            {
                throw Validation("Das Ziel des offenen Angriffsaustauschs wurde nicht gefunden.");
            }

            var zone = exchange.Zone ?? result.Snapshot.Zone!;
            if (exchange.Zone is { } storedExchangeZone &&
                result.Snapshot.Zone is { } suppliedZone &&
                (storedExchangeZone.W20 != suppliedZone.W20 ||
                 storedExchangeZone.ArmorZone != suppliedZone.ArmorZone ||
                 storedExchangeZone.WoundZone != suppliedZone.WoundZone ||
                 storedExchangeZone.Facing != suppliedZone.Facing))
            {
                throw Validation("Die Trefferzone des TP-Wurfs passt nicht zum Angriffsaustausch.");
            }
            var targetProfile = target.Kind == CombatParticipantKind.Hero
                ? await ReadCombatProfileAsync(target, cancellationToken)
                : null;
            var targetSet = targetProfile is null
                ? null
                : ResolveParticipantSet(targetProfile, target, null);
            var armorRating = target.Kind == CombatParticipantKind.Opponent
                ? target.OpponentProfile?.ArmorRating
                : zone.ArmorZone is { } armorZone
                    ? CombatZoneRules.ResolveArmorRating(targetSet, armorZone)
                    : null;
            if (!armorRating.HasValue)
            {
                throw Validation("Für das Ziel ist kein gültiger RS zur Trefferzone bekannt.");
            }

            var rawDamage = result.Snapshot.Damage;
            var calculation = CombatRollRules.CalculateDamage(
                rawDamage.DiceTotal,
                rawDamage.WeaponBonus,
                rawDamage.PreMultiplierModifier,
                rawDamage.Multiplier,
                rawDamage.PostMultiplierModifier,
                rawDamage.IsCritical,
                armorRating);
            if (calculation.Total != rawDamage.Total)
            {
                throw Validation("Der TP-Wurf enthält eine ungültige Schadenssumme.");
            }

            var runtime = target.RuntimeState;
            var currentLeP = runtime?.CurrentLeP ?? target.OpponentProfile?.LeP;
            var wounds = runtime?.Wounds;
            if (wounds is null && target.Kind == CombatParticipantKind.Opponent)
            {
                wounds = CreateEmptyWounds();
            }

            if (zone.WoundZone is not { } woundZone)
            {
                throw Validation("Für den Treffer ist keine Wundzone bekannt.");
            }
            var currentWounds = wounds is not null && wounds.TryGetValue(woundZone, out var woundValue)
                ? woundValue
                : null;
            var constitution = targetProfile?.Attributes
                .FirstOrDefault(attribute => string.Equals(attribute.Key, "KO", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            var woundThresholds = CombatWoundRules.CreateThresholds(
                target.Kind == CombatParticipantKind.Opponent
                    ? target.OpponentProfile?.WoundThreshold
                    : targetProfile?.WoundThreshold,
                constitution);
            var application = CombatWoundRules.Resolve(
                calculation.StructurePoints,
                currentLeP,
                woundZone,
                currentWounds,
                woundThresholds);

            var nextWounds = wounds is null
                ? null
                : new Dictionary<CombatWoundZone, int?>(wounds);
            if (nextWounds is not null && application.ResultingWounds.HasValue)
            {
                nextWounds[woundZone] = application.ResultingWounds;
            }

            var nextRuntime = (runtime ?? new CombatRuntimeStateDto()) with
            {
                IsStarted = true,
                CurrentLeP = application.LePAfter,
                Wounds = nextWounds ?? []
            };
            var targetBudget = target.ActionBudget ?? new CombatActionBudgetDto();
            if (application.IsIncapacitated)
            {
                targetBudget = targetBudget with
                {
                    NormalActionsRemaining = 0,
                    ReactionsRemaining = 0,
                    FreeActionAvailable = false,
                    HeldActionId = null,
                    HeldActionRound = null
                };
            }

            var nextRevision = current.Revision + 1;
            var storedDamage = rawDamage with
            {
                ArmorRating = armorRating,
                StructurePoints = calculation.StructurePoints
            };
            var storedZone = zone with { ArmorRating = armorRating };
            var updatedExchange = exchange with
            {
                Revision = nextRevision,
                Status = CombatExchangeStatus.Completed,
                Zone = storedZone,
                Damage = storedDamage,
                HistoryEntryIds = exchange.HistoryEntryIds
                    .Append(result.Snapshot.EntryId)
                    .Distinct()
                    .ToArray(),
                RuleNote = application.IsIncapacitated
                    ? $"{calculation.StructurePoints} SP; {target.Name} ist handlungsunfähig."
                    : $"{calculation.StructurePoints} SP auf {target.Name}."
            };
            if (exchange.Status == CombatExchangeStatus.Hit &&
                !CombatAttackExchangeRules.CanTransition(exchange.Status, CombatExchangeStatus.DamageOpen))
            {
                throw Validation("Der Treffer kann keine Schadensfolge öffnen.");
            }

            if (!CombatAttackExchangeRules.CanTransition(CombatExchangeStatus.DamageOpen, CombatExchangeStatus.Completed))
            {
                throw Validation("Die Schadensfolge kann nicht abgeschlossen werden.");
            }

            var next = FinalizeSnapshot(current with
            {
                Revision = nextRevision,
                Participants = ReplaceParticipant(current.Participants, target with
                {
                    RuntimeState = nextRuntime,
                    ActionBudget = targetBudget
                }),
                ActiveExchange = updatedExchange,
                LastMutationId = request.RequestId,
                LastMutationDescription = $"Schadensfolge für {exchange.ExchangeId} gespeichert",
                LastMutationUserId = userId
            });
            var appliedRequestIds = AppendAppliedRequestId(persisted.AppliedRequestIds, request.RequestId);
            next = next with { UndoAvailable = true };
            Save(session, CreateExchangePersistedState(
                next,
                current,
                persisted,
                exchange.ExchangeId,
                userId,
                appliedRequestIds));
            return next;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<CombatAutomaticHitResolutionDto?> ResolveAutomaticHitAsync(
        CombatRollRequestDto request,
        CombatRollResultDto result,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(request.SessionId) ||
            (IsAttackAction(request.Action) == false && !IsDefenseAction(request.Action)))
        {
            return null;
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
            var exchange = current.ActiveExchange;
            if (exchange is null || exchange.Status != CombatExchangeStatus.Hit || exchange.Damage is not null ||
                exchange.AttackResult is null ||
                (exchange.AllowedDefenseActions.Length > 0 && exchange.DefenseResult is null))
            {
                return null;
            }

            var attacker = current.Participants.FirstOrDefault(participant =>
                string.Equals(participant.Id, exchange.AttackerParticipantId, StringComparison.Ordinal));
            var target = current.Participants.FirstOrDefault(participant =>
                string.Equals(participant.Id, exchange.TargetParticipantId, StringComparison.Ordinal));
            if (attacker is null || target is null)
            {
                return null;
            }

            var attackInfo = await ResolveAutomaticAttackInfoAsync(attacker, exchange, cancellationToken);
            if (attackInfo is null)
            {
                return null;
            }

            var targetProfile = target.Kind == CombatParticipantKind.Hero
                ? await ReadCombatProfileAsync(target, cancellationToken)
                : null;
            var targetSet = targetProfile is null
                ? null
                : ResolveParticipantSet(targetProfile, target, null);
            var zoneRequest = request.Zone ?? new CombatZoneRollRequestDto(
                exchange.Facing,
                CombatArmorZone.LeftArm,
                CombatArmorZone.RightArm);
            var fixedW20 = request.ResolvedZone?.W20;
            var w20 = fixedW20 ?? diceService.RollDice([new DiceRollGroupDto(20, 1)])[0].Value;
            var mappedZone = CombatZoneRules.ResolveHitZone(w20, zoneRequest);
            var armorRating = target.Kind == CombatParticipantKind.Opponent
                ? CombatZoneRules.ResolveEnemyArmorRating(target.OpponentProfile?.Armor, mappedZone.ArmorZone)
                  ?? target.OpponentProfile?.ArmorRating
                : CombatZoneRules.ResolveArmorRating(targetSet, mappedZone.ArmorZone);
            if (!armorRating.HasValue || !CombatRollRules.TryParseDamageNotation(
                    attackInfo.DamageNotation,
                    out var diceCount,
                    out var diceSides,
                    out var weaponBonus))
            {
                return null;
            }

            var damageRolls = diceService.RollDice([new DiceRollGroupDto(diceSides, diceCount)]);
            var damageRequest = request.Damage;
            var preMultiplierModifier = damageRequest?.PreMultiplierModifier ?? 0;
            var multiplier = Math.Max(1, damageRequest?.Multiplier ?? 1);
            var postMultiplierModifier = damageRequest is null
                ? request.DamageModifier
                : damageRequest.PostMultiplierModifier + request.DamageModifier;
            var calculation = CombatRollRules.CalculateDamage(
                damageRolls.Sum(roll => roll.Value),
                weaponBonus,
                preMultiplierModifier,
                multiplier,
                postMultiplierModifier,
                exchange.AttackResult.IsCritical,
                armorRating);
            if (!calculation.StructurePoints.HasValue)
            {
                return null;
            }

            var woundZone = mappedZone.WoundZone;

            var runtime = target.RuntimeState;
            var currentLeP = runtime?.CurrentLeP ??
                             (target.Kind == CombatParticipantKind.Opponent
                                 ? target.OpponentProfile?.LeP
                                 : null);
            var wounds = runtime?.Wounds ?? CreateEmptyWounds();
            var currentWounds = wounds.TryGetValue(woundZone, out var woundValue) ? woundValue : null;
            var constitution = targetProfile?.Attributes
                .FirstOrDefault(attribute => string.Equals(attribute.Key, "KO", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            var thresholds = CombatWoundRules.CreateThresholds(
                target.Kind == CombatParticipantKind.Opponent
                    ? target.OpponentProfile?.WoundThreshold
                    : targetProfile?.WoundThreshold,
                constitution);
            var application = CombatWoundRules.Resolve(
                calculation.StructurePoints,
                currentLeP,
                woundZone,
                currentWounds,
                thresholds);

            var nextWounds = new Dictionary<CombatWoundZone, int?>(wounds);
            if (application.ResultingWounds.HasValue)
            {
                nextWounds[woundZone] = application.ResultingWounds;
            }

            var nextRuntime = (runtime ?? new CombatRuntimeStateDto()) with
            {
                IsStarted = true,
                CurrentLeP = application.LePAfter,
                Wounds = nextWounds
            };
            var targetBudget = target.ActionBudget ?? new CombatActionBudgetDto();
            if (application.IsIncapacitated)
            {
                targetBudget = targetBudget with
                {
                    NormalActionsRemaining = 0,
                    ReactionsRemaining = 0,
                    FreeActionAvailable = false,
                    HeldActionId = null,
                    HeldActionRound = null
                };
            }

            var zone = new CombatZoneSnapshotDto(
                w20,
                mappedZone.ArmorZone,
                mappedZone.WoundZone,
                exchange.Facing,
                armorRating);
            var damage = new CombatDamageSnapshotDto(
                calculation.DiceTotal,
                calculation.WeaponBonus,
                calculation.PreMultiplierModifier,
                calculation.Multiplier,
                calculation.PostMultiplierModifier,
                calculation.Total,
                calculation.IsCritical,
                calculation.ArmorRating,
                calculation.StructurePoints);
            var ruleNotes = new List<string>
            {
                "Trefferzone und TP nach feststehendem Treffer automatisch ermittelt.",
                $"Trefferzone: {FormatWoundZone(woundZone)}",
                $"RS {armorRating.Value}; SP {calculation.StructurePoints.Value}",
                currentLeP.HasValue && application.LePAfter.HasValue
                    ? $"LeP {currentLeP.Value} → {application.LePAfter.Value}"
                    : "LeP-Verlust gespeichert; ein Ausgangswert ist nicht bekannt."
            };
            if (application.AddedWounds > 0)
            {
                ruleNotes.Add($"Wunden +{application.AddedWounds} ({FormatWoundZone(woundZone)})");
            }

            var nextRevision = current.Revision + 1;
            var updatedExchange = exchange with
            {
                Revision = nextRevision,
                Status = CombatExchangeStatus.Completed,
                Zone = zone,
                Damage = damage,
                RuleNote = string.Join("; ", ruleNotes)
            };
            var next = FinalizeSnapshot(current with
            {
                Revision = nextRevision,
                Participants = ReplaceParticipant(current.Participants, target with
                {
                    RuntimeState = nextRuntime,
                    ActionBudget = targetBudget
                }),
                ActiveExchange = updatedExchange,
                LastMutationId = request.RequestId,
                LastMutationDescription = $"Trefferfolge für {exchange.ExchangeId} automatisch gespeichert",
                LastMutationUserId = userId
            }) with { UndoAvailable = true };
            Save(session, CreateExchangePersistedState(
                next,
                current,
                persisted,
                exchange.ExchangeId,
                userId,
                persisted.AppliedRequestIds));

            var automaticRolls = new List<DiceRollDto>();
            if (!fixedW20.HasValue)
            {
                automaticRolls.Add(new DiceRollDto(20, w20));
            }

            automaticRolls.AddRange(damageRolls);
            return new CombatAutomaticHitResolutionDto(
                next,
                automaticRolls.ToArray(),
                zone,
                damage,
                application,
                ruleNotes.ToArray(),
                !fixedW20.HasValue);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<CombatSessionSnapshotDto> BindHitZoneAsync(
        CombatRollRequestDto request,
        CombatRollResultDto result,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(request.SessionId) || request.Action != CombatActionKind.HitZone)
        {
            throw Validation("Nur ein Session-Trefferzonenwurf kann einen offenen Treffer fortsetzen.");
        }

        if (string.IsNullOrWhiteSpace(request.ExchangeId))
        {
            throw Validation("Ein Session-Trefferzonenwurf braucht eine offene ExchangeId.");
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
            var exchange = current.ActiveExchange;
            if (exchange is null || !string.Equals(exchange.ExchangeId, request.ExchangeId.Trim(), StringComparison.Ordinal))
            {
                throw Validation("Für diesen Trefferzonenwurf gibt es keinen passenden Angriffsaustausch.");
            }

            if (exchange.Zone is not null)
            {
                return current;
            }

            if (exchange.Status != CombatExchangeStatus.Hit ||
                result.RequestId != request.RequestId ||
                result.Snapshot.RequestId != request.RequestId ||
                !string.Equals(result.Snapshot.SessionId, request.SessionId, StringComparison.Ordinal) ||
                !string.Equals(result.Snapshot.ExchangeId, request.ExchangeId, StringComparison.Ordinal) ||
                result.Snapshot.Action != CombatActionKind.HitZone ||
                result.Snapshot.Zone is not { WoundZone: not null })
            {
                throw Validation("Der Trefferzonenwurf passt nicht zum aktuellen Angriffsaustausch.");
            }

            var attacker = current.Participants.FirstOrDefault(participant =>
                string.Equals(participant.Id, exchange.AttackerParticipantId, StringComparison.Ordinal));
            if (attacker is null ||
                !MatchesRequestedParticipant(attacker, request) ||
                (attacker.OwnerUserId != userId && !string.Equals(session.MasterUserId, userId, StringComparison.Ordinal)))
            {
                throw new RequestRejectedException(RequestRejectionReason.Forbidden,
                    "Du darfst nur die Trefferzone des eigenen Angriffs würfeln.");
            }

            var target = current.Participants.FirstOrDefault(participant =>
                string.Equals(participant.Id, exchange.TargetParticipantId, StringComparison.Ordinal));
            if (target is null)
            {
                throw Validation("Das Ziel des offenen Angriffsaustauschs wurde nicht gefunden.");
            }

            var zone = result.Snapshot.Zone!;
            var targetProfile = target.Kind == CombatParticipantKind.Hero
                ? await ReadCombatProfileAsync(target, cancellationToken)
                : null;
            var targetSet = targetProfile is null
                ? null
                : ResolveParticipantSet(targetProfile, target, null);
            var armorRating = target.Kind == CombatParticipantKind.Opponent
                ? target.OpponentProfile?.ArmorRating
                : zone.ArmorZone is { } armorZone
                    ? CombatZoneRules.ResolveArmorRating(targetSet, armorZone)
                    : null;
            if (!armorRating.HasValue)
            {
                throw Validation("Für das Ziel ist kein gültiger RS zur Trefferzone bekannt.");
            }

            if (!CombatAttackExchangeRules.CanTransition(exchange.Status, CombatExchangeStatus.DamageOpen))
            {
                throw Validation("Der Treffer kann keine Schadensfolge öffnen.");
            }

            var nextRevision = current.Revision + 1;
            var updatedExchange = exchange with
            {
                Revision = nextRevision,
                Status = CombatExchangeStatus.DamageOpen,
                Zone = zone with { ArmorRating = armorRating },
                HistoryEntryIds = exchange.HistoryEntryIds
                    .Append(result.Snapshot.EntryId)
                    .Distinct()
                    .ToArray(),
                RuleNote = "Trefferzone bestimmt; TP-Wurf steht noch aus."
            };
            var next = FinalizeSnapshot(current with
            {
                Revision = nextRevision,
                ActiveExchange = updatedExchange,
                LastMutationId = request.RequestId,
                LastMutationDescription = $"Trefferzone für {exchange.ExchangeId} gespeichert",
                LastMutationUserId = userId
            });
            var appliedRequestIds = AppendAppliedRequestId(persisted.AppliedRequestIds, request.RequestId);
            next = next with { UndoAvailable = true };
            Save(session, CreateExchangePersistedState(
                next,
                current,
                persisted,
                exchange.ExchangeId,
                userId,
                appliedRequestIds));
            return next;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<CombatSessionSnapshotDto> BindAttackRollAsync(
        CombatRollRequestDto request,
        CombatRollResultDto result,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(request.SessionId) || !IsAttackAction(request.Action))
        {
            throw Validation("Nur ein Session-AT-Wurf kann an einen Angriffsaustausch gebunden werden.");
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
            var exchange = current.ActiveExchange;
            if (exchange is null || !string.Equals(exchange.ExchangeId, request.ExchangeId?.Trim(), StringComparison.Ordinal))
            {
                throw Validation("Für diesen AT-Wurf gibt es keinen passenden Angriffsaustausch.");
            }

            if (exchange.AttackResult is not null)
            {
                if (exchange.AttackRollRequestId == request.RequestId)
                {
                    return current;
                }

                throw Validation("Für diesen Angriff wurde bereits ein anderer AT-Wurf gespeichert.");
            }

            if (exchange.Status != CombatExchangeStatus.Declared ||
                exchange.AttackKind != request.Action ||
                result.RequestId != request.RequestId ||
                result.Snapshot.RequestId != request.RequestId ||
                result.Snapshot.ExchangeId != request.ExchangeId ||
                result.Snapshot.SessionId != request.SessionId ||
                result.Snapshot.Action != request.Action ||
                (!string.IsNullOrWhiteSpace(exchange.ActionId) &&
                 !string.IsNullOrWhiteSpace(request.ActionId) &&
                 !string.Equals(exchange.ActionId, request.ActionId, StringComparison.Ordinal)) ||
                (!string.IsNullOrWhiteSpace(request.TargetParticipantId) &&
                 !string.Equals(exchange.TargetParticipantId, request.TargetParticipantId, StringComparison.Ordinal)))
            {
                throw Validation("Der AT-Wurf passt nicht zum aktuellen Angriffsaustausch.");
            }

            var attacker = current.Participants.FirstOrDefault(participant =>
                string.Equals(participant.Id, exchange.AttackerParticipantId, StringComparison.Ordinal));
            if (attacker is null ||
                !MatchesRequestedParticipant(attacker, request) ||
                (attacker.OwnerUserId != userId && !string.Equals(session.MasterUserId, userId, StringComparison.Ordinal)))
            {
                throw new RequestRejectedException(RequestRejectionReason.Forbidden,
                    "Du darfst nur den eigenen Angriff würfeln.");
            }

            var action = current.Actions.FirstOrDefault(item =>
                string.Equals(item.Id, exchange.ActionId ?? request.ActionId, StringComparison.Ordinal));
            if (action is null || action.ParticipantId != attacker.Id || action.IsReaction ||
                action.State is not (CombatActionEntryState.Open or CombatActionEntryState.Held))
            {
                throw Validation("Die normale Handlung für diesen Angriff ist nicht mehr offen.");
            }

            var budget = attacker.ActionBudget ?? new CombatActionBudgetDto();
            var actionCost = Math.Max(1, action.ActionCost);
            if (!action.IsAdditional && budget.NormalActionsRemaining < actionCost)
            {
                throw Validation($"Für {attacker.Name} ist keine normale Aktion mehr verfügbar.");
            }

            var evaluation = CreateAttackEvaluation(result.Snapshot);
            var decision = CombatRollRules.ResolveAttackDecision(
                evaluation,
                exchange.AllowedDefenseActions,
                allowUnopposedHit: true);
            var nextStatus = decision.IsValid ? decision.Status : CombatExchangeStatus.Cancelled;
            if (!CombatAttackExchangeRules.CanTransition(exchange.Status, CombatExchangeStatus.AttackOpen) ||
                !CombatAttackExchangeRules.CanTransition(CombatExchangeStatus.AttackOpen, nextStatus))
            {
                throw Validation("Der Angriffsaustausch kann dieses AT-Ergebnis nicht mehr aufnehmen.");
            }

            var nextRevision = current.Revision + 1;
            var actionConsumed = !action.IsAdditional;
            var completedAction = action with
            {
                State = CombatActionEntryState.Completed,
                CompletedAt = DateTimeOffset.UtcNow
            };
            var updatedExchange = exchange with
            {
                Revision = nextRevision,
                AttackRollRequestId = request.RequestId,
                Status = nextStatus,
                AttackResult = evaluation,
                ActionConsumed = actionConsumed,
                HistoryEntryIds = exchange.HistoryEntryIds
                    .Append(result.Snapshot.EntryId)
                    .Distinct()
                    .ToArray(),
                RuleNote = decision.ValidationMessage ?? decision.StatusLabel
            };
            var next = FinalizeSnapshot(current with
            {
                Revision = nextRevision,
                Participants = actionConsumed
                    ? ReplaceParticipant(current.Participants, attacker with
                    {
                        ActionBudget = budget.ConsumeNormalAction(actionCost)
                    })
                    : current.Participants,
                Actions = ReplaceAction(current.Actions, completedAction),
                ActiveExchange = updatedExchange,
                LastMutationId = request.RequestId,
                LastMutationDescription = $"AT-Wurf für {exchange.ExchangeId} gespeichert",
                LastMutationUserId = userId
            });
            var appliedRequestIds = AppendAppliedRequestId(persisted.AppliedRequestIds, request.RequestId);
            next = next with { UndoAvailable = true };
            Save(session, CreateExchangePersistedState(
                next,
                current,
                persisted,
                exchange.ExchangeId,
                userId,
                appliedRequestIds));
            return next;
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

            EnsureNoOpenAttackExchange(current, request.Kind);

            var previous = current;
            var rolls = Array.Empty<DiceRollDto>();
            RollHistoryEntryDto? historyEntry = null;
            CombatSessionSnapshotDto next;
            string description;

            switch (request.Kind)
            {
                case CombatSessionMutationKind.RollInitiative:
                    (next, rolls, description, historyEntry) = await RollInitiativeAsync(
                        session,
                        current,
                        request,
                        userId,
                        cancellationToken);
                    break;
                case CombatSessionMutationKind.SetInitiative:
                    (next, description) = await SetInitiativeAsync(session, current, request, userId, cancellationToken);
                    break;
                case CombatSessionMutationKind.SyncRuntimeState:
                    (next, description) = await SyncRuntimeStateAsync(current, request, userId, cancellationToken);
                    break;
                case CombatSessionMutationKind.DeclareAttack:
                    (next, description) = await DeclareAttackAsync(
                        current,
                        request,
                        userId,
                        cancellationToken);
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
                    (next, description) = request.EnemySelection is null
                        ? AddOpponent(current, request)
                        : AddCatalogOpponent(current, request, enemyAdapter);
                    break;
                case CombatSessionMutationKind.RemoveOpponent:
                    EnsureMaster(session, userId);
                    (next, description) = RemoveOpponent(current, request);
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

            if (historyEntry is null && request.Kind != CombatSessionMutationKind.RollInitiative)
            {
                historyEntry = CreateMutationHistoryEntry(
                    session,
                    previous,
                    next,
                    request,
                    description,
                    userId);
            }

            var appliedRequestIds = AppendAppliedRequestId(persisted.AppliedRequestIds, request.RequestId);
            var undoExchangeId = request.Kind == CombatSessionMutationKind.Undo
                ? null
                : ResolveUndoExchangeId(current, next, persisted);
            var keepExchangeUndo = undoExchangeId is not null &&
                                   string.Equals(undoExchangeId, persisted.UndoExchangeId,
                                       StringComparison.Ordinal) &&
                                   persisted.Undo is not null;
            var undo = request.Kind == CombatSessionMutationKind.Undo
                ? null
                : keepExchangeUndo ? persisted.Undo : previous;
            var undoOwner = request.Kind == CombatSessionMutationKind.Undo
                ? null
                : keepExchangeUndo ? persisted.UndoOwnerUserId : userId;
            next = next with { UndoAvailable = undo is not null };
            Save(session, new PersistedState(
                next,
                undo,
                undoOwner,
                appliedRequestIds,
                undoExchangeId,
                persisted.CachedRolls));
            if (historyEntry is not null)
            {
                recordStore.AppendHistoryEntry(session.SessionId, historyEntry);
            }

            return Result(request, next, applied: true, alreadyApplied: false, stale: false, description, rolls) with
            {
                HistoryEntry = historyEntry
            };
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private static RollHistoryEntryDto CreateMutationHistoryEntry(
        GameSession session,
        CombatSessionSnapshotDto previous,
        CombatSessionSnapshotDto next,
        CombatSessionMutationRequestDto request,
        string description,
        string userId)
    {
        var participant = next.Participants.FirstOrDefault(current =>
                           string.Equals(current.Id, request.ParticipantId, StringComparison.Ordinal))
                       ?? previous.Participants.FirstOrDefault(current =>
                           string.Equals(current.Id, request.ParticipantId, StringComparison.Ordinal));
        var relatedEntryId = request.Kind == CombatSessionMutationKind.Undo
            ? previous.ActiveExchange?.HistoryEntryIds.LastOrDefault() ?? previous.LastMutationId
            : null;
        if (relatedEntryId == Guid.Empty)
        {
            relatedEntryId = null;
        }

        var stateChange = new CombatStateChangeDto(
            request.RequestId,
            request.Kind,
            description,
            next.Round,
            participant?.Id ?? request.ParticipantId,
            participant?.Name,
            next.ActiveExchange?.ExchangeId ?? previous.ActiveExchange?.ExchangeId,
            relatedEntryId,
            request.Kind == CombatSessionMutationKind.Undo);
        var playerName = session.Players.FirstOrDefault(player =>
                             string.Equals(player.UserId, userId, StringComparison.Ordinal))?.Name
                         ?? participant?.Name
                         ?? "Kampf";

        return new RollHistoryEntryDto(
            playerName,
            DateTime.UtcNow,
            [],
            0,
            0,
            new RollHistoryContextDto(
                RollHistoryKind.Combat,
                "Kampfstatus",
                RollHistoryOutcome.None,
                null,
                [],
                new RollHistorySnapshotDto
                {
                    ParticipantName = participant?.Name,
                    CombatStateChange = stateChange
                }));
    }

    private async Task<(
        CombatSessionSnapshotDto Snapshot,
        DiceRollDto[] Rolls,
        string Description,
        RollHistoryEntryDto HistoryEntry)> RollInitiativeAsync(
        GameSession session,
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request,
        string userId,
        CancellationToken cancellationToken)
    {
        var participant = FindParticipant(current, request.ParticipantId, request.HeroId)
                          ?? throw Validation("Der Initiative-Teilnehmer wurde nicht gefunden.");
        if (!IsCombatEligible(participant))
        {
            throw Validation("Dieser Teilnehmer ist nicht mehr kampffaehig.");
        }
        if (participant.CurrentInitiative.HasValue || participant.ActionBudget?.HeldActionId is not null)
        {
            throw Validation($"Für {participant.Name} liegt bereits ein Initiativewert vor. Bitte den aktuellen Wert korrigieren.");
        }
        var initiativeInfo = await ResolveInitiativeInfoAsync(participant, request, userId, cancellationToken);
        if (!initiativeInfo.BaseValue.HasValue)
        {
            throw Validation("Für diesen Teilnehmer ist kein Initiative-Basiswert vorhanden.");
        }

        var roll = diceService.RollDice([new DiceRollGroupDto(6, initiativeInfo.DiceCount)]);
        var rollTotal = roll.Sum(result => result.Value);
        var totalModifier = initiativeInfo.RuntimeModifier + request.InitiativeCorrection;
        var finalInitiative = initiativeInfo.BaseValue.Value + rollTotal + totalModifier;
        var updatedParticipant = participant with
        {
            InitiativeBase = initiativeInfo.BaseValue,
            InitiativeSetId = initiativeInfo.SetId,
            InitiativeDiceCount = initiativeInfo.DiceCount,
            StartRoll = rollTotal,
            InitiativeCorrection = request.InitiativeCorrection,
            InitiativeRuntimeModifier = initiativeInfo.RuntimeModifier,
            InitiativeRuntimeNotes = initiativeInfo.RuntimeNotes,
            RuntimeState = request.RuntimeState ?? participant.RuntimeState,
            RecoverableInitiativeLoss = 0,
            CurrentInitiative = finalInitiative,
            ActionBudget = new CombatActionBudgetDto(),
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

        var initiativeSnapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = session.SessionId,
            ParticipantId = participant.Id,
            ParticipantName = participant.Name,
            HeroId = participant.HeroId,
            Action = CombatActionKind.InitiativeHelper,
            ActionLabel = "Initiative",
            ValuesSource = "Regelhilfe",
            BaseValue = initiativeInfo.BaseValue,
            Modifiers = CreateInitiativeModifiers(initiativeInfo.RuntimeModifier, request.InitiativeCorrection),
            Outcome = CombatOutcome.Neutral,
            StatusLabel = "Initiative gewürfelt",
            LabeledRolls = roll
                .Select(result => new CombatLabeledRollDto("INI-Wurf", result.Sides, result.Value))
                .ToArray()
        };
        var historyEntry = new RollHistoryEntryDto(
            session.Players.FirstOrDefault(player => string.Equals(player.UserId, userId, StringComparison.Ordinal))?.Name
                ?? participant.Name,
            DateTime.UtcNow,
            roll,
            initiativeInfo.BaseValue.Value + totalModifier,
            finalInitiative,
            new RollHistoryContextDto(
                RollHistoryKind.Combat,
                "Initiative",
                RollHistoryOutcome.None,
                null,
                [],
                new RollHistorySnapshotDto
                {
                    HeroName = participant.Kind == CombatParticipantKind.Hero ? participant.Name : null,
                    ParticipantName = participant.Name,
                    Combat = initiativeSnapshot
                }));

        return (current with { IsStarted = true, Participants = participants, Actions = actions }, roll,
            current.Participants.Any(existing => existing.Id == participant.Id && existing.CurrentInitiative.HasValue)
                ? $"Startwurf für {participant.Name} ersetzt"
                : $"Startwurf für {participant.Name} gespeichert",
            historyEntry);
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
            ReactionAvailable = participant.ReactionAvailable,
            ActionBudget = participant.ActionBudget ?? new CombatActionBudgetDto()
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

    private async Task<(CombatSessionSnapshotDto Snapshot, string Description)> DeclareAttackAsync(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!current.IsStarted)
        {
            throw Validation("Eine Attacke ist erst nach dem ersten Initiativewurf möglich.");
        }

        if (CombatAttackExchangeRules.IsOpen(current.ActiveExchange))
        {
            throw Validation("Es ist bereits ein offener Angriffsaustausch vorhanden.");
        }

        if (string.IsNullOrWhiteSpace(request.ExchangeId))
        {
            throw Validation("Eine Attacke braucht eine ExchangeId.");
        }

        if (request.ActionKind is not (CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack))
        {
            throw Validation("Nur Nah- oder Fernkampfangriffe können deklariert werden.");
        }

        if (string.IsNullOrWhiteSpace(request.TargetParticipantId))
        {
            throw Validation("Für eine Attacke muss ein Ziel ausgewählt sein.");
        }

        var attacker = FindParticipant(current, request.ParticipantId, request.HeroId)
                        ?? throw Validation("Der Angreifer wurde nicht gefunden.");
        if (!IsCombatEligible(attacker))
        {
            throw Validation("Dieser Teilnehmer ist nicht mehr kampffaehig.");
        }
        var target = current.Participants.FirstOrDefault(participant =>
            string.Equals(participant.Id, request.TargetParticipantId, StringComparison.Ordinal))
                    ?? throw Validation("Das ausgewählte Ziel gehört nicht zu diesem Kampf.");
        if (!CombatTargetRules.IsValidTarget(attacker, target))
        {
            throw Validation("Das ausgewählte Ziel ist für diesen Angriff nicht gültig.");
        }

        if (!attacker.CurrentInitiative.HasValue)
        {
            throw Validation("Der Angreifer hat noch keinen laufenden Initiativewert.");
        }

        var action = FindAction(current, request.ActionId, attacker.Id)
                     ?? throw Validation("Für den Angreifer ist keine offene normale Handlung vorhanden.");
        if (action.Round != current.Round || action.IsReaction ||
            action.State is not (CombatActionEntryState.Open or CombatActionEntryState.Held))
        {
            throw Validation("Die angeforderte Handlung ist für diesen Angriff nicht offen.");
        }

        var heldAction = action.State == CombatActionEntryState.Held;
        if (!heldAction && current.CurrentActionIds.Length > 0 &&
            !current.CurrentActionIds.Contains(action.Id, StringComparer.Ordinal))
        {
            throw Validation("Der Angreifer ist in dieser Initiativephase nicht an der Reihe.");
        }

        var phaseInitiative = action.PhaseInitiative ?? attacker.CurrentInitiative;
        if (request.PhaseInitiative.HasValue && request.PhaseInitiative != phaseInitiative)
        {
            throw Validation("Die angeforderte Initiativephase passt nicht zum aktuellen Kampfstand.");
        }

        var budget = attacker.ActionBudget ?? new CombatActionBudgetDto();
        var actionCost = Math.Max(1, action.ActionCost);
        if (heldAction && !string.Equals(budget.HeldActionId, action.Id, StringComparison.Ordinal))
        {
            throw Validation("Die gehaltene Handlung ist nicht mehr als eigene Reserve vorhanden.");
        }
        if (!action.IsAdditional && budget.NormalActionsRemaining < actionCost)
        {
            throw Validation($"Für {attacker.Name} ist keine normale Aktion mehr verfügbar.");
        }

        var loadout = await ResolveAttackLoadoutAsync(attacker, request, cancellationToken);
        var allowedDefenseActions = await ResolveAllowedDefenseActionsAsync(target, cancellationToken);
        var exchangeId = request.ExchangeId.Trim();
        var exchange = new CombatAttackExchangeDto
        {
            ExchangeId = exchangeId,
            SessionId = current.SessionId,
            RequestId = request.RequestId,
            Revision = current.Revision + 1,
            AttackerParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            Round = current.Round,
            PhaseInitiative = phaseInitiative,
            SetId = loadout.SetId,
            WeaponId = loadout.WeaponId,
            WeaponName = loadout.WeaponName,
            AttackKind = request.ActionKind.Value,
            Facing = request.Facing ?? CombatFacing.Front,
            Status = CombatExchangeStatus.Declared,
            ActionConsumed = false,
            AllowedDefenseActions = allowedDefenseActions,
            RuleNote = "Attacke deklariert; der AT-Wurf steht noch aus."
        };

        return (current with
        {
            ActiveExchange = exchange
        }, $"Attacke von {attacker.Name} auf {target.Name} deklariert");
    }

    private async Task<AttackLoadout> ResolveAttackLoadoutAsync(
        CombatSessionParticipantDto attacker,
        CombatSessionMutationRequestDto request,
        CancellationToken cancellationToken)
    {
        if (attacker.Kind == CombatParticipantKind.Opponent)
        {
            if (attacker.OpponentProfile?.CatalogProfile is { } catalogProfile)
            {
                if (!catalogProfile.CombatReady)
                {
                    throw Validation("Der Kataloggegner ist für diesen Angriff noch nicht kampfbereit.");
                }

                if (request.ActionKind is not (CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack))
                {
                    throw Validation("Nur Nah- oder Fernkampfangriffe können einen Gegnerangriff eröffnen.");
                }

                var attackId = string.IsNullOrWhiteSpace(request.WeaponId)
                    ? catalogProfile.AttackId
                    : request.WeaponId;
                if (!string.IsNullOrWhiteSpace(catalogProfile.AttackId) &&
                    !string.Equals(attackId, catalogProfile.AttackId, StringComparison.Ordinal))
                {
                    throw Validation("Der angeforderte Katalogangriff passt nicht zur ausgewÃ¤hlten Profilvariante.");
                }

                var attack = catalogProfile.Attacks.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, attackId, StringComparison.Ordinal));
                if (attack is null)
                {
                    throw Validation("Für den Kataloggegner muss ein konkreter Angriff ausgewählt sein.");
                }

                var isRangedAttack = attack.Category.Contains("ranged", StringComparison.OrdinalIgnoreCase);
                if ((request.ActionKind == CombatActionKind.RangedAttack && !isRangedAttack) ||
                    (request.ActionKind == CombatActionKind.MeleeAttack && isRangedAttack))
                {
                    throw Validation("Angriffsart und Katalogangriff passen nicht zusammen.");
                }

                if ((request.ActionKind == CombatActionKind.MeleeAttack && !catalogProfile.Attack.HasValue) ||
                    (request.ActionKind == CombatActionKind.RangedAttack && !catalogProfile.RangedValue.HasValue))
                {
                    throw Validation("FÃ¼r die gewÃ¤hlte Katalogangriffsart fehlt ein Zielwert.");
                }

                return new AttackLoadout(null, attack.Id, attack.Name);
            }

            if (attacker.OpponentProfile?.Attack is null)
            {
                throw Validation("Für diesen Gegner ist kein AT-Wert hinterlegt.");
            }

            return new AttackLoadout(
                request.SetId,
                request.WeaponId,
                string.IsNullOrWhiteSpace(request.WeaponName) ? null : request.WeaponName.Trim());
        }

        var profile = await ReadCombatProfileAsync(attacker, cancellationToken)
                      ?? throw Validation("Für den Angreifer ist kein importiertes Kampfprofil verfügbar.");
        var set = ResolveParticipantSet(profile, attacker, request.SetId);
        if (set is null)
        {
            throw Validation("Das ausgewählte Kampfset ist im importierten Profil nicht vorhanden.");
        }

        if (string.IsNullOrWhiteSpace(request.WeaponId))
        {
            throw Validation("Für die Attacke muss eine Waffe ausgewählt sein.");
        }

        var weapon = set.Weapons.FirstOrDefault(item =>
            string.Equals(item.Id, request.WeaponId, StringComparison.Ordinal));
        if (weapon is null)
        {
            throw Validation("Die ausgewählte Waffe gehört nicht zum Kampfset.");
        }

        if (weapon.IsAvailable == false)
        {
            throw Validation("Die ausgewählte Waffe ist im Kampfset nicht bereit.");
        }

        var validCategory = request.ActionKind == CombatActionKind.MeleeAttack
            ? weapon.Category == CombatWeaponCategory.Melee && weapon.Attack.HasValue
            : weapon.Category == CombatWeaponCategory.Ranged && weapon.RangedValue.HasValue;
        if (!validCategory)
        {
            throw Validation("Waffe und Angriffsart passen nicht zusammen oder der AT-/FK-Wert fehlt.");
        }

        return new AttackLoadout(set.Id, weapon.Id, weapon.Name);
    }

    private async Task<AutomaticAttackInfo?> ResolveAutomaticAttackInfoAsync(
        CombatSessionParticipantDto attacker,
        CombatAttackExchangeDto exchange,
        CancellationToken cancellationToken)
    {
        if (attacker.Kind == CombatParticipantKind.Opponent)
        {
            var profile = attacker.OpponentProfile?.CatalogProfile;
            var attack = profile?.Attacks.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, exchange.WeaponId, StringComparison.Ordinal));
            return attack?.Damage is { } damage
                ? new AutomaticAttackInfo(damage.Notation)
                : null;
        }

        var combatProfile = await ReadCombatProfileAsync(attacker, cancellationToken);
        var set = combatProfile is null
            ? null
            : ResolveParticipantSet(combatProfile, attacker, exchange.SetId);
        var weapon = set?.Weapons.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, exchange.WeaponId, StringComparison.Ordinal));
        var damageNotation = weapon?.CalculatedDamage ?? weapon?.BaseDamage;
        return string.IsNullOrWhiteSpace(damageNotation)
            ? null
            : new AutomaticAttackInfo(damageNotation);
    }

    private async Task<CombatActionKind[]> ResolveAllowedDefenseActionsAsync(
        CombatSessionParticipantDto target,
        CancellationToken cancellationToken)
    {
        if (!IsCombatEligible(target))
        {
            return [];
        }

        var targetBudget = target.ActionBudget ?? new CombatActionBudgetDto();
        if (!targetBudget.HasReaction)
        {
            return [];
        }

        if (target.Kind == CombatParticipantKind.Opponent)
        {
            var profile = target.OpponentProfile;
            return new[]
                {
                    profile?.Parry.HasValue == true ? CombatActionKind.WeaponParry : (CombatActionKind?)null,
                    profile?.Dodge.HasValue == true ? CombatActionKind.Dodge : (CombatActionKind?)null
                }
                .Where(action => action.HasValue)
                .Select(action => action!.Value)
                .ToArray();
        }

        var combatProfile = await ReadCombatProfileAsync(target, cancellationToken);
        var set = combatProfile is null ? null : ResolveParticipantSet(combatProfile, target, null);
        if (set is null)
        {
            return [];
        }

        var actions = new List<CombatActionKind>();
        if (set.Dodge.HasValue)
        {
            actions.Add(CombatActionKind.Dodge);
        }

        if (set.Weapons.Any(weapon => weapon.Category == CombatWeaponCategory.Melee && weapon.Parry.HasValue))
        {
            actions.Add(CombatActionKind.WeaponParry);
        }

        if (set.Weapons.Any(weapon => weapon.Category == CombatWeaponCategory.Shield && weapon.Parry.HasValue))
        {
            actions.Add(CombatActionKind.ShieldParry);
        }

        return actions.ToArray();
    }

    private static CombatSetVariantDto? ResolveParticipantSet(
        CombatProfileDto profile,
        CombatSessionParticipantDto participant,
        string? requestedSetId)
    {
        if (!string.IsNullOrWhiteSpace(requestedSetId))
        {
            return profile.Sets.FirstOrDefault(set =>
                string.Equals(set.Id, requestedSetId, StringComparison.Ordinal));
        }

        return profile.Sets.FirstOrDefault(set => set.Id == participant.InitiativeSetId)
               ?? profile.Sets.FirstOrDefault(set => set.IsInUse)
               ?? profile.Sets.FirstOrDefault(set => set.IsDefault);
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
        if (!IsCombatEligible(participant))
        {
            throw Validation("Dieser Teilnehmer ist nicht mehr kampffaehig.");
        }
        var budget = participant.ActionBudget ?? new CombatActionBudgetDto();
        if (!action.IsAdditional && !budget.HasNormalAction)
        {
            throw Validation($"Für {participant.Name} ist keine normale Aktion mehr verfügbar.");
        }
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
        var completedParticipant = action.IsAdditional
            ? participant
            : participant with { ActionBudget = budget.ConsumeNormalAction(action.ActionCost) };
        if (action.Label.Equals("Orientieren", StringComparison.OrdinalIgnoreCase) &&
            action.OrientationUninterrupted)
        {
            completedParticipant = ApplyOrientation(completedParticipant);
        }
        participants = ReplaceParticipant(participants, completedParticipant);

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
        if (!IsCombatEligible(participant))
        {
            throw Validation("Dieser Teilnehmer ist nicht mehr kampffaehig.");
        }
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
        if (!IsCombatEligible(participant))
        {
            throw Validation("Dieser Teilnehmer ist nicht mehr kampffaehig.");
        }
        if (!participant.ReactionAvailable)
        {
            throw Validation($"Für {participant.Name} ist keine Reaktion mehr verfügbar.");
        }

        var budget = participant.ActionBudget ?? new CombatActionBudgetDto();
        if (!budget.HasReaction)
        {
            throw Validation($"Für {participant.Name} ist keine Reaktion mehr verfügbar.");
        }

        return (current with
        {
            Participants = ReplaceParticipant(current.Participants, participant with
            {
                ActionBudget = budget.ConsumeReaction(),
                ReactionAvailable = false
            })
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

        var participant = current.Participants.FirstOrDefault(item => item.Id == action.ParticipantId);
        if (participant is not null && !IsCombatEligible(participant))
        {
            throw Validation("Dieser Teilnehmer ist nicht mehr kampffaehig.");
        }
        var budget = participant?.ActionBudget ?? new CombatActionBudgetDto();
        if (state == CombatActionEntryState.Held && budget.HeldActionId is not null &&
            !string.Equals(budget.HeldActionId, action.Id, StringComparison.Ordinal))
        {
            throw Validation($"Für {participant?.Name ?? "diesen Teilnehmer"} ist bereits eine Handlung gehalten.");
        }

        if (state == CombatActionEntryState.Open &&
            !string.Equals(budget.HeldActionId, action.Id, StringComparison.Ordinal))
        {
            throw Validation("Die gehaltene Handlung ist nicht mehr als eigene Reserve vorhanden.");
        }

        var nextBudget = state == CombatActionEntryState.Held
            ? budget with { HeldActionId = action.Id, HeldActionRound = current.Round }
            : budget with { HeldActionId = null, HeldActionRound = null };
        var nextParticipants = participant is null
            ? current.Participants
            : ReplaceParticipant(current.Participants, participant with { ActionBudget = nextBudget });

        return (current with
        {
            Actions = ReplaceAction(current.Actions, action with { State = state }),
            Participants = nextParticipants
        }, $"{action.Label}: {description}");
    }

    private static (CombatSessionSnapshotDto Snapshot, string Description) AddAction(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request)
    {
        var participant = FindParticipant(current, request.ParticipantId, request.HeroId)
                          ?? throw Validation("Der Teilnehmer für die Handlung wurde nicht gefunden.");
        if (!IsCombatEligible(participant))
        {
            throw Validation("Dieser Teilnehmer ist nicht mehr kampffaehig.");
        }
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
            OpponentProfile = request.OpponentProfile,
            InitiativeBase = request.InitiativeBase,
            CurrentInitiative = initiative,
            InitiativeCorrection = request.Initiative.HasValue && request.InitiativeBase.HasValue
                ? request.Initiative.Value - request.InitiativeBase.Value
                : 0,
            ActionBudget = new CombatActionBudgetDto(),
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

    private static (CombatSessionSnapshotDto Snapshot, string Description) AddCatalogOpponent(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request,
        CombatEnemyProfileAdapter enemyAdapter)
    {
        var selection = request.EnemySelection
                        ?? throw Validation("Für einen Kataloggegner muss ein Profil ausgewählt sein.");
        var adapted = enemyAdapter.Resolve(selection);
        if (!adapted.IsValid || adapted.Profile is null)
        {
            throw Validation(adapted.Message ?? "Das Gegnerprofil konnte nicht aufgelöst werden.");
        }

        var profile = adapted.Profile;
        var name = string.IsNullOrWhiteSpace(request.Name) ? profile.Name : request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw Validation("Gegner brauchen mindestens einen Namen.");
        }

        var initiative = request.Initiative;
        var opponentProfile = new CombatOpponentProfileDto(
            profile.Attack,
            profile.Parry,
            profile.Dodge,
            profile.Armor.TotalRs,
            profile.LeP,
            profile.WoundThreshold)
        {
            CatalogProfile = profile,
            Armor = profile.Armor
        };
        var participant = new CombatSessionParticipantDto
        {
            Id = $"opponent:{Guid.NewGuid():N}",
            Name = name,
            Kind = CombatParticipantKind.Opponent,
            Affiliation = string.IsNullOrWhiteSpace(request.Affiliation) ? "Gegner" : request.Affiliation.Trim(),
            OpponentProfile = opponentProfile,
            InitiativeBase = profile.InitiativeBase,
            InitiativeDiceCount = Math.Clamp(profile.InitiativeDiceCount, 1, 2),
            CurrentInitiative = initiative,
            InitiativeCorrection = initiative.HasValue && profile.InitiativeBase.HasValue
                ? initiative.Value - profile.InitiativeBase.Value
                : request.InitiativeCorrection,
            RuntimeState = new CombatRuntimeStateDto
            {
                IsStarted = true,
                CurrentLeP = profile.LeP,
                CurrentAuP = profile.AuP,
                Wounds = CreateEmptyWounds()
            },
            ActionBudget = new CombatActionBudgetDto(),
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

    private static (CombatSessionSnapshotDto Snapshot, string Description) RemoveOpponent(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ParticipantId))
        {
            throw Validation("Zum Entfernen muss ein Gegner ausgewählt sein.");
        }

        var participant = current.Participants.FirstOrDefault(item =>
            string.Equals(item.Id, request.ParticipantId.Trim(), StringComparison.Ordinal));
        if (participant is null)
        {
            throw Validation("Der ausgewählte Gegner gehört nicht zu diesem Kampf.");
        }

        if (participant.Kind != CombatParticipantKind.Opponent)
        {
            throw Validation("Nur Gegner können aus der Gegnerverwaltung entfernt werden.");
        }

        if (current.ActiveExchange is { } exchange &&
            CombatAttackExchangeRules.IsOpen(exchange) &&
            (string.Equals(exchange.AttackerParticipantId, participant.Id, StringComparison.Ordinal) ||
             string.Equals(exchange.TargetParticipantId, participant.Id, StringComparison.Ordinal)))
        {
            throw Validation("Der Gegner kann während eines offenen Angriffsaustauschs nicht entfernt werden.");
        }

        return (current with
        {
            Participants = current.Participants
                .Where(item => !string.Equals(item.Id, participant.Id, StringComparison.Ordinal))
                .ToArray(),
            Actions = current.Actions
                .Where(action => !string.Equals(action.ParticipantId, participant.Id, StringComparison.Ordinal))
                .ToArray()
        }, $"Gegner {participant.Name} entfernt");
    }

    private static (CombatSessionSnapshotDto Snapshot, string Description) NewRound(
        CombatSessionSnapshotDto current,
        CombatSessionMutationRequestDto _)
    {
        if (CombatAttackExchangeRules.IsOpen(current.ActiveExchange))
        {
            throw Validation("Der offene Angriffsaustausch muss zuerst abgeschlossen werden.");
        }

        var eligibleParticipantIds = current.Participants
            .Where(IsCombatEligible)
            .Select(participant => participant.Id)
            .ToHashSet(StringComparer.Ordinal);
        var openActions = current.Actions.Any(action => action.Round == current.Round &&
                                                         eligibleParticipantIds.Contains(action.ParticipantId) &&
                                                         !action.IsReaction &&
                                                         action.State == CombatActionEntryState.Open);
        if (openActions)
        {
            throw Validation("Die offene Runde ist noch nicht abgeschlossen oder gehalten.");
        }

        var round = current.Round + 1;
        var currentParticipantsById = current.Participants
            .ToDictionary(participant => participant.Id, StringComparer.Ordinal);
        var actions = current.Actions
            .Select(action => action.State == CombatActionEntryState.Held &&
                              currentParticipantsById.TryGetValue(action.ParticipantId, out var participant) &&
                              IsCombatEligible(participant)
                ? action with
                {
                    Round = round,
                    PhaseInitiative = participant.CurrentInitiative ?? action.PhaseInitiative
                }
                : action)
            .ToList();
        var heldActions = actions
            .Where(action => action.Round == round && action.State == CombatActionEntryState.Held)
            .GroupBy(action => action.ParticipantId)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var participants = current.Participants
            .Select(participant =>
            {
                var eligible = IsCombatEligible(participant);
                var budget = eligible
                    ? (participant.ActionBudget ?? new CombatActionBudgetDto()).ResetForRound()
                    : CreateIncapacitatedBudget();
                if (heldActions.TryGetValue(participant.Id, out var heldAction))
                {
                    budget = budget with { HeldActionId = heldAction.Id, HeldActionRound = round };
                }

                return participant with
                {
                    ActionBudget = budget,
                    ActionAvailable = eligible && participant.CurrentInitiative.HasValue,
                    ReactionAvailable = eligible,
                    IsOriented = false
                };
            })
            .ToArray();
        foreach (var participant in participants.Where(item => IsCombatEligible(item) &&
                                                               item.CurrentInitiative.HasValue &&
                                                               !heldActions.ContainsKey(item.Id)))
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

    private static PersistedState CreateExchangePersistedState(
        CombatSessionSnapshotDto next,
        CombatSessionSnapshotDto current,
        PersistedState persisted,
        string exchangeId,
        string userId,
        Guid[] appliedRequestIds)
    {
        var keepExchangeUndo = string.Equals(persisted.UndoExchangeId, exchangeId, StringComparison.Ordinal) &&
                               persisted.Undo is not null;
        return new PersistedState(
            next,
            keepExchangeUndo ? persisted.Undo : current,
            keepExchangeUndo ? persisted.UndoOwnerUserId : userId,
            appliedRequestIds,
            exchangeId,
            persisted.CachedRolls);
    }

    private static string? ResolveUndoExchangeId(
        CombatSessionSnapshotDto current,
        CombatSessionSnapshotDto next,
        PersistedState persisted)
    {
        var nextExchange = next.ActiveExchange;
        if (nextExchange is null || !IsUndoableExchangeStatus(nextExchange.Status))
        {
            return null;
        }

        return current.ActiveExchange is { } currentExchange &&
               string.Equals(currentExchange.ExchangeId, nextExchange.ExchangeId, StringComparison.Ordinal) &&
               string.Equals(persisted.UndoExchangeId, currentExchange.ExchangeId, StringComparison.Ordinal)
            ? currentExchange.ExchangeId
            : nextExchange.ExchangeId;
    }

    private static bool IsUndoableExchangeStatus(CombatExchangeStatus status) => status is
        CombatExchangeStatus.Declared or
        CombatExchangeStatus.AttackOpen or
        CombatExchangeStatus.DefenseOpen or
        CombatExchangeStatus.Hit or
        CombatExchangeStatus.Avoided or
        CombatExchangeStatus.DamageOpen;

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
        if (request.Kind is CombatSessionMutationKind.NewRound or
            CombatSessionMutationKind.AddOpponent or
            CombatSessionMutationKind.RemoveOpponent)
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
                    InitiativeRuntimeNotes = participant.InitiativeRuntimeNotes ?? [],
                    ActionBudget = NormalizeBudget(
                        participant.ActionBudget,
                        participant.ActionAvailable,
                        participant.ReactionAvailable)
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
                             action.State == CombatActionEntryState.Open &&
                             participantsById.TryGetValue(action.ParticipantId, out var participant) &&
                             IsCombatEligible(participant))
            .OrderByDescending(action => EffectiveInitiative(action, participantsById))
            .ThenByDescending(action => BaseInitiative(action, participantsById))
            .ThenBy(action => action.Id, StringComparer.Ordinal)
            .ToArray();
        var exchangeParticipantId = GetPendingExchangeParticipantId(snapshot.ActiveExchange);
        var current = string.IsNullOrWhiteSpace(exchangeParticipantId)
            ? openActions.FirstOrDefault()
            : openActions.FirstOrDefault(action =>
                string.Equals(action.ParticipantId, exchangeParticipantId, StringComparison.Ordinal));
        var currentValue = current is null ? (int?)null : EffectiveInitiative(current, participantsById);
        var currentBase = current is null ? (int?)null : BaseInitiative(current, participantsById);
        var currentIds = current is null
            ? []
            : (string.IsNullOrWhiteSpace(exchangeParticipantId) ? openActions : openActions
                    .Where(action => string.Equals(action.ParticipantId, exchangeParticipantId, StringComparison.Ordinal)))
                .Where(action => EffectiveInitiative(action, participantsById) == currentValue &&
                                 BaseInitiative(action, participantsById) == currentBase)
                .Select(action => action.Id)
                .ToArray();

        var participants = snapshot.Participants
            .Select(participant => participant with
            {
                ActionAvailable = IsCombatEligible(participant) &&
                                  (participant.ActionBudget ?? new CombatActionBudgetDto()).HasNormalAction &&
                                  openActions.Any(action => action.ParticipantId == participant.Id),
                ReactionAvailable = IsCombatEligible(participant) &&
                                    (participant.ActionBudget ?? new CombatActionBudgetDto()).HasReaction
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

    private static string? GetPendingExchangeParticipantId(CombatAttackExchangeDto? exchange) =>
        !CombatAttackExchangeRules.IsOpen(exchange) || exchange is null
            ? null
            : exchange.Status == CombatExchangeStatus.DefenseOpen
                ? exchange.TargetParticipantId
                : exchange.AttackerParticipantId;

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

    private static CombatRollEvaluationDto CreateAttackEvaluation(CombatRollSnapshotDto snapshot)
    {
        var mainRoll = snapshot.LabeledRolls
            .FirstOrDefault(roll => string.Equals(roll.Role, "Hauptwurf", StringComparison.Ordinal))?.Value ?? 0;
        var controlRoll = snapshot.LabeledRolls
            .FirstOrDefault(roll => string.Equals(roll.Role, "Kontrollwurf", StringComparison.Ordinal))?.Value;
        var isSuccessful = snapshot.Outcome is CombatOutcome.Success or CombatOutcome.Lucky or CombatOutcome.Critical;
        var isFumble = snapshot.Outcome == CombatOutcome.Fumble;

        return new CombatRollEvaluationDto(
            true,
            null,
            snapshot.Action,
            snapshot.BaseValue,
            snapshot.EffectiveTarget,
            snapshot.ControlTarget,
            mainRoll,
            controlRoll,
            snapshot.Outcome,
            isSuccessful,
            snapshot.Outcome == CombatOutcome.Critical,
            isFumble,
            IsAttackAction(snapshot.Action) && isSuccessful,
            snapshot.StatusLabel)
        {
            Modifiers = snapshot.Modifiers ?? [],
            RuleNotes = snapshot.RuleNotes ?? [],
            ValuesSource = snapshot.ValuesSource ?? "Unbekannt"
        };
    }

    private static bool IsAttackAction(CombatActionKind action) => action is
        CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack;

    private static bool IsDefenseAction(CombatActionKind action) =>
        CombatActionBudgetRules.RequiresReaction(action);

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

    private static CombatActionBudgetDto NormalizeBudget(
        CombatActionBudgetDto? budget,
        bool legacyActionAvailable,
        bool legacyReactionAvailable)
    {
        budget ??= new CombatActionBudgetDto
        {
            NormalActionsRemaining = legacyActionAvailable ? 1 : 0,
            ReactionsRemaining = legacyReactionAvailable ? 1 : 0
        };
        return budget with
        {
            NormalActionsRemaining = Math.Clamp(budget.NormalActionsRemaining, 0, 3),
            ReactionsRemaining = Math.Clamp(budget.ReactionsRemaining, 0, 1),
            HeldActionRound = budget.HeldActionId is null ? null : budget.HeldActionRound
        };
    }

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

    private static bool MatchesRequestedParticipant(
        CombatSessionParticipantDto participant,
        CombatRollRequestDto request) =>
        (string.IsNullOrWhiteSpace(request.ParticipantId) ||
         string.Equals(participant.Id, request.ParticipantId.Trim(), StringComparison.Ordinal)) &&
        (participant.Kind == CombatParticipantKind.Hero
            ? participant.HeroId == request.HeroId
            : !request.HeroId.HasValue);

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

    private static Dictionary<CombatWoundZone, int?> CreateEmptyWounds() =>
        Enum.GetValues<CombatWoundZone>()
            .ToDictionary(zone => zone, _ => (int?)0);

    private static CombatActionBudgetDto CreateIncapacitatedBudget() => new()
    {
        NormalActionsRemaining = 0,
        ReactionsRemaining = 0,
        FreeActionAvailable = false
    };

    private static bool IsCombatEligible(CombatSessionParticipantDto participant) =>
        (participant.RuntimeState?.CurrentLeP ?? participant.OpponentProfile?.LeP) is not <= 0;

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

    private static Guid[] AppendAppliedRequestId(IEnumerable<Guid> requestIds, Guid requestId) =>
        requestIds.Append(requestId).Distinct().TakeLast(MaxAppliedRequestIds).ToArray();

    private static string HeroParticipantId(Guid heroId) => $"hero:{heroId:N}";

    private static void EnsureNoOpenAttackExchange(
        CombatSessionSnapshotDto current,
        CombatSessionMutationKind mutationKind)
    {
        if (CombatAttackExchangeRules.IsOpen(current.ActiveExchange) &&
            CombatAttackExchangeRules.BlocksTurnProgress(mutationKind))
        {
            throw Validation("Der offene Angriffsaustausch muss zuerst abgeschlossen werden.");
        }
    }

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

    private static CombatModifierDto[] CreateInitiativeModifiers(int runtimeModifier, int correction)
    {
        var modifiers = new List<CombatModifierDto>();
        if (runtimeModifier != 0)
        {
            modifiers.Add(new CombatModifierDto("Automatisch", runtimeModifier, "Kampf"));
        }

        if (correction != 0)
        {
            modifiers.Add(new CombatModifierDto("Situativ", correction, "Kampfseite"));
        }

        return modifiers.ToArray();
    }

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
        Guid[] AppliedRequestIds,
        string? UndoExchangeId = null,
        CombatRollResultDto[]? CachedRolls = null);

    private sealed record InitiativeProfileInfo(
        int? BaseValue,
        int DiceCount,
        bool HasAttention,
        int? KriegskunstValue,
        CombatProfileDto? Profile,
        string? SetId,
        int RuntimeModifier,
        string[] RuntimeNotes);

    private sealed record AttackLoadout(
        string? SetId,
        string? WeaponId,
        string? WeaponName);

    private sealed record AutomaticAttackInfo(string DamageNotation);

    private sealed record AuthorizationResult(bool Allowed, string Message);

    private static string FormatWoundZone(CombatWoundZone zone) => zone switch
    {
        CombatWoundZone.Head => "Kopf",
        CombatWoundZone.Torso => "Brust/Rücken",
        CombatWoundZone.Abdomen => "Bauch",
        CombatWoundZone.LeftArm => "linker Arm",
        CombatWoundZone.RightArm => "rechter Arm",
        CombatWoundZone.LeftLeg => "linkes Bein",
        CombatWoundZone.RightLeg => "rechtes Bein",
        _ => zone.ToString()
    };
}
