using System.Text;

using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Services;
using DsaWuerfelApp.Services.Application.Import;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;
using DsaWuerfelApp.Tests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace DsaWuerfelApp.Tests;

public sealed class CombatSessionStateTests
{
    [Fact]
    public async Task Initiative_mutations_are_idempotent_revision_checked_and_undoable()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();

        var initial = await state.GetAsync(session.SessionId, "owner");
        var rollRequest = new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        };

        var rolled = await state.MutateAsync(rollRequest, "owner");
        var rolledParticipant = Assert.Single(rolled.Snapshot.Participants, participant => participant.HeroId == hero.Id);
        Assert.True(rolled.Applied);
        Assert.True(rolled.Snapshot.IsStarted);
        Assert.Equal(11, rolledParticipant.InitiativeBase);
        Assert.Equal(1, rolledParticipant.InitiativeDiceCount);
        Assert.Equal(rolledParticipant.InitiativeBase + rolledParticipant.StartRoll, rolledParticipant.CurrentInitiative);
        Assert.Single(rolled.Snapshot.Actions, action => action.ParticipantId == rolledParticipant.Id);

        var duplicate = await state.MutateAsync(rollRequest, "owner");
        Assert.True(duplicate.AlreadyApplied);
        Assert.Equal(rolled.Snapshot.Revision, duplicate.Snapshot.Revision);
        Assert.Equal(rolledParticipant.CurrentInitiative,
            Assert.Single(duplicate.Snapshot.Participants, participant => participant.HeroId == hero.Id).CurrentInitiative);

        await Assert.ThrowsAsync<RequestRejectedException>(() => state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner"));

        var stale = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.SetInitiative,
            HeroId = hero.Id,
            Initiative = 19
        }, "owner");
        Assert.True(stale.Stale);
        Assert.Equal(rolled.Snapshot.Revision, stale.Snapshot.Revision);

        var corrected = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.SetInitiative,
            HeroId = hero.Id,
            Initiative = 19
        }, "owner");
        Assert.True(corrected.Applied);
        Assert.Equal(19, Assert.Single(corrected.Snapshot.Participants, participant => participant.HeroId == hero.Id).CurrentInitiative);

        var undone = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = corrected.Snapshot.Revision,
            Kind = CombatSessionMutationKind.Undo
        }, "owner");
        Assert.True(undone.Applied);
        Assert.Equal(rolledParticipant.CurrentInitiative,
            Assert.Single(undone.Snapshot.Participants, participant => participant.HeroId == hero.Id).CurrentInitiative);
        Assert.Equal(corrected.Snapshot.Revision + 1, undone.Snapshot.Revision);
    }

    [Fact]
    public async Task Session_authorization_rejects_foreign_hero_changes_and_preserves_state_on_reconnect()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var sessions = factory.Services.GetRequiredService<SessionService>();
        sessions.AddPlayer(session.SessionId, new PlayerInfo { UserId = "other", Name = "Andere" });
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();

        var initial = await state.GetAsync(session.SessionId, "other");
        await Assert.ThrowsAsync<RequestRejectedException>(() => state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.SetInitiative,
            HeroId = hero.Id,
            Initiative = 20
        }, "other"));

        var ownerState = await state.GetAsync(session.SessionId, "owner");
        var refreshedState = await state.GetAsync(session.SessionId, "other");
        Assert.Equal(ownerState.Revision, refreshedState.Revision);
        Assert.Equal(ownerState.Participants.Select(participant => participant.Id),
            refreshedState.Participants.Select(participant => participant.Id));
    }

    [Fact]
    public async Task Opponent_profile_is_persisted_without_inventing_missing_combat_values()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var initial = await state.GetAsync(session.SessionId, "owner");
        var profile = new CombatOpponentProfileDto(14, 12, 10, 3, 20, 5);

        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Ork",
            InitiativeBase = 10,
            OpponentProfile = profile
        }, "owner");

        var opponent = Assert.Single(added.Snapshot.Participants, participant =>
            participant.Kind == CombatParticipantKind.Opponent);
        Assert.Equal(profile, opponent.OpponentProfile);

        var reloaded = await state.GetAsync(session.SessionId, "owner");
        Assert.Equal(profile, Assert.Single(reloaded.Participants, participant =>
            participant.Id == opponent.Id).OpponentProfile);
    }

    [Fact]
    public async Task Attack_declaration_validates_target_and_loadout_before_consuming_the_action()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var profile = await factory.Services.GetRequiredService<HeroCombatProfileReader>().ReadAsync(hero.Id, "owner");
        var set = Assert.Single(profile!.Sets, item => item.ArmorModel == CombatArmorModel.Zone);
        var weapon = Assert.Single(set.Weapons, item => item.Name == "Schwert");
        var unavailableWeapon = Assert.Single(set.Weapons, item => item.Name == "Unbereites Schwert");

        var initial = await state.GetAsync(session.SessionId, "owner");
        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var attacker = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, item => item.ParticipantId == attacker.Id);
        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Übungsgegner",
            InitiativeBase = 1,
            Initiative = 1,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, 7, 2, 20)
        }, "owner");
        var target = Assert.Single(added.Snapshot.Participants, item => item.Kind == CombatParticipantKind.Opponent);

        var invalidTarget = new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = added.Snapshot.Revision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = "opponent:not-in-session",
            ActionId = action.Id,
            ExchangeId = "exchange-invalid-target",
            SetId = set.Id,
            WeaponId = weapon.Id,
            ActionKind = CombatActionKind.MeleeAttack
        };
        await Assert.ThrowsAsync<RequestRejectedException>(() => state.MutateAsync(invalidTarget, "owner"));

        var invalidWeapon = invalidTarget with
        {
            RequestId = Guid.NewGuid(),
            TargetParticipantId = target.Id,
            ExchangeId = "exchange-unavailable-weapon",
            WeaponId = unavailableWeapon.Id
        };
        await Assert.ThrowsAsync<RequestRejectedException>(() => state.MutateAsync(invalidWeapon, "owner"));
        var unchanged = await state.GetAsync(session.SessionId, "owner");
        Assert.Equal(added.Snapshot.Revision, unchanged.Revision);
        Assert.Null(unchanged.ActiveExchange);

        var declaration = await state.MutateAsync(invalidWeapon with
        {
            RequestId = Guid.NewGuid(),
            ExchangeId = "exchange-valid",
            WeaponId = weapon.Id
        }, "owner");

        Assert.True(declaration.Applied);
        Assert.Equal(added.Snapshot.Revision + 1, declaration.Snapshot.Revision);
        Assert.NotNull(declaration.Snapshot.ActiveExchange);
        Assert.Equal("exchange-valid", declaration.Snapshot.ActiveExchange!.ExchangeId);
        Assert.Equal(CombatExchangeStatus.Declared, declaration.Snapshot.ActiveExchange.Status);
        Assert.Equal(attacker.Id, declaration.Snapshot.ActiveExchange.AttackerParticipantId);
        Assert.Equal(target.Id, declaration.Snapshot.ActiveExchange.TargetParticipantId);
        Assert.Equal([CombatActionKind.WeaponParry, CombatActionKind.Dodge],
            declaration.Snapshot.ActiveExchange.AllowedDefenseActions);
        Assert.Equal(1, Assert.Single(declaration.Snapshot.Participants, item => item.Id == attacker.Id)
            .ActionBudget?.NormalActionsRemaining);
        Assert.Equal(CombatActionEntryState.Open,
            Assert.Single(declaration.Snapshot.Actions, item => item.Id == action.Id).State);
    }

    [Fact]
    public async Task Attack_roll_is_bound_to_the_declared_exchange_and_cannot_be_rolled_twice()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var handler = factory.Services.GetRequiredService<RollCombatHandler>();
        var profile = await factory.Services.GetRequiredService<HeroCombatProfileReader>().ReadAsync(hero.Id, "owner");
        var set = Assert.Single(profile!.Sets, item => item.ArmorModel == CombatArmorModel.Zone);
        var weapon = Assert.Single(set.Weapons, item => item.Name == "Schwert");

        var initial = await state.GetAsync(session.SessionId, "owner");
        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var attacker = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, item => item.ParticipantId == attacker.Id);
        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Übungsgegner",
            InitiativeBase = 1,
            Initiative = 1,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, 7, 2, 20)
        }, "owner");
        var target = Assert.Single(added.Snapshot.Participants, item => item.Kind == CombatParticipantKind.Opponent);
        var declaration = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = added.Snapshot.Revision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExchangeId = "exchange-roll",
            SetId = set.Id,
            WeaponId = weapon.Id,
            ActionKind = CombatActionKind.MeleeAttack
        }, "owner");

        var rollRequest = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = declaration.Snapshot.ActiveExchange!.ActionId,
            ExpectedRevision = declaration.Snapshot.Revision,
            HeroId = hero.Id,
            ExchangeId = "exchange-roll",
            SetId = set.Id,
            WeaponId = weapon.Id,
            WeaponName = weapon.Name,
            Action = CombatActionKind.MeleeAttack
        };
        var result = await handler.HandleAsync(rollRequest, "owner", "Besitzer");

        Assert.NotNull(result.CombatSessionSnapshot);
        Assert.Equal(rollRequest.ExchangeId, result.Snapshot.ExchangeId);
        Assert.Equal(result.Snapshot.EntryId, result.CombatSessionSnapshot!.ActiveExchange!.HistoryEntryIds.Single());
        Assert.NotEqual(CombatExchangeStatus.Declared, result.CombatSessionSnapshot.ActiveExchange.Status);
        Assert.NotNull(result.CombatSessionSnapshot.ActiveExchange.AttackResult);
        Assert.Equal(0, Assert.Single(result.CombatSessionSnapshot.Participants, item => item.Id == attacker.Id)
            .ActionBudget?.NormalActionsRemaining);
        var stored = await state.GetAsync(session.SessionId, "owner");
        Assert.Equal(result.CombatSessionSnapshot.Revision, stored.Revision);
        Assert.Equal(result.CombatSessionSnapshot.ActiveExchange.Status, stored.ActiveExchange!.Status);

        var retry = await handler.HandleAsync(rollRequest, "owner", "Besitzer");
        Assert.Equal(result.Snapshot.EntryId, retry.Snapshot.EntryId);
        Assert.Equal(result.Snapshot.LabeledRolls.Select(roll => roll.Value),
            retry.Snapshot.LabeledRolls.Select(roll => roll.Value));
        Assert.Equal(stored.Revision, retry.CombatSessionSnapshot!.Revision);

        await Assert.ThrowsAsync<RequestRejectedException>(() => handler.HandleAsync(
            rollRequest with { RequestId = Guid.NewGuid() },
            "owner",
            "Besitzer"));
        var unchanged = await state.GetAsync(session.SessionId, "owner");
        Assert.Equal(stored.Revision, unchanged.Revision);
        Assert.Equal(stored.ActiveExchange!.AttackResult!.MainRoll,
            unchanged.ActiveExchange!.AttackResult!.MainRoll);
        Assert.Equal(stored.ActiveExchange.Status, unchanged.ActiveExchange.Status);
        Assert.Equal(declaration.Snapshot.Revision + 1, stored.Revision);
    }

    [Fact]
    public async Task Attack_roll_prepares_and_binds_the_exchange_without_a_client_declaration()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var handler = factory.Services.GetRequiredService<RollCombatHandler>();
        var profile = await factory.Services.GetRequiredService<HeroCombatProfileReader>().ReadAsync(hero.Id, "owner");
        var set = Assert.Single(profile!.Sets, item => item.ArmorModel == CombatArmorModel.Zone);
        var weapon = Assert.Single(set.Weapons, item => item.Name == "Schwert");

        var initial = await state.GetAsync(session.SessionId, "owner");
        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var attacker = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, item => item.ParticipantId == attacker.Id);
        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Übungsgegner",
            InitiativeBase = 1,
            Initiative = 1,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, 7, 2, 20)
        }, "owner");
        var target = Assert.Single(added.Snapshot.Participants, item => item.Kind == CombatParticipantKind.Opponent);

        var invalidRequest = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ParticipantId = attacker.Id,
            TargetParticipantId = "opponent:not-in-session",
            ActionId = action.Id,
            ExpectedRevision = added.Snapshot.Revision,
            HeroId = hero.Id,
            SetId = set.Id,
            WeaponId = weapon.Id,
            Action = CombatActionKind.MeleeAttack
        };
        await Assert.ThrowsAsync<RequestRejectedException>(() => handler.HandleAsync(invalidRequest, "owner"));
        var unchanged = await state.GetAsync(session.SessionId, "owner");
        Assert.Null(unchanged.ActiveExchange);
        Assert.Equal(1, Assert.Single(unchanged.Participants, item => item.Id == attacker.Id)
            .ActionBudget?.NormalActionsRemaining);

        var request = invalidRequest with
        {
            RequestId = Guid.NewGuid(),
            TargetParticipantId = target.Id,
            WeaponName = weapon.Name
        };
        var result = await handler.HandleAsync(request, "owner", "Besitzer");

        var exchange = result.CombatSessionSnapshot?.ActiveExchange;
        Assert.NotNull(exchange);
        Assert.Equal(request.RequestId, exchange!.RequestId);
        Assert.Equal(request.RequestId, exchange.AttackRollRequestId);
        Assert.NotNull(exchange.AttackResult);
        Assert.Equal(0, Assert.Single(result.CombatSessionSnapshot!.Participants,
                item => item.Id == attacker.Id).ActionBudget?.NormalActionsRemaining);

        var retry = await handler.HandleAsync(request, "owner", "Besitzer");
        Assert.Equal(result.Snapshot.EntryId, retry.Snapshot.EntryId);
        Assert.Equal(result.CombatSessionSnapshot.Revision, retry.CombatSessionSnapshot!.Revision);
    }

    [Fact]
    public async Task Stored_attack_outcome_can_be_recovered_before_the_result_cache_is_written()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var handler = factory.Services.GetRequiredService<RollCombatHandler>();
        var profile = await factory.Services.GetRequiredService<HeroCombatProfileReader>().ReadAsync(hero.Id, "owner");
        var set = Assert.Single(profile!.Sets, item => item.ArmorModel == CombatArmorModel.Zone);
        var weapon = Assert.Single(set.Weapons, item => item.Name == "Schwert");

        var initial = await state.GetAsync(session.SessionId, "owner");
        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var attacker = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, item => item.ParticipantId == attacker.Id);
        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Wiederholungsziel",
            InitiativeBase = 1,
            Initiative = 1,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, 7, 2, 20)
        }, "owner");
        var target = Assert.Single(added.Snapshot.Participants, item => item.Kind == CombatParticipantKind.Opponent);
        var declaration = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = added.Snapshot.Revision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExchangeId = "exchange-recoverable",
            SetId = set.Id,
            WeaponId = weapon.Id,
            ActionKind = CombatActionKind.MeleeAttack
        }, "owner");

        var request = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExpectedRevision = declaration.Snapshot.Revision,
            HeroId = hero.Id,
            ExchangeId = "exchange-recoverable",
            SetId = set.Id,
            WeaponId = weapon.Id,
            WeaponName = weapon.Name,
            Action = CombatActionKind.MeleeAttack
        };
        var storedAttack = CreateCombatResult(request, 8, 14, CombatOutcome.Success);
        var bound = await state.BindAttackRollAsync(request, storedAttack, "owner");

        var recovered = await handler.HandleAsync(request, "owner", "Besitzer");

        Assert.Equal(bound.Revision, recovered.CombatSessionSnapshot!.Revision);
        Assert.Equal(storedAttack.Snapshot.EntryId, recovered.Snapshot.EntryId);
        Assert.Equal(storedAttack.Snapshot.LabeledRolls.Select(roll => roll.Value),
            recovered.Snapshot.LabeledRolls.Select(roll => roll.Value));
        Assert.Equal(CombatExchangeStatus.DefenseOpen,
            recovered.CombatSessionSnapshot.ActiveExchange!.Status);

        var retry = await handler.HandleAsync(request, "owner", "Besitzer");
        Assert.Equal(recovered.Snapshot.EntryId, retry.Snapshot.EntryId);
        Assert.Equal(recovered.CombatSessionSnapshot.Revision, retry.CombatSessionSnapshot!.Revision);
    }

    [Fact]
    public async Task Session_roll_uses_authoritative_participant_runtime_instead_of_stale_request_state()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var handler = factory.Services.GetRequiredService<RollCombatHandler>();
        var profile = await factory.Services.GetRequiredService<HeroCombatProfileReader>().ReadAsync(hero.Id, "owner");
        var set = Assert.Single(profile!.Sets, item => item.ArmorModel == CombatArmorModel.Zone);
        var weapon = Assert.Single(set.Weapons, item => item.Name == "Schwert");
        var wounds = Enum.GetValues<CombatWoundZone>()
            .ToDictionary(zone => zone, _ => (int?)0);
        var authoritativeRuntime = new CombatRuntimeStateDto
        {
            IsStarted = true,
            CurrentLeP = 1,
            CurrentAuP = 28,
            Wounds = wounds
        };

        var initial = await state.GetAsync(session.SessionId, "owner");
        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id,
            RuntimeState = authoritativeRuntime,
            SetId = set.Id
        }, "owner");
        var attacker = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, item => item.ParticipantId == attacker.Id);

        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Testziel",
            InitiativeBase = 1,
            Initiative = 1,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, 7, 0, 20)
        }, "owner");
        var target = Assert.Single(added.Snapshot.Participants, item => item.Kind == CombatParticipantKind.Opponent);
        var declaration = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = added.Snapshot.Revision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExchangeId = "exchange-authoritative-runtime",
            SetId = set.Id,
            WeaponId = weapon.Id,
            ActionKind = CombatActionKind.MeleeAttack
        }, "owner");

        var result = await handler.HandleAsync(new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = declaration.Snapshot.ActiveExchange!.ActionId,
            ExpectedRevision = declaration.Snapshot.Revision,
            HeroId = hero.Id,
            ExchangeId = "exchange-authoritative-runtime",
            SetId = set.Id,
            WeaponId = weapon.Id,
            RuntimeState = new CombatRuntimeStateDto
            {
                IsStarted = true,
                CurrentLeP = 22,
                CurrentAuP = 28,
                Wounds = wounds
            },
            Options = new CombatRuleOptionsDto(SpecialResultsEnabled: false, LowLePEnabled: true),
            Action = CombatActionKind.MeleeAttack
        }, "owner", "Besitzer");

        var lowLePModifier = Assert.Single(result.Snapshot.Modifiers,
            modifier => modifier.Label == "Niedrige LeP");
        Assert.Equal(-3, lowLePModifier.Value);
        Assert.Equal(1, result.CombatSessionSnapshot!.Participants
            .Single(item => item.Id == attacker.Id).RuntimeState!.CurrentLeP);
    }

    [Fact]
    public async Task Defense_reaction_resolves_the_open_exchange_and_consumes_only_one_reaction()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var profile = await factory.Services.GetRequiredService<HeroCombatProfileReader>().ReadAsync(hero.Id, "owner");
        var set = Assert.Single(profile!.Sets, item => item.ArmorModel == CombatArmorModel.Zone);
        var weapon = Assert.Single(set.Weapons, item => item.Name == "Schwert");

        var initial = await state.GetAsync(session.SessionId, "owner");
        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var attacker = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, item => item.ParticipantId == attacker.Id);
        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Übungsgegner",
            InitiativeBase = 1,
            Initiative = 1,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, 7, 2, 20)
        }, "owner");
        var target = Assert.Single(added.Snapshot.Participants, item => item.Kind == CombatParticipantKind.Opponent);
        var targetAction = Assert.Single(added.Snapshot.Actions, item => item.ParticipantId == target.Id);
        var declaration = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = added.Snapshot.Revision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExchangeId = "exchange-defense",
            SetId = set.Id,
            WeaponId = weapon.Id,
            ActionKind = CombatActionKind.MeleeAttack
        }, "owner");

        var attackRequest = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = declaration.Snapshot.ActiveExchange!.ActionId,
            HeroId = hero.Id,
            ExchangeId = "exchange-defense",
            SetId = set.Id,
            WeaponId = weapon.Id,
            Action = CombatActionKind.MeleeAttack
        };
        var attackResult = CreateCombatResult(attackRequest, 8, 14, CombatOutcome.Success);
        var defenseOpen = await state.BindAttackRollAsync(attackRequest, attackResult, "owner");
        Assert.Equal(CombatExchangeStatus.DefenseOpen, defenseOpen.ActiveExchange!.Status);
        Assert.Equal(targetAction.Id, defenseOpen.CurrentActionId);
        Assert.Equal(targetAction.Id, Assert.Single(defenseOpen.CurrentActionIds));

        var defenseRequest = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExchangeId = "exchange-defense",
            Action = CombatActionKind.Dodge
        };
        var defenseResult = CreateCombatResult(defenseRequest, 6, 7, CombatOutcome.Success);
        var avoided = await state.BindDefenseRollAsync(defenseRequest, defenseResult, "owner");

        Assert.Equal(CombatExchangeStatus.Avoided, avoided.ActiveExchange!.Status);
        Assert.Equal(CombatActionKind.Dodge, avoided.ActiveExchange.SelectedDefenseAction);
        Assert.Equal(0, Assert.Single(avoided.Participants, item => item.Id == target.Id)
            .ActionBudget?.ReactionsRemaining);
        await Assert.ThrowsAsync<RequestRejectedException>(() => state.EnsureDefenseRollAvailabilityAsync(
            defenseRequest with { RequestId = Guid.NewGuid() }, "owner"));
        Assert.Equal(declaration.Snapshot.Revision + 2, avoided.Revision);

        var undone = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = avoided.Revision,
            Kind = CombatSessionMutationKind.Undo
        }, "owner");

        Assert.True(undone.Applied);
        Assert.Null(undone.Snapshot.ActiveExchange);
        Assert.Equal(1, Assert.Single(undone.Snapshot.Participants, item => item.Id == attacker.Id)
            .ActionBudget?.NormalActionsRemaining);
        Assert.Equal(1, Assert.Single(undone.Snapshot.Participants, item => item.Id == target.Id)
            .ActionBudget?.ReactionsRemaining);

        var luckyDeclaration = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = undone.Snapshot.Revision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExchangeId = "exchange-lucky-parry",
            SetId = set.Id,
            WeaponId = weapon.Id,
            ActionKind = CombatActionKind.MeleeAttack
        }, "owner");
        var luckyAttackRequest = attackRequest with
        {
            RequestId = Guid.NewGuid(),
            ExpectedRevision = luckyDeclaration.Snapshot.Revision,
            ActionId = luckyDeclaration.Snapshot.ActiveExchange!.ActionId,
            ExchangeId = "exchange-lucky-parry"
        };
        var luckyAttack = await state.BindAttackRollAsync(
            luckyAttackRequest,
            CreateCombatResult(luckyAttackRequest, 8, 14, CombatOutcome.Success),
            "owner");
        Assert.Equal(CombatExchangeStatus.DefenseOpen, luckyAttack.ActiveExchange!.Status);

        var luckyDefenseRequest = defenseRequest with
        {
            RequestId = Guid.NewGuid(),
            ExchangeId = "exchange-lucky-parry",
            Action = CombatActionKind.WeaponParry
        };
        var lucky = await state.BindDefenseRollAsync(
            luckyDefenseRequest,
            CreateCombatResult(luckyDefenseRequest, 1, 10, CombatOutcome.Lucky),
            "owner");

        Assert.Equal(CombatExchangeStatus.Avoided, lucky.ActiveExchange!.Status);
        Assert.Equal(1, Assert.Single(lucky.Participants, item => item.Id == target.Id)
            .ActionBudget?.ReactionsRemaining);
    }

    [Fact]
    public async Task Damage_application_updates_target_lep_wounds_and_is_idempotent()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var profile = await factory.Services.GetRequiredService<HeroCombatProfileReader>().ReadAsync(hero.Id, "owner");
        var set = Assert.Single(profile!.Sets, item => item.ArmorModel == CombatArmorModel.Zone);
        var weapon = Assert.Single(set.Weapons, item => item.Name == "Schwert");

        var initial = await state.GetAsync(session.SessionId, "owner");
        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var attacker = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, item => item.ParticipantId == attacker.Id);
        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Übungsgegner",
            InitiativeBase = 1,
            Initiative = 1,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, 7, 2, 20, 5)
        }, "owner");
        var target = Assert.Single(added.Snapshot.Participants, item => item.Kind == CombatParticipantKind.Opponent);
        var declaration = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = added.Snapshot.Revision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExchangeId = "exchange-damage",
            SetId = set.Id,
            WeaponId = weapon.Id,
            ActionKind = CombatActionKind.MeleeAttack
        }, "owner");

        var attackRequest = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            HeroId = hero.Id,
            ExchangeId = "exchange-damage",
            SetId = set.Id,
            WeaponId = weapon.Id,
            WeaponName = weapon.Name,
            Action = CombatActionKind.MeleeAttack
        };
        var attackResult = CreateCombatResult(attackRequest, 8, 14, CombatOutcome.Success);
        var attackOpen = await state.BindAttackRollAsync(attackRequest, attackResult, "owner");
        Assert.Equal(CombatExchangeStatus.DefenseOpen, attackOpen.ActiveExchange!.Status);

        var defenseRequest = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ParticipantId = target.Id,
            TargetParticipantId = attacker.Id,
            ActionId = attackOpen.ActiveExchange!.ActionId,
            ExchangeId = "exchange-damage",
            Action = CombatActionKind.Dodge
        };
        var defenseResult = CreateCombatResult(defenseRequest, 19, 7, CombatOutcome.Failure);
        var hit = await state.BindDefenseRollAsync(defenseRequest, defenseResult, "owner");
        Assert.Equal(CombatExchangeStatus.Hit, hit.ActiveExchange!.Status);

        Assert.Equal(0, Assert.Single(hit.Participants, item => item.Id == attacker.Id)
            .ActionBudget?.NormalActionsRemaining);

        var damageRequest = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            HeroId = hero.Id,
            ExchangeId = "exchange-damage",
            Action = CombatActionKind.Damage
        };
        var zone = new CombatZoneSnapshotDto(
            10,
            CombatArmorZone.Chest,
            CombatWoundZone.Torso,
            CombatFacing.Front,
            null);
        var zoneRequest = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            HeroId = hero.Id,
            ExchangeId = "exchange-damage",
            Action = CombatActionKind.HitZone,
            Zone = new CombatZoneRollRequestDto(
                CombatFacing.Front,
                CombatArmorZone.LeftArm,
                CombatArmorZone.RightArm)
        };
        var zoneResult = CreateZoneResult(zoneRequest, zone);
        var zoneOpen = await state.BindHitZoneAsync(zoneRequest, zoneResult, "owner");
        Assert.Equal(CombatExchangeStatus.DamageOpen, zoneOpen.ActiveExchange!.Status);
        Assert.Equal(2, zoneOpen.ActiveExchange.Zone!.ArmorRating);

        var damageResult = CreateDamageResult(damageRequest, 8, zone);
        var history = factory.Services.GetRequiredService<SessionRecordStore>();
        history.AppendHistoryEntry(session.SessionId, attackResult.HistoryEntry);
        history.AppendHistoryEntry(session.SessionId, defenseResult.HistoryEntry);
        history.AppendHistoryEntry(session.SessionId, zoneResult.HistoryEntry);
        history.AppendHistoryEntry(session.SessionId, damageResult.HistoryEntry);

        var completed = await state.ApplyDamageAsync(damageRequest, damageResult, "owner");

        Assert.Equal(zoneOpen.Revision + 1, completed.Revision);
        Assert.Equal(CombatExchangeStatus.Completed, completed.ActiveExchange!.Status);
        Assert.Equal(2, completed.ActiveExchange.Damage!.ArmorRating);
        Assert.Equal(6, completed.ActiveExchange.Damage.StructurePoints);
        Assert.Equal(2, completed.ActiveExchange.Zone!.ArmorRating);
        var targetAfterDamage = Assert.Single(completed.Participants, item => item.Id == target.Id);
        Assert.Equal(14, targetAfterDamage.RuntimeState!.CurrentLeP);
        Assert.Equal(1, targetAfterDamage.RuntimeState.Wounds[CombatWoundZone.Torso]);

        var repeated = await state.ApplyDamageAsync(damageRequest, damageResult, "owner");

        Assert.Equal(completed.Revision, repeated.Revision);
        Assert.Equal(14, Assert.Single(repeated.Participants, item => item.Id == target.Id)
            .RuntimeState!.CurrentLeP);
        Assert.Equal(1, Assert.Single(repeated.Participants, item => item.Id == target.Id)
            .RuntimeState!.Wounds[CombatWoundZone.Torso]);
        Assert.Equal(declaration.Snapshot.Revision + 4, completed.Revision);

        var undone = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = completed.Revision,
            Kind = CombatSessionMutationKind.Undo
        }, "owner");

        Assert.True(undone.Applied);
        Assert.Equal(completed.Revision + 1, undone.Snapshot.Revision);
        Assert.Null(undone.Snapshot.ActiveExchange);
        var restoredAttacker = Assert.Single(undone.Snapshot.Participants, item => item.Id == attacker.Id);
        var restoredTarget = Assert.Single(undone.Snapshot.Participants, item => item.Id == target.Id);
        Assert.Equal(1, restoredAttacker.ActionBudget?.NormalActionsRemaining);
        Assert.Equal(1, restoredTarget.ActionBudget?.ReactionsRemaining);
        Assert.Null(restoredTarget.RuntimeState);
        Assert.Contains(
            history.LoadHistory(session.SessionId),
            entry => entry.Context?.Snapshot?.Combat?.ExchangeId == "exchange-damage");
        Assert.Contains(
            history.LoadHistory(session.SessionId),
            entry => entry.Context?.Snapshot?.CombatStateChange is { IsUndo: true } stateChange &&
                     stateChange.RelatedEntryId == damageResult.Snapshot.EntryId);
    }

    [Fact]
    public async Task Final_hit_resolves_zone_damage_and_wounds_without_a_follow_up_request()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var profile = await factory.Services.GetRequiredService<HeroCombatProfileReader>().ReadAsync(hero.Id, "owner");
        var set = Assert.Single(profile!.Sets, item => item.ArmorModel == CombatArmorModel.Zone);
        var weapon = Assert.Single(set.Weapons, item => item.Name == "Schwert");

        var initial = await state.GetAsync(session.SessionId, "owner");
        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var attacker = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, item => item.ParticipantId == attacker.Id);
        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Übungsgegner",
            InitiativeBase = 1,
            Initiative = 1,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, 7, 2, 20, 1)
            {
                Armor = new CombatEnemyArmorDto
                {
                    UsesZonalArmor = true,
                    Head = 2
                }
            }
        }, "owner");
        var target = Assert.Single(added.Snapshot.Participants, item => item.Kind == CombatParticipantKind.Opponent);
        var noReaction = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = added.Snapshot.Revision,
            Kind = CombatSessionMutationKind.ConsumeReaction,
            ParticipantId = target.Id
        }, "owner");
        var declaration = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = noReaction.Snapshot.Revision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExchangeId = "exchange-automatic-hit",
            SetId = set.Id,
            WeaponId = weapon.Id,
            ActionKind = CombatActionKind.MeleeAttack
        }, "owner");

        var attackRequest = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = declaration.Snapshot.ActiveExchange!.ActionId,
            ExpectedRevision = declaration.Snapshot.Revision,
            HeroId = hero.Id,
            ExchangeId = "exchange-automatic-hit",
            SetId = set.Id,
            WeaponId = weapon.Id,
            ResolvedZone = new CombatZoneSnapshotDto(
                20,
                CombatArmorZone.Head,
                CombatWoundZone.Head,
                CombatFacing.Front,
                2),
            Action = CombatActionKind.MeleeAttack
        };
        var attackResult = CreateCombatResult(attackRequest, 8, 14, CombatOutcome.Success);
        var hit = await state.BindAttackRollAsync(attackRequest, attackResult, "owner");
        Assert.Equal(CombatExchangeStatus.Hit, hit.ActiveExchange!.Status);

        var automatic = await state.ResolveAutomaticHitAsync(
            attackRequest,
            attackResult with { CombatSessionSnapshot = hit },
            "owner");

        Assert.NotNull(automatic);
        Assert.Equal(CombatExchangeStatus.Completed, automatic!.Snapshot.ActiveExchange!.Status);
        Assert.NotNull(automatic.Snapshot.ActiveExchange.Zone);
        Assert.NotNull(automatic.Snapshot.ActiveExchange.Damage);
        Assert.NotNull(automatic.WoundApplication);
        Assert.Equal(CombatWoundZone.Head, automatic.WoundApplication.Zone);
        Assert.NotNull(automatic.WoundApplication.InitiativeLoss);
        Assert.Equal(2, automatic.WoundApplication.InitiativeLossRolls.Length);
        Assert.InRange(automatic.WoundApplication.InitiativeLoss!.Value, 2, 12);
        Assert.Contains(automatic.RuleNotes, note => note.Contains("INI-Verlust", StringComparison.Ordinal));
        var targetAfterDamage = Assert.Single(automatic.Snapshot.Participants, item => item.Id == target.Id);
        Assert.Equal(target.CurrentInitiative - automatic.WoundApplication.InitiativeLoss,
            targetAfterDamage.CurrentInitiative);
        Assert.Equal(automatic.WoundApplication.InitiativeLoss,
            targetAfterDamage.RecoverableInitiativeLoss);
        Assert.Equal(0, Assert.Single(automatic.Snapshot.Participants, item => item.Id == attacker.Id)
            .ActionBudget?.NormalActionsRemaining);
        Assert.Equal(3, automatic.Rolls.Length);

        var stored = await state.GetAsync(session.SessionId, "owner");
        Assert.Equal(automatic.Snapshot.Revision, stored.Revision);
        Assert.Equal(automatic.Snapshot.ActiveExchange.Status, stored.ActiveExchange!.Status);
    }

    [Fact]
    public async Task Confirmed_fumble_is_completed_once_and_keeps_its_table_decision()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var profile = await factory.Services.GetRequiredService<HeroCombatProfileReader>().ReadAsync(hero.Id, "owner");
        var set = Assert.Single(profile!.Sets, item => item.ArmorModel == CombatArmorModel.Zone);
        var weapon = Assert.Single(set.Weapons, item => item.Name == "Schwert");

        var initial = await state.GetAsync(session.SessionId, "owner");
        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var attacker = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, item => item.ParticipantId == attacker.Id);
        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Patzerziel",
            InitiativeBase = 1,
            Initiative = 1,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, 7, 0, 20)
        }, "owner");
        var target = Assert.Single(added.Snapshot.Participants, item => item.Kind == CombatParticipantKind.Opponent);
        var declaration = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = added.Snapshot.Revision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExchangeId = "exchange-fumble",
            SetId = set.Id,
            WeaponId = weapon.Id,
            ActionKind = CombatActionKind.MeleeAttack
        }, "owner");

        var request = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExchangeId = declaration.Snapshot.ActiveExchange!.ExchangeId,
            HeroId = hero.Id,
            SetId = set.Id,
            WeaponId = weapon.Id,
            Action = CombatActionKind.MeleeAttack
        };
        var followUps = CombatRollRules.Evaluate(
            CombatActionKind.MeleeAttack,
            14,
            [],
            20,
            controlRoll: 20).FollowUps;
        var fumble = CreateCombatResult(request, 20, 14, CombatOutcome.Fumble, followUps);

        var completed = await state.BindAttackRollAsync(request, fumble, "owner");

        Assert.Equal(CombatExchangeStatus.Completed, completed.ActiveExchange!.Status);
        Assert.Equal(CombatOutcome.Fumble, completed.ActiveExchange.AttackResult!.Outcome);
        Assert.Equal(followUps, completed.ActiveExchange.AttackResult.FollowUps);
        Assert.Null(completed.ActiveExchange.Damage);
        Assert.Null(completed.ActiveExchange.DefenseResult);
        Assert.Equal(0, Assert.Single(completed.Participants, item => item.Id == attacker.Id)
            .ActionBudget?.NormalActionsRemaining);
        Assert.Equal(CombatActionEntryState.Completed,
            completed.Actions.Single(item => item.Id == action.Id).State);
    }

    [Fact]
    public async Task Missing_automatic_hit_data_is_visible_and_does_not_reroll_or_advance_repeatedly()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();

        var attackerResult = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = 0,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Angreifer ohne TP",
            Initiative = 20,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, 7, 0, 20)
        }, "owner");
        var attacker = Assert.Single(attackerResult.Snapshot.Participants,
            participant => participant.Kind == CombatParticipantKind.Opponent);
        var targetResult = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = attackerResult.Snapshot.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Ziel ohne Reaktion",
            Initiative = 10,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, 7, 0, 20)
        }, "owner");
        var target = Assert.Single(targetResult.Snapshot.Participants,
            participant => participant.Id != attacker.Id && participant.Kind == CombatParticipantKind.Opponent);
        var targetWithoutReaction = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = targetResult.Snapshot.Revision,
            Kind = CombatSessionMutationKind.ConsumeReaction,
            ParticipantId = target.Id
        }, "owner");
        var action = Assert.Single(targetWithoutReaction.Snapshot.Actions,
            current => current.ParticipantId == attacker.Id && current.State == CombatActionEntryState.Open);
        var declaration = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = targetWithoutReaction.Snapshot.Revision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExchangeId = "exchange-missing-automatic-data",
            ActionKind = CombatActionKind.MeleeAttack,
            WeaponId = "unbekannte-waffe"
        }, "owner");
        var request = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExchangeId = declaration.Snapshot.ActiveExchange!.ExchangeId,
            Action = CombatActionKind.MeleeAttack,
            WeaponId = "unbekannte-waffe"
        };
        var attackResult = CreateCombatResult(request, 8, 10, CombatOutcome.Success);
        var hit = await state.BindAttackRollAsync(request, attackResult, "owner");
        Assert.Equal(CombatExchangeStatus.Hit, hit.ActiveExchange!.Status);

        Assert.Null(await state.ResolveAutomaticHitAsync(
            request,
            attackResult with { CombatSessionSnapshot = hit },
            "owner"));
        var pending = await state.GetAsync(session.SessionId, "owner");
        Assert.Equal(CombatExchangeStatus.Hit, pending.ActiveExchange!.Status);
        Assert.Contains("TP-Notation", pending.ActiveExchange.RuleNote, StringComparison.Ordinal);
        var pendingRevision = pending.Revision;

        Assert.Null(await state.ResolveAutomaticHitAsync(
            request,
            attackResult with { CombatSessionSnapshot = pending },
            "owner"));
        var repeated = await state.GetAsync(session.SessionId, "owner");
        Assert.Equal(pendingRevision, repeated.Revision);
        Assert.Equal(pending.ActiveExchange.RuleNote, repeated.ActiveExchange!.RuleNote);
    }

    [Fact]
    public async Task Held_action_moves_to_the_next_round_without_rerolling_initiative()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var initial = await state.GetAsync(session.SessionId, "owner");

        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var participant = Assert.Single(rolled.Snapshot.Participants, current => current.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, current => current.ParticipantId == participant.Id);

        await Assert.ThrowsAsync<RequestRejectedException>(() => state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.NewRound
        }, "owner"));

        var held = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.HoldAction,
            ActionId = action.Id,
            ParticipantId = participant.Id
        }, "owner");
        var corrected = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = held.Snapshot.Revision,
            Kind = CombatSessionMutationKind.SetInitiative,
            ParticipantId = participant.Id,
            Initiative = participant.CurrentInitiative
        }, "owner");
        Assert.Equal(action.Id, corrected.Snapshot.Participants.Single(current => current.Id == participant.Id)
            .ActionBudget?.HeldActionId);

        var nextRound = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = corrected.Snapshot.Revision,
            Kind = CombatSessionMutationKind.NewRound
        }, "owner");

        var nextParticipant = Assert.Single(nextRound.Snapshot.Participants, current => current.HeroId == hero.Id);
        Assert.Equal(rolled.Snapshot.Round + 1, nextRound.Snapshot.Round);
        Assert.Equal(participant.CurrentInitiative, nextParticipant.CurrentInitiative);
        Assert.Equal(participant.StartRoll, nextParticipant.StartRoll);
        Assert.Equal(action.Id, nextParticipant.ActionBudget?.HeldActionId);
        Assert.Equal(nextRound.Snapshot.Round, nextParticipant.ActionBudget?.HeldActionRound);
        Assert.Contains(nextRound.Snapshot.Actions, current => current.Id == action.Id &&
            current.State == CombatActionEntryState.Held && current.Round == nextRound.Snapshot.Round);
        Assert.DoesNotContain(nextRound.Snapshot.Actions, current => current.ParticipantId == participant.Id &&
            current.State == CombatActionEntryState.Open && current.Round == nextRound.Snapshot.Round);
    }

    [Fact]
    public async Task Incapacitated_participant_keeps_no_action_or_reaction_in_next_round()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var initial = await state.GetAsync(session.SessionId, "owner");

        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var participant = Assert.Single(rolled.Snapshot.Participants, current => current.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, current => current.ParticipantId == participant.Id);

        var held = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.HoldAction,
            ActionId = action.Id,
            ParticipantId = participant.Id
        }, "owner");

        var synced = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = held.Snapshot.Revision,
            Kind = CombatSessionMutationKind.SyncRuntimeState,
            HeroId = hero.Id,
            RuntimeState = CreateRuntimeState(28) with { CurrentLeP = 0 }
        }, "owner");

        var afterSync = Assert.Single(synced.Snapshot.Participants, current => current.Id == participant.Id);
        Assert.False(afterSync.ActionAvailable);
        Assert.False(afterSync.ReactionAvailable);
        Assert.Null(afterSync.ActionBudget?.HeldActionId);
        Assert.Equal(CombatActionEntryState.Completed,
            synced.Snapshot.Actions.Single(current => current.Id == action.Id).State);

        var nextRound = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = synced.Snapshot.Revision,
            Kind = CombatSessionMutationKind.NewRound
        }, "owner");

        var nextParticipant = Assert.Single(nextRound.Snapshot.Participants, current => current.Id == participant.Id);
        Assert.Equal(0, nextParticipant.ActionBudget?.NormalActionsRemaining);
        Assert.Equal(0, nextParticipant.ActionBudget?.ReactionsRemaining);
        Assert.False(nextParticipant.ActionAvailable);
        Assert.False(nextParticipant.ReactionAvailable);
        Assert.DoesNotContain(nextRound.Snapshot.Actions, action =>
            action.ParticipantId == participant.Id && action.Round == nextRound.Snapshot.Round);
    }

    [Fact]
    public async Task Undo_of_a_non_roll_state_change_is_kept_in_history_and_correlated()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var history = factory.Services.GetRequiredService<SessionRecordStore>();
        var initial = await state.GetAsync(session.SessionId, "owner");

        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var participant = Assert.Single(rolled.Snapshot.Participants, current => current.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, current => current.ParticipantId == participant.Id);
        var holdRequestId = Guid.NewGuid();

        var held = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = holdRequestId,
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.HoldAction,
            ActionId = action.Id,
            ParticipantId = participant.Id
        }, "owner");
        Assert.Equal(holdRequestId, held.HistoryEntry?.Context?.Snapshot?.CombatStateChange?.Id);

        var undone = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = held.Snapshot.Revision,
            Kind = CombatSessionMutationKind.Undo
        }, "owner");

        Assert.True(undone.Applied);
        Assert.Contains(history.LoadHistory(session.SessionId), entry =>
            entry.Context?.Snapshot?.CombatStateChange is { IsUndo: true } stateChange &&
            stateChange.RelatedEntryId == holdRequestId);
    }

    [Fact]
    public async Task New_round_rejects_an_open_attack_exchange_instead_of_dropping_it()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var profile = await factory.Services.GetRequiredService<HeroCombatProfileReader>().ReadAsync(hero.Id, "owner");
        var set = Assert.Single(profile!.Sets, item => item.ArmorModel == CombatArmorModel.Zone);
        var weapon = Assert.Single(set.Weapons, item => item.Name == "Schwert");

        var initial = await state.GetAsync(session.SessionId, "owner");
        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var attacker = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, item => item.ParticipantId == attacker.Id);
        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Offenes Ziel",
            InitiativeBase = 1,
            Initiative = 1,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, 7, 2, 20)
        }, "owner");
        var target = Assert.Single(added.Snapshot.Participants, item => item.Kind == CombatParticipantKind.Opponent);
        var declared = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = added.Snapshot.Revision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExchangeId = "exchange-open-round",
            SetId = set.Id,
            WeaponId = weapon.Id,
            ActionKind = CombatActionKind.MeleeAttack
        }, "owner");

        await Assert.ThrowsAsync<RequestRejectedException>(() => state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = declared.Snapshot.Revision,
            Kind = CombatSessionMutationKind.CompleteAction,
            ParticipantId = attacker.Id,
            ActionId = action.Id
        }, "owner"));

        await Assert.ThrowsAsync<RequestRejectedException>(() => state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = declared.Snapshot.Revision,
            Kind = CombatSessionMutationKind.NewRound
        }, "owner"));

        var preserved = await state.GetAsync(session.SessionId, "owner");
        Assert.Equal(CombatExchangeStatus.Declared, preserved.ActiveExchange!.Status);
        Assert.Equal(declared.Snapshot.Revision, preserved.Revision);
    }

    [Fact]
    public async Task Held_action_can_be_rolled_directly_and_consumes_the_reserve_once()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var handler = factory.Services.GetRequiredService<RollCombatHandler>();
        var profile = await factory.Services.GetRequiredService<HeroCombatProfileReader>().ReadAsync(hero.Id, "owner");
        var set = Assert.Single(profile!.Sets, item => item.ArmorModel == CombatArmorModel.Zone);
        var weapon = Assert.Single(set.Weapons, item => item.Name == "Schwert");

        var initial = await state.GetAsync(session.SessionId, "owner");
        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var attacker = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, item => item.ParticipantId == attacker.Id);

        var held = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.HoldAction,
            ActionId = action.Id,
            ParticipantId = attacker.Id
        }, "owner");
        Assert.Equal(action.Id, held.Snapshot.Participants.Single(item => item.Id == attacker.Id)
            .ActionBudget?.HeldActionId);

        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = held.Snapshot.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = "Reserveziel",
            InitiativeBase = 1,
            Initiative = 1,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, null, 2, 20)
        }, "owner");
        var target = Assert.Single(added.Snapshot.Participants, item => item.Kind == CombatParticipantKind.Opponent);

        var declaration = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = added.Snapshot.Revision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExchangeId = "exchange-held-direct",
            SetId = set.Id,
            WeaponId = weapon.Id,
            ActionKind = CombatActionKind.MeleeAttack
        }, "owner");

        var request = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ExpectedRevision = declaration.Snapshot.Revision,
            HeroId = hero.Id,
            ExchangeId = "exchange-held-direct",
            SetId = set.Id,
            WeaponId = weapon.Id,
            WeaponName = weapon.Name,
            Action = CombatActionKind.MeleeAttack
        };
        var result = await handler.HandleAsync(request, "owner", "Besitzer");

        Assert.NotNull(result.CombatSessionSnapshot);
        var stored = await state.GetAsync(session.SessionId, "owner");
        Assert.Equal(0, stored.Participants.Single(item => item.Id == attacker.Id)
            .ActionBudget?.NormalActionsRemaining);
        Assert.Null(stored.Participants.Single(item => item.Id == attacker.Id)
            .ActionBudget?.HeldActionId);
        Assert.Equal(CombatActionEntryState.Completed,
            stored.Actions.Single(item => item.Id == action.Id).State);

        var retry = await handler.HandleAsync(request, "owner", "Besitzer");
        Assert.Equal(result.Snapshot.EntryId, retry.Snapshot.EntryId);
        Assert.Equal(result.Snapshot.LabeledRolls.Select(roll => roll.Value),
            retry.Snapshot.LabeledRolls.Select(roll => roll.Value));
    }

    [Fact]
    public async Task Normal_action_and_reaction_budgets_are_consumed_independently()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var initial = await state.GetAsync(session.SessionId, "owner");

        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id
        }, "owner");
        var participant = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        var action = Assert.Single(rolled.Snapshot.Actions, item => item.ParticipantId == participant.Id);

        var completed = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.CompleteAction,
            ActionId = action.Id,
            ParticipantId = participant.Id
        }, "owner");
        var afterAction = Assert.Single(completed.Snapshot.Participants, item => item.Id == participant.Id);
        Assert.Equal(0, afterAction.ActionBudget?.NormalActionsRemaining);
        Assert.True(afterAction.ReactionAvailable);
        await Assert.ThrowsAsync<RequestRejectedException>(() => state.EnsureRollAvailabilityAsync(
            new CombatRollRequestDto
            {
                SessionId = session.SessionId,
                HeroId = hero.Id,
                Action = CombatActionKind.MeleeAttack
            },
            "owner"));

        var reaction = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = completed.Snapshot.Revision,
            Kind = CombatSessionMutationKind.ConsumeReaction,
            ParticipantId = participant.Id
        }, "owner");
        var afterReaction = Assert.Single(reaction.Snapshot.Participants, item => item.Id == participant.Id);
        Assert.Equal(0, afterReaction.ActionBudget?.ReactionsRemaining);
        Assert.False(afterReaction.ActionAvailable);
        Assert.False(afterReaction.ReactionAvailable);
        await Assert.ThrowsAsync<RequestRejectedException>(() => state.EnsureRollAvailabilityAsync(
            new CombatRollRequestDto
            {
                SessionId = session.SessionId,
                HeroId = hero.Id,
                Action = CombatActionKind.Dodge
            },
            "owner"));

        await Assert.ThrowsAsync<RequestRejectedException>(() => state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = reaction.Snapshot.Revision,
            Kind = CombatSessionMutationKind.ConsumeReaction,
            ParticipantId = participant.Id
        }, "owner"));
    }

    [Fact]
    public async Task Initiative_roll_applies_runtime_wounds_and_low_aup_before_the_first_round()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var initial = await state.GetAsync(session.SessionId, "owner");
        var wounds = Enum.GetValues<CombatWoundZone>()
            .ToDictionary(zone => zone, _ => (int?)0);
        wounds[CombatWoundZone.LeftLeg] = 1;

        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id,
            RuntimeState = new CombatRuntimeStateDto
            {
                IsStarted = false,
                CurrentLeP = 22,
                CurrentAuP = 8,
                Wounds = wounds
            }
        }, "owner");

        var participant = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        Assert.Equal(-3, participant.InitiativeRuntimeModifier);
        Assert.Equal(participant.InitiativeBase + participant.StartRoll - 3, participant.CurrentInitiative);
        Assert.Contains(participant.InitiativeRuntimeNotes, note => note.Contains("linkes Bein", StringComparison.Ordinal));
        Assert.Contains(participant.InitiativeRuntimeNotes, note => note.Contains("AuP 8/28", StringComparison.Ordinal));

        var recovered = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.SyncRuntimeState,
            HeroId = hero.Id,
            RuntimeState = new CombatRuntimeStateDto
            {
                IsStarted = true,
                CurrentLeP = 22,
                CurrentAuP = 28,
                Wounds = Enum.GetValues<CombatWoundZone>()
                    .ToDictionary(zone => zone, _ => (int?)0)
            }
        }, "owner");

        var recoveredParticipant = Assert.Single(recovered.Snapshot.Participants, item => item.HeroId == hero.Id);
        Assert.Equal(0, recoveredParticipant.InitiativeRuntimeModifier);
        Assert.Equal(participant.CurrentInitiative + 3, recoveredParticipant.CurrentInitiative);
        Assert.Equal(28, recoveredParticipant.RuntimeState?.CurrentAuP);
    }

    [Fact]
    public async Task Selected_set_is_stored_and_changes_update_running_initiative_base()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var profileReader = factory.Services.GetRequiredService<HeroCombatProfileReader>();
        var profile = await profileReader.ReadAsync(hero.Id, "owner");
        Assert.NotNull(profile);
        var simpleSet = Assert.Single(profile.Sets, set => set.ArmorModel == CombatArmorModel.Simple);
        var zonalSet = Assert.Single(profile.Sets, set => set.ArmorModel == CombatArmorModel.Zone);
        var simpleInitiative = Assert.IsType<int>(simpleSet.Initiative);
        var zonalInitiative = Assert.IsType<int>(zonalSet.Initiative);
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var initial = await state.GetAsync(session.SessionId, "owner");
        var runtime = new CombatRuntimeStateDto
        {
            IsStarted = true,
            CurrentLeP = 22,
            CurrentAuP = 28,
            Wounds = Enum.GetValues<CombatWoundZone>().ToDictionary(zone => zone, _ => (int?)0)
        };

        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id,
            SetId = simpleSet.Id,
            RuntimeState = runtime
        }, "owner");
        var rolledParticipant = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);
        Assert.Equal(simpleSet.Id, rolledParticipant.InitiativeSetId);
        Assert.Equal(simpleInitiative, rolledParticipant.InitiativeBase);

        var switched = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.SyncRuntimeState,
            HeroId = hero.Id,
            SetId = zonalSet.Id,
            RuntimeState = runtime
        }, "owner");
        var switchedParticipant = Assert.Single(switched.Snapshot.Participants, item => item.HeroId == hero.Id);
        Assert.Equal(zonalSet.Id, switchedParticipant.InitiativeSetId);
        Assert.Equal(zonalInitiative, switchedParticipant.InitiativeBase);
        Assert.Equal(rolledParticipant.CurrentInitiative + zonalInitiative - simpleInitiative,
            switchedParticipant.CurrentInitiative);
    }

    [Fact]
    public async Task Completing_attention_orientation_uses_the_latest_runtime_modifier()
    {
        using var factory = new TestApplicationFactory();
        var hero = await SeedHeroAsync(factory, "owner");
        var session = CreateSession(factory, hero, "owner");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var initial = await state.GetAsync(session.SessionId, "owner");
        var startState = CreateRuntimeState(28);

        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = hero.Id,
            RuntimeState = startState
        }, "owner");
        var participant = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == hero.Id);

        var oriented = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.Orient,
            ParticipantId = participant.Id,
            RuntimeState = startState,
            OrientationUninterrupted = true
        }, "owner");
        var orientation = Assert.Single(oriented.Snapshot.Actions, action =>
            action.ParticipantId == participant.Id && action.Label == "Orientieren");

        var completed = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = oriented.Snapshot.Revision,
            Kind = CombatSessionMutationKind.CompleteAction,
            ActionId = orientation.Id,
            ParticipantId = participant.Id,
            RuntimeState = CreateRuntimeState(8)
        }, "owner");

        var completedParticipant = Assert.Single(completed.Snapshot.Participants, item => item.HeroId == hero.Id);
        Assert.Equal(CombatActionEntryState.Completed,
            Assert.Single(completed.Snapshot.Actions, action => action.Id == orientation.Id).State);
        Assert.Equal(-1, completedParticipant.InitiativeRuntimeModifier);
        Assert.Equal(8, completedParticipant.RuntimeState?.CurrentAuP);
        Assert.Equal(completedParticipant.InitiativeBase + completedParticipant.InitiativeDiceCount * 6 - 1 +
                     completedParticipant.InitiativeCorrection,
            completedParticipant.CurrentInitiative);
    }

    [Fact]
    public async Task Undo_is_rejected_after_a_foreign_session_mutation()
    {
        using var factory = new TestApplicationFactory();
        var ownerHero = await SeedHeroAsync(factory, "owner");
        var otherHero = await SeedHeroAsync(factory, "other");
        var session = CreateSession(factory, ownerHero, "owner");
        var sessions = factory.Services.GetRequiredService<SessionService>();
        sessions.AddPlayer(session.SessionId, new PlayerInfo { UserId = "other", Name = "Andere" });
        sessions.UpdatePlayerHero(session.SessionId, "other", otherHero.Id, otherHero.Name);
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();

        var initial = await state.GetAsync(session.SessionId, "owner");
        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            HeroId = ownerHero.Id
        }, "owner");
        var otherParticipant = Assert.Single(rolled.Snapshot.Participants, item => item.HeroId == otherHero.Id);
        var foreign = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = rolled.Snapshot.Revision,
            Kind = CombatSessionMutationKind.SetAnnouncement,
            ParticipantId = otherParticipant.Id,
            Announcement = "Andere Ansage"
        }, "other");

        await Assert.ThrowsAsync<RequestRejectedException>(() => state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = foreign.Snapshot.Revision,
            Kind = CombatSessionMutationKind.Undo
        }, "owner"));

        var current = await state.GetAsync(session.SessionId, "owner");
        Assert.Equal(foreign.Snapshot.Revision, current.Revision);
        Assert.Equal("Andere Ansage", Assert.Single(current.Participants, item => item.HeroId == otherHero.Id).Announcement);
    }

    private static CombatRollResultDto CreateCombatResult(
        CombatRollRequestDto request,
        int mainRoll,
        int target,
        CombatOutcome outcome,
        CombatFollowUpRequirementDto[]? followUps = null)
    {
        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            ExchangeId = request.ExchangeId,
            HeroId = request.HeroId,
            Action = request.Action,
            BaseValue = target,
            EffectiveTarget = target,
            Outcome = outcome,
            StatusLabel = outcome == CombatOutcome.Success ? "Gelungen" : "Misslungen",
            LabeledRolls = [new CombatLabeledRollDto("Hauptwurf", 20, mainRoll)],
            FollowUps = followUps ?? []
        };
        var roll = new DiceRollDto(20, mainRoll);
        var historyOutcome = outcome == CombatOutcome.Success
            ? RollHistoryOutcome.Success
            : RollHistoryOutcome.Failure;
        var historyEntry = new RollHistoryEntryDto(
            "Besitzer",
            DateTime.UtcNow,
            [roll],
            0,
            mainRoll,
            new RollHistoryContextDto(
                RollHistoryKind.Combat,
                snapshot.ActionLabel ?? "Kampfwurf",
                historyOutcome,
                null,
                [],
                new RollHistorySnapshotDto { Combat = snapshot }));
        return new CombatRollResultDto(
            snapshot.EntryId,
            request.RequestId,
            request.SessionId,
            "owner",
            "Besitzer",
            null,
            outcome,
            snapshot,
            [roll],
            historyEntry);
    }

    private static CombatRollResultDto CreateDamageResult(
        CombatRollRequestDto request,
        int rawDamage,
        CombatZoneSnapshotDto zone)
    {
        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            ExchangeId = request.ExchangeId,
            HeroId = request.HeroId,
            Action = request.Action,
            Outcome = CombatOutcome.Neutral,
            StatusLabel = "Trefferpunkte",
            LabeledRolls = [new CombatLabeledRollDto("TP-Würfel", 20, rawDamage)],
            Damage = new CombatDamageSnapshotDto(rawDamage, 0, 0, 1, 0, rawDamage, false),
            Zone = zone
        };
        var roll = new DiceRollDto(20, rawDamage);
        var historyEntry = new RollHistoryEntryDto(
            "Besitzer",
            DateTime.UtcNow,
            [roll],
            0,
            rawDamage,
            new RollHistoryContextDto(
                RollHistoryKind.Combat,
                snapshot.StatusLabel,
                RollHistoryOutcome.None,
                null,
                [],
                new RollHistorySnapshotDto { Combat = snapshot }));
        return new CombatRollResultDto(
            snapshot.EntryId,
            request.RequestId,
            request.SessionId,
            "owner",
            "Besitzer",
            null,
            CombatOutcome.Neutral,
            snapshot,
            [roll],
            historyEntry);
    }

    private static CombatRollResultDto CreateZoneResult(
        CombatRollRequestDto request,
        CombatZoneSnapshotDto zone)
    {
        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            ExchangeId = request.ExchangeId,
            HeroId = request.HeroId,
            Action = request.Action,
            Outcome = CombatOutcome.Neutral,
            StatusLabel = "Trefferzone gewürfelt",
            LabeledRolls = [new CombatLabeledRollDto("Trefferzone", 20, zone.W20 ?? 1)],
            Zone = zone
        };
        var roll = new DiceRollDto(20, zone.W20 ?? 1);
        var historyEntry = new RollHistoryEntryDto(
            "Besitzer",
            DateTime.UtcNow,
            [roll],
            0,
            roll.Value,
            new RollHistoryContextDto(
                RollHistoryKind.Combat,
                snapshot.StatusLabel,
                RollHistoryOutcome.None,
                null,
                [],
                new RollHistorySnapshotDto { Combat = snapshot }));
        return new CombatRollResultDto(
            snapshot.EntryId,
            request.RequestId,
            request.SessionId,
            "owner",
            "Besitzer",
            null,
            CombatOutcome.Neutral,
            snapshot,
            [roll],
            historyEntry);
    }

    private static CombatRuntimeStateDto CreateRuntimeState(int currentAuP) => new()
    {
        IsStarted = true,
        CurrentLeP = 22,
        CurrentAuP = currentAuP,
        Wounds = Enum.GetValues<CombatWoundZone>().ToDictionary(zone => zone, _ => (int?)0)
    };

    private static GameSession CreateSession(TestApplicationFactory factory, Hero hero, string owner)
    {
        var sessions = factory.Services.GetRequiredService<SessionService>();
        var session = sessions.CreateSession(owner, "Besitzer", "Kampftest");
        sessions.UpdatePlayerHero(session.SessionId, owner, hero.Id, hero.Name);
        return session;
    }

    private static async Task<Hero> SeedHeroAsync(TestApplicationFactory factory, string owner)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
        var hero = new Hero
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner,
            IsActive = true,
            Name = "Kampftestheld",
            ImportVersion = 3,
            SourceXml = Encoding.UTF8.GetBytes("""
                <daten>
                  <angaben><name>Kampftestheld</name><wundschwelle>4</wundschwelle></angaben>
                  <eigenschaften><intuition><akt>14</akt></intuition><lebensenergie><akt>22</akt></lebensenergie><ausdauer><akt>28</akt></ausdauer></eigenschaften>
                  <sonderfertigkeiten><sonderfertigkeit><name>Aufmerksamkeit</name><bezeichner>Aufmerksamkeit</bezeichner><bereich>Kampf</bereich></sonderfertigkeit></sonderfertigkeiten>
                  <kampfsets><kampfset nr="1" tzm="true" inbenutzung="true"><ini>11</ini><ausweichen>13</ausweichen><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein></ruestungzonen><nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><möglich>true</möglich><name>Schwert</name><at>14</at><pa>10</pa><tp>1W+4</tp></nahkampfwaffe><nahkampfwaffe><nummer>2</nummer><möglich>false</möglich><name>Unbereites Schwert</name><at>14</at><pa>10</pa><tp>1W+4</tp></nahkampfwaffe></nahkampfwaffen></kampfset><kampfset nr="2" tzm="false" inbenutzung="false"><ini>8</ini><ruestungeinfach><gesamt>0</gesamt><behinderung>0</behinderung></ruestungeinfach></kampfset></kampfsets>
                </daten>
                """)
        };
        db.Heroes.Add(hero);
        await db.SaveChangesAsync();
        return hero;
    }
}
