using System.Globalization;
using System.Text.RegularExpressions;

using DsaWuerfelApp.Services.Application.Import;
using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed partial class RollCombatHandler(
    HeroContextReader heroContextReader,
    HeroCombatProfileReader heroCombatProfileReader,
    DiceService diceService,
    CombatSessionStateService combatSessionStateService)
{
    private const int RuntimeValueLimit = 1_000_000;

    public async Task<CombatRollResultDto> HandleAsync(
        CombatRollRequestDto request,
        string userId,
        string playerName = "Unbekannt",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var resolvedPlayerName = string.IsNullOrWhiteSpace(playerName) ? "Unbekannt" : playerName.Trim();

        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var cached = await combatSessionStateService.GetCachedRollAsync(
                request.SessionId,
                request.RequestId,
                userId,
                cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        request = await PrepareSessionAttackAsync(request, userId, cancellationToken);
        var recovered = await TryRecoverStoredSessionRollAsync(
            request,
            userId,
            resolvedPlayerName,
            cancellationToken);
        if (recovered is not null)
        {
            await combatSessionStateService.EnsureParticipantRollAccessAsync(
                request,
                userId,
                cancellationToken);
            return await BindSessionResultAsync(request, recovered, userId, cancellationToken);
        }

        await combatSessionStateService.EnsureRollAvailabilityAsync(request, userId, cancellationToken);

        CombatSessionSnapshotDto? sessionSnapshot = null;
        CombatSessionParticipantDto? sessionParticipant = null;
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            sessionSnapshot = await combatSessionStateService.GetAsync(
                request.SessionId,
                userId,
                cancellationToken);

            sessionParticipant = !string.IsNullOrWhiteSpace(request.ParticipantId)
                ? sessionSnapshot.Participants.FirstOrDefault(participant =>
                    string.Equals(participant.Id, request.ParticipantId.Trim(), StringComparison.Ordinal))
                : request.HeroId.HasValue
                    ? sessionSnapshot.Participants.FirstOrDefault(participant =>
                        participant.Kind == CombatParticipantKind.Hero && participant.HeroId == request.HeroId)
                    : null;

            if (!string.IsNullOrWhiteSpace(request.ParticipantId) && sessionParticipant is null)
            {
                throw Validation("Der Kampfwurf gehÃ¶rt nicht zu einem Teilnehmer dieser Session.");
            }

            if (sessionParticipant is not null)
            {
                if (sessionParticipant.Kind == CombatParticipantKind.Hero &&
                    request.HeroId.HasValue && request.HeroId != sessionParticipant.HeroId)
                {
                    throw Validation("Der Kampfwurf gehÃ¶rt nicht zum ausgewÃ¤hlten Helden.");
                }

                if (sessionParticipant.Kind == CombatParticipantKind.Opponent && request.HeroId.HasValue)
                {
                    throw Validation("Ein Gegnerkampf verwendet keine Helden-ID.");
                }

                request = request with
                {
                    ParticipantId = sessionParticipant.Id,
                    HeroId = sessionParticipant.Kind == CombatParticipantKind.Hero
                        ? sessionParticipant.HeroId
                        : null,
                    // Session rolls always use the authoritative runtime snapshot.
                    RuntimeState = sessionParticipant.RuntimeState
                };
                await combatSessionStateService.EnsureParticipantRollAccessAsync(
                    request,
                    userId,
                    cancellationToken);
            }

            if (sessionParticipant?.Kind == CombatParticipantKind.Opponent)
            {
                var opponentResult = RollOpponent(
                    request,
                    sessionParticipant,
                    sessionSnapshot,
                    resolvedPlayerName,
                    userId);
                return await BindSessionResultAsync(
                    request,
                    opponentResult,
                    userId,
                    cancellationToken);
            }
        }

        var hero = await heroContextReader.LoadContextAsync(
            request.HeroId,
            request.SessionId,
            userId,
            cancellationToken);
        if (hero is null)
        {
            throw Validation("Für den Kampfwurf ist kein aktiver Held ausgewählt.");
        }

        var ownerUserId = string.IsNullOrWhiteSpace(hero.OwnerUserId) ? userId : hero.OwnerUserId;
        var profile = await heroCombatProfileReader.ReadAsync(hero.Id, ownerUserId, cancellationToken);
        if (profile is null)
        {
            throw new RequestRejectedException(
                RequestRejectionReason.NotFound,
                "Für den ausgewählten Helden ist kein importiertes Kampfprofil verfügbar.");
        }

        var set = ResolveSet(profile, request.SetId);

        var result = request.Action switch
        {
            CombatActionKind.MeleeAttack or
            CombatActionKind.WeaponParry or
            CombatActionKind.ShieldParry or
            CombatActionKind.Dodge or
            CombatActionKind.RangedAttack => RollCheck(request, hero, profile, set, resolvedPlayerName, userId),
            CombatActionKind.Damage => RollDamage(request, hero, set, resolvedPlayerName, userId),
            CombatActionKind.HitZone => RollHitZone(request, hero, set, resolvedPlayerName, userId),
            CombatActionKind.InitiativeHelper or
            CombatActionKind.WoundHelper or
            CombatActionKind.FumbleHelper => RollHelper(request, hero, resolvedPlayerName, userId),
            _ => throw Validation("Diese Kampfwurfart wird nicht unterstützt.")
        };

        return await BindSessionResultAsync(request, result, userId, cancellationToken);
    }

    private async Task<CombatRollResultDto?> TryRecoverStoredSessionRollAsync(
        CombatRollRequestDto request,
        string userId,
        string playerName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) ||
            request.Action is not (CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack or
                CombatActionKind.WeaponParry or CombatActionKind.ShieldParry or CombatActionKind.Dodge) ||
            string.IsNullOrWhiteSpace(request.ExchangeId))
        {
            return null;
        }

        var sessionSnapshot = await combatSessionStateService.GetAsync(
            request.SessionId,
            userId,
            cancellationToken);
        var exchange = sessionSnapshot.ActiveExchange;
        if (exchange is null ||
            !string.Equals(exchange.ExchangeId, request.ExchangeId, StringComparison.Ordinal))
        {
            return null;
        }

        var evaluation = request.Action is CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack
            ? exchange.AttackRollRequestId == request.RequestId ? exchange.AttackResult : null
            : exchange.DefenseRollRequestId == request.RequestId ? exchange.DefenseResult : null;
        if (evaluation is null)
        {
            return null;
        }

        var participantId = request.Action is CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack
            ? exchange.AttackerParticipantId
            : exchange.TargetParticipantId;
        var participant = sessionSnapshot.Participants.FirstOrDefault(current =>
            string.Equals(current.Id, participantId, StringComparison.Ordinal));
        if (participant is null)
        {
            return null;
        }

        var entryId = exchange.HistoryEntryIds.LastOrDefault();
        if (entryId == Guid.Empty)
        {
            entryId = Guid.NewGuid();
        }

        var recoveredRequest = request with
        {
            SessionId = sessionSnapshot.SessionId,
            ParticipantId = participant.Id,
            TargetParticipantId = request.Action is CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack
                ? exchange.TargetParticipantId
                : exchange.AttackerParticipantId,
            ActionId = exchange.ActionId,
            ExchangeId = exchange.ExchangeId,
            HeroId = participant.HeroId,
            WeaponId = request.Action is CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack
                ? exchange.WeaponId
                : request.WeaponId,
            WeaponName = request.Action is CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack
                ? exchange.WeaponName
                : request.WeaponName
        };
        var rolls = new List<DiceRollDto> { new(20, evaluation.MainRoll) };
        if (evaluation.ControlRoll is { } controlRoll)
        {
            rolls.Add(new DiceRollDto(20, controlRoll));
        }

        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = entryId,
            RequestId = recoveredRequest.RequestId,
            SessionId = recoveredRequest.SessionId,
            ParticipantId = participant.Id,
            ParticipantName = participant.Name,
            ExchangeId = exchange.ExchangeId,
            HeroId = participant.HeroId,
            Action = evaluation.Action,
            ActionLabel = GetActionLabel(evaluation.Action),
            WeaponName = recoveredRequest.WeaponName,
            ValuesSource = evaluation.ValuesSource,
            BaseValue = evaluation.BaseValue,
            UnmodifiedBaseValue = evaluation.BaseValue,
            EffectiveTarget = evaluation.EffectiveTarget,
            ControlTarget = evaluation.ControlTarget,
            Modifiers = evaluation.Modifiers ?? [],
            RuleOptions = recoveredRequest.Options ?? new CombatRuleOptionsDto(),
            Outcome = evaluation.Outcome,
            StatusLabel = evaluation.StatusLabel,
            LabeledRolls = CreateStoredLabeledRolls(evaluation),
            FollowUps = evaluation.FollowUps ?? [],
            RuleNotes = evaluation.RuleNotes ?? []
        };
        var result = CreateResult(
            recoveredRequest,
            participant.HeroId,
            participant.Name,
            participant.Id,
            userId,
            playerName,
            snapshot,
            rolls,
            MapHistoryOutcome(evaluation.Outcome),
            BuildChecks(evaluation));
        return AttachExchangeContext(result, sessionSnapshot);
    }

    private static CombatLabeledRollDto[] CreateStoredLabeledRolls(CombatRollEvaluationDto evaluation)
    {
        var rolls = new List<CombatLabeledRollDto>
        {
            new("Hauptwurf", 20, evaluation.MainRoll)
        };
        if (evaluation.ControlRoll is { } controlRoll)
        {
            rolls.Add(new CombatLabeledRollDto("Kontrollwurf", 20, controlRoll));
        }

        return rolls.ToArray();
    }

    private async Task<CombatRollRequestDto> PrepareSessionAttackAsync(
        CombatRollRequestDto request,
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) ||
            request.Action is not (CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack) ||
            !string.IsNullOrWhiteSpace(request.ExchangeId))
        {
            return request;
        }

        var snapshot = await combatSessionStateService.GetAsync(
            request.SessionId,
            userId,
            cancellationToken);
        if (snapshot.ActiveExchange is { } existingExchange)
        {
            if (existingExchange.RequestId != request.RequestId &&
                existingExchange.AttackRollRequestId != request.RequestId)
            {
                throw Validation("Der offene Angriffsaustausch muss zuerst abgeschlossen werden.");
            }

            EnsureSameAttackRequest(request, existingExchange);
            return BindPreparedExchange(request, snapshot, existingExchange);
        }

        var attacker = ResolveRequestedParticipant(snapshot, request)
                       ?? throw Validation("Der Angreifer wurde nicht gefunden.");
        var action = ResolveAttackAction(snapshot, attacker.Id, request)
                     ?? throw Validation("Für den Angreifer ist keine offene normale Handlung vorhanden.");
        var expectedRevision = request.ExpectedRevision ?? snapshot.Revision;
        var declaration = await combatSessionStateService.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = request.RequestId,
            SessionId = snapshot.SessionId,
            ExpectedRevision = expectedRevision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = request.TargetParticipantId,
            ActionId = action.Id,
            ActionKind = request.Action,
            ExchangeId = Guid.NewGuid().ToString("N"),
            HeroId = attacker.HeroId,
            SetId = request.SetId,
            WeaponId = request.WeaponId,
            WeaponName = request.WeaponName,
            PhaseInitiative = action.PhaseInitiative,
            Facing = request.Facing
        }, userId, cancellationToken);

        if (!declaration.Applied && !declaration.AlreadyApplied)
        {
            throw Validation(declaration.Message);
        }

        var exchange = declaration.Snapshot.ActiveExchange;
        if (exchange is null || exchange.RequestId != request.RequestId ||
            !CombatAttackExchangeRules.IsOpen(exchange))
        {
            throw Validation("Der Angriff konnte nicht für den Wurf vorbereitet werden.");
        }

        return BindPreparedExchange(request, declaration.Snapshot, exchange);
    }

    private static CombatRollRequestDto BindPreparedExchange(
        CombatRollRequestDto request,
        CombatSessionSnapshotDto snapshot,
        CombatAttackExchangeDto exchange)
    {
        var attacker = snapshot.Participants.FirstOrDefault(participant =>
            string.Equals(participant.Id, exchange.AttackerParticipantId, StringComparison.Ordinal));
        return request with
        {
            ParticipantId = exchange.AttackerParticipantId,
            TargetParticipantId = exchange.TargetParticipantId,
            ActionId = exchange.ActionId,
            ExpectedRevision = snapshot.Revision,
            HeroId = attacker?.HeroId,
            SetId = exchange.SetId,
            ExchangeId = exchange.ExchangeId,
            WeaponId = exchange.WeaponId,
            WeaponName = exchange.WeaponName
        };
    }

    private static void EnsureSameAttackRequest(
        CombatRollRequestDto request,
        CombatAttackExchangeDto exchange)
    {
        if (exchange.AttackKind != request.Action ||
            !MatchesOptional(request.ParticipantId, exchange.AttackerParticipantId) ||
            !MatchesOptional(request.TargetParticipantId, exchange.TargetParticipantId) ||
            !MatchesOptional(request.ActionId, exchange.ActionId) ||
            !MatchesOptional(request.SetId, exchange.SetId) ||
            !MatchesOptional(request.WeaponId, exchange.WeaponId))
        {
            throw Validation("Der wiederholte AT-Wurf passt nicht zum bereits vorbereiteten Angriff.");
        }
    }

    private static CombatSessionParticipantDto? ResolveRequestedParticipant(
        CombatSessionSnapshotDto snapshot,
        CombatRollRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.ParticipantId))
        {
            return snapshot.Participants.FirstOrDefault(participant =>
                string.Equals(participant.Id, request.ParticipantId.Trim(), StringComparison.Ordinal));
        }

        if (request.HeroId.HasValue)
        {
            return snapshot.Participants.FirstOrDefault(participant =>
                participant.Kind == CombatParticipantKind.Hero && participant.HeroId == request.HeroId);
        }

        if (!string.IsNullOrWhiteSpace(request.ActionId))
        {
            var action = snapshot.Actions.FirstOrDefault(item => item.Id == request.ActionId);
            return action is null
                ? null
                : snapshot.Participants.FirstOrDefault(participant => participant.Id == action.ParticipantId);
        }

        return snapshot.Participants.FirstOrDefault(participant => participant.Kind == CombatParticipantKind.Hero);
    }

    private static CombatSessionActionDto? ResolveAttackAction(
        CombatSessionSnapshotDto snapshot,
        string participantId,
        CombatRollRequestDto request)
    {
        var actions = snapshot.Actions.Where(action =>
                action.ParticipantId == participantId &&
                action.Round == snapshot.Round &&
                !action.IsReaction &&
                action.State is CombatActionEntryState.Open or CombatActionEntryState.Held)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(request.ActionId))
        {
            return actions.FirstOrDefault(action => action.Id == request.ActionId);
        }

        return actions.FirstOrDefault(action => !IsOrientationAction(action));
    }

    private static bool IsOrientationAction(CombatSessionActionDto action) =>
        string.Equals(action.Label, "Orientieren", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesOptional(string? requested, string? expected) =>
        string.IsNullOrWhiteSpace(requested) ||
        string.Equals(requested.Trim(), expected, StringComparison.Ordinal);

    private async Task<CombatRollResultDto> BindSessionResultAsync(
        CombatRollRequestDto request,
        CombatRollResultDto result,
        string userId,
        CancellationToken cancellationToken)
    {
        var processing = await combatSessionStateService.ProcessSessionRollAsync(
            request,
            result,
            userId,
            cancellationToken);
        if (processing.Snapshot is not { } sessionSnapshot)
        {
            return result;
        }

        var boundResult = AttachExchangeContext(
            processing.Result with { CombatSessionSnapshot = sessionSnapshot },
            sessionSnapshot);
        if (processing.AutomaticHit is { } automaticHit)
        {
            boundResult = EnrichAutomaticHitResult(boundResult, automaticHit);
        }
        else if (request.Action == CombatActionKind.Damage)
        {
            boundResult = EnrichWoundConsequenceResult(boundResult, sessionSnapshot);
        }

        await combatSessionStateService.CacheRollResultAsync(
            request,
            boundResult,
            userId,
            cancellationToken);
        return boundResult;
    }

    private static CombatRollResultDto EnrichAutomaticHitResult(
        CombatRollResultDto result,
        CombatAutomaticHitResolutionDto automaticHit)
    {
        var initiativeLossRollCount = automaticHit.WoundApplication.InitiativeLossRolls.Length;
        var initiativeLossStart = automaticHit.Rolls.Length - initiativeLossRollCount;
        var automaticLabels = automaticHit.Rolls
            .Select((roll, index) => new CombatLabeledRollDto(
                automaticHit.ZoneWasRolled && index == 0
                    ? "Trefferzonenwurf"
                    : index >= initiativeLossStart ? "INI-Verlust-Würfel" : "TP-Würfel",
                roll.Sides,
                roll.Value));
        var snapshot = result.Snapshot with
        {
            StatusLabel = "Treffer · Trefferzone und TP automatisch gespeichert",
            LabeledRolls = result.Snapshot.LabeledRolls.Concat(automaticLabels).ToArray(),
            Zone = automaticHit.Zone,
            Damage = automaticHit.Damage,
            WoundApplication = automaticHit.WoundApplication,
            RuleNotes = result.Snapshot.RuleNotes
                .Concat(automaticHit.RuleNotes)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
        var historyContext = result.HistoryEntry.Context;
        var historyEntry = result.HistoryEntry with
        {
            Rolls = result.HistoryEntry.Rolls.Concat(automaticHit.Rolls).ToArray(),
            Context = historyContext is null
                ? null
                : historyContext with
                {
                    Snapshot = (historyContext.Snapshot ?? new RollHistorySnapshotDto()) with
                    {
                        Combat = snapshot
                    }
                }
        };
        return AttachExchangeContext(result with
        {
            Snapshot = snapshot,
            Rolls = result.Rolls.Concat(automaticHit.Rolls).ToArray(),
            HistoryEntry = historyEntry,
            CombatSessionSnapshot = automaticHit.Snapshot
        }, automaticHit.Snapshot);
    }

    private static CombatRollResultDto EnrichWoundConsequenceResult(
        CombatRollResultDto result,
        CombatSessionSnapshotDto sessionSnapshot)
    {
        var application = sessionSnapshot.ActiveExchange?.WoundApplication;
        if (application is null || application.InitiativeLossRolls.Length == 0)
        {
            return result;
        }

        var labels = application.InitiativeLossRolls
            .Select(roll => new CombatLabeledRollDto("INI-Verlust-Würfel", roll.Sides, roll.Value));
        var snapshot = result.Snapshot with
        {
            LabeledRolls = result.Snapshot.LabeledRolls.Concat(labels).ToArray(),
            WoundApplication = application
        };
        var historyEntry = result.HistoryEntry with
        {
            Rolls = result.HistoryEntry.Rolls.Concat(application.InitiativeLossRolls).ToArray(),
            Context = result.HistoryEntry.Context is not { } context
                ? null
                : context with
                {
                    Snapshot = (context.Snapshot ?? new RollHistorySnapshotDto()) with
                    {
                        Combat = snapshot
                    }
                }
        };
        return result with
        {
            Snapshot = snapshot,
            Rolls = result.Rolls.Concat(application.InitiativeLossRolls).ToArray(),
            HistoryEntry = historyEntry
        };
    }

    private static CombatRollResultDto AttachExchangeContext(
        CombatRollResultDto result,
        CombatSessionSnapshotDto sessionSnapshot)
    {
        var exchange = sessionSnapshot.ActiveExchange;
        if (exchange is null || string.IsNullOrWhiteSpace(result.Snapshot.ExchangeId) ||
            !string.Equals(exchange.ExchangeId, result.Snapshot.ExchangeId, StringComparison.Ordinal))
        {
            return result;
        }

        var attacker = sessionSnapshot.Participants.FirstOrDefault(participant =>
            string.Equals(participant.Id, exchange.AttackerParticipantId, StringComparison.Ordinal));
        var target = sessionSnapshot.Participants.FirstOrDefault(participant =>
            string.Equals(participant.Id, exchange.TargetParticipantId, StringComparison.Ordinal));
        var resultFollowUps = result.Snapshot.FollowUps ?? [];
        var exchangeFollowUps = exchange.AttackResult?.FollowUps ?? [];
        var snapshot = result.Snapshot with
        {
            ExchangeAttackerName = attacker?.Name,
            ExchangeTargetName = target?.Name,
            ExchangeAttackResult = exchange.AttackResult,
            ExchangeDefenseAction = exchange.SelectedDefenseAction,
            ExchangeDefenseResult = exchange.DefenseResult,
            Zone = exchange.Zone ?? result.Snapshot.Zone,
            Damage = exchange.Damage ?? result.Snapshot.Damage,
            WoundApplication = exchange.WoundApplication ?? result.Snapshot.WoundApplication,
            FollowUps = resultFollowUps.Length > 0 ? resultFollowUps : exchangeFollowUps,
            RuleNotes = exchange.Status is (CombatExchangeStatus.Hit or CombatExchangeStatus.DamageOpen) &&
                        !string.IsNullOrWhiteSpace(exchange.RuleNote)
                ? result.Snapshot.RuleNotes
                    .Append(exchange.RuleNote!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
                : result.Snapshot.RuleNotes
        };

        if (result.HistoryEntry.Context is not { } context)
        {
            return result with { Snapshot = snapshot };
        }

        var historySnapshot = (context.Snapshot ?? new RollHistorySnapshotDto()) with
        {
            Combat = snapshot
        };
        var historyEntry = result.HistoryEntry with
        {
            Context = context with
            {
                DisplayName = GetHistoryDisplayName(snapshot),
                Snapshot = historySnapshot
            }
        };
        return result with
        {
            Snapshot = snapshot,
            HistoryEntry = historyEntry
        };
    }

    private CombatRollResultDto RollOpponent(
        CombatRollRequestDto request,
        CombatSessionParticipantDto opponent,
        CombatSessionSnapshotDto sessionSnapshot,
        string playerName,
        string userId)
    {
        var profile = opponent.OpponentProfile?.CatalogProfile;
        if (profile is null)
        {
            throw Validation("FÃ¼r diesen Gegner ist kein DSA-4.1-Katalogprofil hinterlegt.");
        }

        if (!profile.CombatReady)
        {
            throw Validation($"{opponent.Name} ist noch nicht kampfbereit: {GetReadinessText(profile)}");
        }

        return request.Action switch
        {
            CombatActionKind.MeleeAttack or
            CombatActionKind.WeaponParry or
            CombatActionKind.Dodge or
            CombatActionKind.RangedAttack => RollOpponentCheck(
                request,
                opponent,
                profile,
                playerName,
                userId),
            CombatActionKind.Damage => RollOpponentDamage(
                request,
                opponent,
                profile,
                sessionSnapshot,
                playerName,
                userId),
            CombatActionKind.HitZone => RollOpponentHitZone(request, opponent, playerName, userId),
            _ => throw Validation("Dieser Gegnerwurf wird Ã¼ber die bestehende Session-Aktion ausgefÃ¼hrt.")
        };
    }

    private CombatRollResultDto RollOpponentCheck(
        CombatRollRequestDto request,
        CombatSessionParticipantDto opponent,
        CombatEnemyResolvedProfileDto profile,
        string playerName,
        string userId)
    {
        CombatEnemyAttackDto? attack = null;
        if (request.Action is CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack)
        {
            attack = ResolveCatalogAttack(profile, request);
            if (attack is null)
            {
                throw Validation("FÃ¼r diesen Gegner ist kein konkreter Katalogangriff ausgewÃ¤hlt.");
            }
        }

        var baseValue = request.Action switch
        {
            CombatActionKind.MeleeAttack => profile.Attack,
            CombatActionKind.WeaponParry => profile.Parry,
            CombatActionKind.Dodge => profile.Dodge,
            CombatActionKind.RangedAttack => profile.RangedValue,
            _ => null
        };
        if (!baseValue.HasValue)
        {
            throw Validation($"FÃ¼r {GetActionLabel(request.Action)} ist im Katalog kein Zielwert vorhanden.");
        }

        var options = request.Options ?? new CombatRuleOptionsDto();
        return RollCheckCore(
            request,
            null,
            opponent.Name,
            opponent.Id,
            attack?.Name,
            baseValue.Value,
            request.Modifiers ?? [],
            "DSA 4.1-Katalog",
            BuildCatalogRollNotes(profile, attack),
            options,
            playerName,
            userId);
    }

    private CombatRollResultDto RollOpponentDamage(
        CombatRollRequestDto request,
        CombatSessionParticipantDto opponent,
        CombatEnemyResolvedProfileDto profile,
        CombatSessionSnapshotDto sessionSnapshot,
        string playerName,
        string userId)
    {
        var exchange = sessionSnapshot.ActiveExchange;
        if (exchange is null ||
            !string.Equals(exchange.AttackerParticipantId, opponent.Id, StringComparison.Ordinal))
        {
            throw Validation("FÃ¼r diesen Gegner gibt es keinen offenen Angriff fÃ¼r einen TP-Wurf.");
        }

        var attack = ResolveCatalogAttack(profile, request, exchange)
                     ?? throw Validation("FÃ¼r diesen Gegner ist kein konkreter Katalogangriff ausgewÃ¤hlt.");
        var damageText = attack.Damage?.Notation;
        var damage = ParseDamageExpression(damageText);
        return RollDamageCore(
            request,
            null,
            opponent.Name,
            opponent.Id,
            attack.Name,
            damageText,
            damage,
            "DSA 4.1-Katalog",
            playerName,
            userId,
            BuildCatalogRollNotes(profile, attack));
    }

    private CombatRollResultDto RollOpponentHitZone(
        CombatRollRequestDto request,
        CombatSessionParticipantDto opponent,
        string playerName,
        string userId) => RollHitZoneCore(
        request,
        null,
        opponent.Name,
        opponent.Id,
        null,
        playerName,
        userId);

    private static CombatEnemyAttackDto? ResolveCatalogAttack(
        CombatEnemyResolvedProfileDto profile,
        CombatRollRequestDto request,
        CombatAttackExchangeDto? exchange = null)
    {
        var attackId = string.IsNullOrWhiteSpace(request.WeaponId)
            ? exchange?.WeaponId
            : request.WeaponId;
        attackId = string.IsNullOrWhiteSpace(attackId) ? profile.AttackId : attackId;
        if (string.IsNullOrWhiteSpace(attackId) && profile.Attacks.Length == 1)
        {
            return profile.Attacks[0];
        }

        if (!string.IsNullOrWhiteSpace(profile.AttackId) &&
            !string.Equals(attackId, profile.AttackId, StringComparison.Ordinal))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(attackId)
            ? null
            : profile.Attacks.FirstOrDefault(attack =>
                string.Equals(attack.Id, attackId.Trim(), StringComparison.Ordinal));
    }

    private static string[] BuildCatalogRollNotes(
        CombatEnemyResolvedProfileDto profile,
        CombatEnemyAttackDto? attack)
    {
        var notes = profile.SourceNotes.ToList();
        if (attack is not null)
        {
            notes.Add($"Angriff aus Katalog: {attack.Name}");
        }

        notes.AddRange(profile.SpecialRules.Select(rule =>
            string.IsNullOrWhiteSpace(rule.SourceNotation)
                ? $"Sonderregel {rule.RuleId} bleibt eine manuelle Entscheidung."
                : $"Sonderregel {rule.RuleId}: {rule.SourceNotation} bleibt eine manuelle Entscheidung."));
        return notes.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string GetReadinessText(CombatEnemyResolvedProfileDto profile) => profile.Readiness switch
    {
        "sourceDependent" => "ein Wertebereich braucht eine Meisterentscheidung",
        "requiresEquipmentValues" => "fÃ¼r die AusrÃ¼stung fehlen im Katalog TP-/RS-Werte",
        "requiresAttackSelection" => "ein konkreter Angriff muss ausgewÃ¤hlt werden",
        _ => "erforderliche Werte fehlen"
    };

    private CombatRollResultDto RollCheck(
        CombatRollRequestDto request,
        DsaWuerfelApp.Shared.Models.Hero hero,
        CombatProfileDto profile,
        CombatSetVariantDto set,
        string playerName,
        string userId)
    {
        var weapon = ResolveWeapon(request, set);
        var baseValue = ResolveCheckValue(request.Action, set, weapon);
        if (!baseValue.HasValue)
        {
            throw Validation($"Für {GetActionLabel(request.Action)} ist im importierten Kampfset kein Zielwert vorhanden.");
        }

        var options = request.Options ?? new CombatRuleOptionsDto();
        var runtimeModifiers = CombatRuntimeModifierRules.Resolve(
            profile,
            set,
            request.RuntimeState,
            request.Action,
            options);
        var modifiers = (request.Modifiers ?? [])
            .Concat(runtimeModifiers.Modifiers)
            .ToArray();
        var valuesSource = runtimeModifiers.Modifiers.Length > 0 || runtimeModifiers.RuleNotes.Length > 0
            ? "Import + laufender Kampfzustand"
            : "Import";
        return RollCheckCore(
            request,
            hero.Id,
            hero.Name,
            request.ParticipantId,
            weapon?.Name,
            baseValue.Value,
            modifiers,
            valuesSource,
            runtimeModifiers.RuleNotes,
            options,
            playerName,
            userId);
    }

    private CombatRollResultDto RollCheckCore(
        CombatRollRequestDto request,
        Guid? heroId,
        string actorName,
        string? participantId,
        string? weaponName,
        int baseValue,
        IReadOnlyList<CombatModifierDto> modifiers,
        string valuesSource,
        IReadOnlyList<string> ruleNotes,
        CombatRuleOptionsDto options,
        string playerName,
        string userId)
    {
        var mainRoll = RollSingleD20();
        var preliminary = CombatRollRules.Evaluate(
            request.Action,
            baseValue,
            modifiers,
            mainRoll,
            unmodifiedBaseValue: baseValue,
            options: options);

        int? controlRoll = null;
        if (preliminary.Outcome == CombatOutcome.Unknown && preliminary.ControlTarget.HasValue)
        {
            controlRoll = RollSingleD20();
        }

        var evaluation = CombatRollRules.Evaluate(
            request.Action,
            baseValue,
            modifiers,
            mainRoll,
            controlRoll,
            unmodifiedBaseValue: baseValue,
            options,
            valuesSource,
            ruleNotes);
        if (!evaluation.IsValid)
        {
            throw Validation(evaluation.ValidationMessage ?? "Kampfwurf konnte nicht ausgewertet werden.");
        }

        var rolls = controlRoll.HasValue
            ? new[] { new DiceRollDto(20, mainRoll), new DiceRollDto(20, controlRoll.Value) }
            : new[] { new DiceRollDto(20, mainRoll) };
        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            ParticipantId = participantId,
            ParticipantName = actorName,
            ExchangeId = request.ExchangeId,
            HeroId = heroId,
            Action = request.Action,
            ActionLabel = GetActionLabel(request.Action),
            WeaponName = weaponName,
            ValuesSource = valuesSource,
            BaseValue = baseValue,
            UnmodifiedBaseValue = baseValue,
            EffectiveTarget = evaluation.EffectiveTarget,
            ControlTarget = evaluation.ControlTarget,
            Modifiers = modifiers.ToArray(),
            RuleOptions = options,
            Outcome = evaluation.Outcome,
            StatusLabel = evaluation.StatusLabel,
            LabeledRolls = controlRoll.HasValue
                ? [
                    new CombatLabeledRollDto("Hauptwurf", 20, mainRoll),
                    new CombatLabeledRollDto("Kontrollwurf", 20, controlRoll.Value)
                ]
                : [new CombatLabeledRollDto("Hauptwurf", 20, mainRoll)],
            FollowUps = evaluation.FollowUps ?? [],
            RuleNotes = BuildRuleNotes(request, evaluation, ruleNotes)
        };

        return CreateResult(
            request,
            heroId,
            actorName,
            participantId,
            userId,
            playerName,
            snapshot,
            rolls,
            MapHistoryOutcome(evaluation.Outcome),
            BuildChecks(evaluation));
    }

    private CombatRollResultDto RollDamageCore(
        CombatRollRequestDto request,
        Guid? heroId,
        string actorName,
        string? participantId,
        string weaponName,
        string? damageText,
        DamageExpression damage,
        string valuesSource,
        string playerName,
        string userId,
        IReadOnlyList<string>? additionalNotes = null)
    {
        var manualDamageModifier = request.DamageModifier;
        var isCritical = request.Damage?.IsCritical == true;
        var rolls = diceService.RollDice([new DiceRollGroupDto(damage.DiceSides, damage.DiceCount)]);
        var calculation = CombatRollRules.CalculateDamage(
            rolls.Sum(roll => roll.Value),
            damage.WeaponBonus,
            0,
            1,
            manualDamageModifier,
            isCritical);
        var notes = BuildRuleNotes(request, null)
            .Append($"{valuesSource}: Schaden {damageText ?? "unbekannt"}")
            .Concat(additionalNotes ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            ParticipantId = participantId,
            ParticipantName = actorName,
            ExchangeId = request.ExchangeId,
            HeroId = heroId,
            Action = CombatActionKind.Damage,
            ActionLabel = GetActionLabel(CombatActionKind.Damage),
            WeaponName = weaponName,
            ValuesSource = valuesSource,
            Modifiers = request.Modifiers ?? [],
            RuleOptions = request.Options ?? new CombatRuleOptionsDto(),
            Outcome = CombatOutcome.Neutral,
            StatusLabel = "TP-Wurf Â· kein Treffer angewendet",
            LabeledRolls = rolls.Select(roll => new CombatLabeledRollDto("TP-WÃ¼rfel", roll.Sides, roll.Value)).ToArray(),
            Damage = new CombatDamageSnapshotDto(
                calculation.DiceTotal,
                calculation.WeaponBonus,
                calculation.PreMultiplierModifier,
                calculation.Multiplier,
                calculation.PostMultiplierModifier,
                calculation.Total,
                calculation.IsCritical,
                calculation.ArmorRating,
                calculation.StructurePoints),
            Zone = request.ResolvedZone,
            RuleNotes = notes
        };

        return CreateResult(
            request,
            heroId,
            actorName,
            participantId,
            userId,
            playerName,
            snapshot,
            rolls,
            RollHistoryOutcome.None,
            []);
    }

    private CombatRollResultDto RollDamage(
        CombatRollRequestDto request,
        DsaWuerfelApp.Shared.Models.Hero hero,
        CombatSetVariantDto set,
        string playerName,
        string userId)
    {
        var weapon = ResolveWeapon(request, set)
                     ?? throw Validation("Für einen TP-Wurf muss eine importierte Waffe ausgewählt sein.");
        var damageText = weapon.CalculatedDamage ?? weapon.BaseDamage;
        var damage = ParseDamageExpression(damageText);
        var manualDamageModifier = request.DamageModifier;
        var isCritical = request.Damage?.IsCritical == true;
        var rolls = diceService.RollDice([new DiceRollGroupDto(damage.DiceSides, damage.DiceCount)]);
        var calculation = CombatRollRules.CalculateDamage(
            rolls.Sum(roll => roll.Value),
            damage.WeaponBonus,
            0,
            1,
            manualDamageModifier,
            isCritical);
        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            ExchangeId = request.ExchangeId,
            HeroId = hero.Id,
            Action = CombatActionKind.Damage,
            ActionLabel = GetActionLabel(CombatActionKind.Damage),
            WeaponName = weapon.Name,
            ValuesSource = "Import",
            Modifiers = request.Modifiers ?? [],
            RuleOptions = request.Options ?? new CombatRuleOptionsDto(),
            Outcome = CombatOutcome.Neutral,
            StatusLabel = "TP-Wurf · kein Treffer angewendet",
            LabeledRolls = rolls.Select(roll => new CombatLabeledRollDto("TP-Würfel", roll.Sides, roll.Value)).ToArray(),
            Damage = new CombatDamageSnapshotDto(
                calculation.DiceTotal,
                calculation.WeaponBonus,
                calculation.PreMultiplierModifier,
                calculation.Multiplier,
                calculation.PostMultiplierModifier,
                calculation.Total,
                calculation.IsCritical,
                calculation.ArmorRating,
                calculation.StructurePoints),
            Zone = request.ResolvedZone,
            RuleNotes = BuildRuleNotes(request, null)
                .Append($"Importierter Schaden: {damageText ?? "unbekannt"}")
                .ToArray()
        };

        return CreateResult(
            request,
            hero,
            userId,
            playerName,
            snapshot,
            rolls,
            RollHistoryOutcome.None,
            []);
    }

    private CombatRollResultDto RollHitZoneCore(
        CombatRollRequestDto request,
        Guid? heroId,
        string actorName,
        string? participantId,
        CombatSetVariantDto? set,
        string playerName,
        string userId)
    {
        var d20 = RollSingleD20();
        var mapped = CombatZoneRules.ResolveHitZone(d20, request.Zone);
        var armorRating = CombatZoneRules.ResolveArmorRating(set, mapped.ArmorZone);
        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            ParticipantId = participantId,
            ParticipantName = actorName,
            ExchangeId = request.ExchangeId,
            HeroId = heroId,
            Action = CombatActionKind.HitZone,
            ActionLabel = GetActionLabel(CombatActionKind.HitZone),
            ValuesSource = "Regeltabelle",
            RuleOptions = request.Options ?? new CombatRuleOptionsDto(),
            Outcome = CombatOutcome.Neutral,
            StatusLabel = "Trefferzone gewÃ¼rfelt",
            LabeledRolls = [new CombatLabeledRollDto("Trefferzonenwurf", 20, d20)],
            Zone = new CombatZoneSnapshotDto(d20, mapped.ArmorZone, mapped.WoundZone, request.Zone?.Facing ?? CombatFacing.Front, armorRating),
            RuleNotes = BuildRuleNotes(request, null)
        };

        return CreateResult(
            request,
            heroId,
            actorName,
            participantId,
            userId,
            playerName,
            snapshot,
            [new DiceRollDto(20, d20)],
            RollHistoryOutcome.None,
            []);
    }

    private CombatRollResultDto RollHitZone(
        CombatRollRequestDto request,
        DsaWuerfelApp.Shared.Models.Hero hero,
        CombatSetVariantDto set,
        string playerName,
        string userId)
    {
        var d20 = RollSingleD20();
        var mapped = CombatZoneRules.ResolveHitZone(d20, request.Zone);
        var armorRating = CombatZoneRules.ResolveArmorRating(set, mapped.ArmorZone);
        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            ExchangeId = request.ExchangeId,
            HeroId = hero.Id,
            Action = CombatActionKind.HitZone,
            ActionLabel = GetActionLabel(CombatActionKind.HitZone),
            ValuesSource = "Regeltabelle",
            RuleOptions = request.Options ?? new CombatRuleOptionsDto(),
            Outcome = CombatOutcome.Neutral,
            StatusLabel = "Trefferzone gewürfelt",
            LabeledRolls = [new CombatLabeledRollDto("Trefferzonenwurf", 20, d20)],
            Zone = new CombatZoneSnapshotDto(d20, mapped.ArmorZone, mapped.WoundZone, request.Zone?.Facing ?? CombatFacing.Front, armorRating),
            RuleNotes = BuildRuleNotes(request, null)
        };

        return CreateResult(
            request,
            hero,
            userId,
            playerName,
            snapshot,
            [new DiceRollDto(20, d20)],
            RollHistoryOutcome.None,
            []);
    }

    private CombatRollResultDto RollHelper(
        CombatRollRequestDto request,
        DsaWuerfelApp.Shared.Models.Hero hero,
        string playerName,
        string userId)
    {
        var helper = request.Helper ?? new CombatHelperRollRequestDto();
        ValidateDice(helper.DiceCount, helper.DiceSides);
        var purpose = string.IsNullOrWhiteSpace(helper.Purpose)
            ? GetActionLabel(request.Action)
            : helper.Purpose.Trim();
        var rolls = diceService.RollDice([new DiceRollGroupDto(helper.DiceSides, helper.DiceCount)]);
        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            ExchangeId = request.ExchangeId,
            HeroId = hero.Id,
            Action = request.Action,
            ActionLabel = GetActionLabel(request.Action),
            ValuesSource = "Regelhilfe",
            BaseValue = request.BaseValue,
            Modifiers = request.Modifiers ?? [],
            RuleOptions = request.Options ?? new CombatRuleOptionsDto(),
            Outcome = CombatOutcome.Neutral,
            StatusLabel = request.Action == CombatActionKind.InitiativeHelper
                ? "Initiative gewürfelt"
                : $"Hilfswurf · {purpose}",
            LabeledRolls = rolls.Select(roll => new CombatLabeledRollDto(
                request.Action == CombatActionKind.InitiativeHelper ? "INI-Wurf" : purpose,
                roll.Sides,
                roll.Value)).ToArray(),
            RuleNotes = request.Action == CombatActionKind.InitiativeHelper
                ? []
                : BuildRuleNotes(request, null).Append($"Zweck: {purpose}").ToArray()
        };

        return CreateResult(request, hero, userId, playerName, snapshot, rolls, RollHistoryOutcome.None, []);
    }

    private CombatRollResultDto CreateResult(
        CombatRollRequestDto request,
        DsaWuerfelApp.Shared.Models.Hero hero,
        string userId,
        string playerName,
        CombatRollSnapshotDto snapshot,
        IReadOnlyList<DiceRollDto> rolls,
        RollHistoryOutcome historyOutcome,
        RollHistoryCheckDto[] checks) => CreateResult(
        request,
        hero.Id,
        hero.Name,
        request.ParticipantId,
        userId,
        playerName,
        snapshot,
        rolls,
        historyOutcome,
        checks);

    private CombatRollResultDto CreateResult(
        CombatRollRequestDto request,
        Guid? heroId,
        string actorName,
        string? participantId,
        string userId,
        string playerName,
        CombatRollSnapshotDto snapshot,
        IReadOnlyList<DiceRollDto> rolls,
        RollHistoryOutcome historyOutcome,
        RollHistoryCheckDto[] checks)
    {
        snapshot = snapshot with
        {
            HeroId = heroId,
            ParticipantId = participantId,
            ParticipantName = actorName
        };
        var timestamp = DateTime.UtcNow;
        var rollArray = rolls.ToArray();
        var historyModifier = snapshot.Action == CombatActionKind.InitiativeHelper
            ? (snapshot.BaseValue ?? 0) + snapshot.Modifiers.Sum(modifier => modifier.Value)
            : 0;
        var equation = DiceResultFactory.CreateEquation(rollArray, historyModifier);
        var historyContext = new RollHistoryContextDto(
            RollHistoryKind.Combat,
            GetHistoryDisplayName(snapshot),
            historyOutcome,
            null,
            checks,
            new RollHistorySnapshotDto
            {
                HeroName = heroId.HasValue ? actorName : null,
                ParticipantName = actorName,
                Combat = snapshot
            });
        var historyEntry = DiceResultFactory.CreateHistoryEntry(playerName, timestamp, equation, historyContext);

        return new CombatRollResultDto(
            snapshot.EntryId,
            request.RequestId,
            request.SessionId,
            userId,
            playerName,
            heroId.HasValue ? actorName : null,
            snapshot.Outcome,
            snapshot,
            rollArray,
            historyEntry)
        {
            ParticipantId = participantId,
            ParticipantName = actorName
        };
    }

    private int RollSingleD20() => diceService.RollDice([new DiceRollGroupDto(20, 1)])[0].Value;

    private static CombatSetVariantDto ResolveSet(CombatProfileDto profile, string? setId)
    {
        var set = string.IsNullOrWhiteSpace(setId)
            ? profile.Sets.FirstOrDefault(current => current.IsInUse) ?? profile.Sets.FirstOrDefault(current => current.IsDefault)
            : profile.Sets.FirstOrDefault(current => string.Equals(current.Id, setId, StringComparison.Ordinal));
        return set ?? throw Validation("Das ausgewählte Kampfset ist im Import nicht vorhanden.");
    }

    private static CombatWeaponDto? ResolveWeapon(CombatRollRequestDto request, CombatSetVariantDto set)
    {
        if (request.Action == CombatActionKind.Dodge)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.WeaponId))
        {
            throw Validation("Für diese Kampfaktion muss eine Waffe oder Abwehr ausgewählt sein.");
        }

        var weapon = set.Weapons.FirstOrDefault(current =>
            string.Equals(current.Id, request.WeaponId, StringComparison.Ordinal));
        if (weapon is null)
        {
            throw Validation("Die ausgewählte Waffe gehört nicht zum Kampfset.");
        }

        var valid = request.Action switch
        {
            CombatActionKind.MeleeAttack or CombatActionKind.WeaponParry or CombatActionKind.Damage =>
                weapon.Category == CombatWeaponCategory.Melee,
            CombatActionKind.ShieldParry => weapon.Category == CombatWeaponCategory.Shield,
            CombatActionKind.RangedAttack => weapon.Category == CombatWeaponCategory.Ranged,
            _ => true
        };
        if (!valid)
        {
            throw Validation("Waffe und Kampfaktion passen nicht zusammen.");
        }

        return weapon;
    }

    private static int? ResolveCheckValue(
        CombatActionKind action,
        CombatSetVariantDto set,
        CombatWeaponDto? weapon) => action switch
    {
        CombatActionKind.MeleeAttack => weapon?.Attack,
        CombatActionKind.WeaponParry => weapon?.Parry,
        CombatActionKind.ShieldParry => weapon?.Parry,
        CombatActionKind.Dodge => set.Dodge,
        CombatActionKind.RangedAttack => weapon?.RangedValue,
        _ => null
    };

    private static DamageExpression ParseDamageExpression(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Validation("Für diese Waffe ist keine berechnete TP-Formel importiert.");
        }

        var match = DamagePattern().Match(value.Replace(" ", string.Empty));
        if (!match.Success ||
            !int.TryParse(match.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            throw Validation($"Die importierte TP-Formel „{value}“ konnte nicht ausgewertet werden.");
        }

        var sides = match.Groups["sides"].Success && match.Groups["sides"].Length > 0
            ? int.Parse(match.Groups["sides"].Value, CultureInfo.InvariantCulture)
            : 6;

        var modifier = 0;
        if (match.Groups["modifier"].Success)
        {
            modifier = int.Parse(match.Groups["modifier"].Value, CultureInfo.InvariantCulture);
        }

        ValidateDice(count, sides);
        return new DamageExpression(count, sides, modifier);
    }

    private static RollHistoryCheckDto[] BuildChecks(CombatRollEvaluationDto evaluation)
    {
        var checks = new List<RollHistoryCheckDto>
        {
            new(
                GetMainCheckName(evaluation.Action),
                evaluation.MainRoll,
                evaluation.EffectiveTarget ?? 0,
                Math.Max(0, evaluation.MainRoll - (evaluation.EffectiveTarget ?? 0)),
                evaluation.IsSuccessful ? RollHistoryCheckState.WithinTarget : RollHistoryCheckState.Failed)
        };

        if (evaluation.ControlRoll is { } controlRoll && evaluation.ControlTarget is { } controlTarget)
        {
            var succeeded = evaluation.Outcome is CombatOutcome.Critical or CombatOutcome.Lucky or
                CombatOutcome.Success or CombatOutcome.FumbleAvoided;
            checks.Add(new RollHistoryCheckDto(
                "Kontrolle",
                controlRoll,
                controlTarget,
                Math.Max(0, controlRoll - controlTarget),
                succeeded ? RollHistoryCheckState.WithinTarget : RollHistoryCheckState.Failed));
        }

        return checks.ToArray();
    }

    private static string[] BuildRuleNotes(
        CombatRollRequestDto request,
        CombatRollEvaluationDto? evaluation,
        IReadOnlyList<string>? runtimeNotes = null)
    {
        var notes = new List<string>();
        if (evaluation?.RequiresDefenseDecision == true)
        {
            notes.Add("Die AT ist gelungen und öffnet die Abwehrentscheidung; sie wendet keinen Treffer an.");
        }

        if (evaluation?.FollowUps is { Length: > 0 } followUps)
        {
            notes.AddRange(followUps.Select(followUp =>
                $"Meisterentscheidung erforderlich: {followUp.Purpose}"));
        }

        if (request.Modifiers is { Length: > 0 })
        {
            notes.Add("Situative Modifikatoren sind im Snapshot einzeln gespeichert.");
        }

        if (runtimeNotes is { Count: > 0 })
        {
            notes.AddRange(runtimeNotes);
        }

        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            notes.Add($"Manuelle Notiz: {request.Note.Trim()}");
        }

        return notes.ToArray();
    }

    private static RollHistoryOutcome MapHistoryOutcome(CombatOutcome outcome) => outcome switch
    {
        CombatOutcome.Success => RollHistoryOutcome.Success,
        CombatOutcome.Lucky or CombatOutcome.Critical => RollHistoryOutcome.CriticalSuccess,
        CombatOutcome.Fumble => RollHistoryOutcome.Fumble,
        CombatOutcome.Failure or CombatOutcome.FumbleAvoided => RollHistoryOutcome.Failure,
        _ => RollHistoryOutcome.None
    };

    private static string GetHistoryDisplayName(CombatRollSnapshotDto snapshot)
    {
        var action = snapshot.ActionLabel ?? GetActionLabel(snapshot.Action);
        return string.IsNullOrWhiteSpace(snapshot.WeaponName)
            ? action
            : $"{action} · {snapshot.WeaponName.Trim()}";
    }

    private static string GetMainCheckName(CombatActionKind action) => action switch
    {
        CombatActionKind.MeleeAttack => "AT",
        CombatActionKind.WeaponParry or CombatActionKind.ShieldParry => "PA",
        CombatActionKind.Dodge => "AW",
        CombatActionKind.RangedAttack => "FK",
        _ => "Wurf"
    };

    private static string GetActionLabel(CombatActionKind action) => action switch
    {
        CombatActionKind.MeleeAttack => "Attacke",
        CombatActionKind.WeaponParry => "Waffenparade",
        CombatActionKind.ShieldParry => "Schildparade",
        CombatActionKind.Dodge => "Ausweichen",
        CombatActionKind.RangedAttack => "Fernkampf",
        CombatActionKind.Damage => "Trefferpunkte",
        CombatActionKind.HitZone => "Trefferzone",
        CombatActionKind.InitiativeHelper => "Initiative",
        CombatActionKind.WoundHelper => "Wund-Hilfswurf",
        CombatActionKind.FumbleHelper => "Patzer-Hilfswurf",
        _ => "Kampfwurf"
    };

    private static void ValidateRequest(CombatRollRequestDto request)
    {
        if (request.RequestId == Guid.Empty)
        {
            throw Validation("Jeder Kampfwurf braucht eine RequestId.");
        }

        if (!Enum.IsDefined(request.Action))
        {
            throw Validation("Unbekannte Kampfwurfart.");
        }

        foreach (var modifier in request.Modifiers ?? [])
        {
            if (modifier is null || string.IsNullOrWhiteSpace(modifier.Label))
            {
                throw Validation("Modifikatoren brauchen eine Bezeichnung.");
            }

            if (modifier.Value is < -999 or > 999)
            {
                throw Validation($"Modifikator {modifier.Label} liegt außerhalb des zulässigen Bereichs.");
            }
        }

        if (request.DamageModifier is < -999 or > 999)
        {
            throw Validation("Der TP-Modifikator liegt außerhalb des zulässigen Bereichs.");
        }

        if (request.RuntimeState is { } runtimeState)
        {
            if (runtimeState.CurrentLeP is < -RuntimeValueLimit or > RuntimeValueLimit)
            {
                throw Validation("Der laufende LeP-Wert liegt außerhalb des zulässigen Bereichs.");
            }

            if (runtimeState.CurrentAuP is < 0 or > RuntimeValueLimit)
            {
                throw Validation("Der laufende AuP-Wert liegt außerhalb des zulässigen Bereichs.");
            }

            foreach (var wound in runtimeState.Wounds ?? [])
            {
                if (!Enum.IsDefined(wound.Key) || wound.Value is < 0 or > 3)
                {
                    throw Validation("Laufende Wundstände müssen pro Zone zwischen 0 und 3 liegen.");
                }
            }
        }
    }

    private static void ValidateDice(int count, int sides)
    {
        if (count is < 1 or > DiceService.MaxDicePerGroup)
        {
            throw Validation($"Es sind höchstens {DiceService.MaxDicePerGroup} Würfel erlaubt.");
        }

        if (sides is < DiceService.MinSides or > DiceService.MaxSides)
        {
            throw Validation("Die Würfelgröße ist nicht zulässig.");
        }
    }

    private static RequestRejectedException Validation(string message) =>
        new(RequestRejectionReason.Validation, message);

    [GeneratedRegex(@"(?<count>\d+)[Ww](?<sides>\d*)(?<modifier>[+-]\d+)?")]
    private static partial Regex DamagePattern();

    private sealed record DamageExpression(int DiceCount, int DiceSides, int WeaponBonus);
}
