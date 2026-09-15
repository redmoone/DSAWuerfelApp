using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Tests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace DsaWuerfelApp.Tests;

public sealed class CombatEnemySessionTests
{
    [Fact]
    public async Task Adds_catalog_enemy_without_hero_id_and_rolls_catalog_initiative()
    {
        using var factory = new TestApplicationFactory();
        var session = factory.Services.GetRequiredService<SessionService>()
            .CreateSession("owner", "Meister", "Katalogtest");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();

        var initial = await state.GetAsync(session.SessionId, "owner");
        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            EnemySelection = new CombatEnemySelectionDto { EnemyId = "wolf-gemeine-werte" }
        }, "owner");

        var opponent = Assert.Single(added.Snapshot.Participants,
            participant => participant.Kind == CombatParticipantKind.Opponent);
        Assert.True(added.Applied);
        Assert.Null(opponent.HeroId);
        Assert.Equal("wolf-gemeine-werte", opponent.OpponentProfile!.CatalogProfile!.CatalogEnemyId);
        Assert.True(opponent.OpponentProfile.CatalogProfile.CombatReady);
        Assert.Equal(23, opponent.OpponentProfile.LeP);
        Assert.Equal(100, opponent.RuntimeState!.CurrentAuP);
        Assert.Null(opponent.CurrentInitiative);
        Assert.False(added.Snapshot.IsStarted);
        Assert.Empty(added.Snapshot.Actions);

        var reloaded = await state.GetAsync(session.SessionId, "owner");
        var persistedOpponent = Assert.Single(reloaded.Participants,
            participant => participant.Id == opponent.Id);
        Assert.Equal(opponent.OpponentProfile.CatalogProfile!.CatalogEnemyId,
            persistedOpponent.OpponentProfile!.CatalogProfile!.CatalogEnemyId);
        Assert.Equal(opponent.OpponentProfile.LeP, persistedOpponent.OpponentProfile.LeP);
        Assert.Equal(opponent.OpponentProfile.ArmorRating, persistedOpponent.OpponentProfile.ArmorRating);
        Assert.Null(persistedOpponent.HeroId);

        var rolled = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = reloaded.Revision,
            Kind = CombatSessionMutationKind.RollInitiative,
            ParticipantId = opponent.Id
        }, "owner");

        var rolledOpponent = Assert.Single(rolled.Snapshot.Participants,
            participant => participant.Id == opponent.Id);
        Assert.InRange(rolledOpponent.CurrentInitiative!.Value, 10, 15);
        Assert.Equal(9, rolledOpponent.InitiativeBase);
        Assert.Equal(1, rolledOpponent.InitiativeDiceCount);
        Assert.Single(rolled.Snapshot.Actions, action => action.ParticipantId == opponent.Id);
    }

    [Fact]
    public async Task Keeps_source_dependent_and_equipment_dependent_enemies_unready_in_session()
    {
        using var factory = new TestApplicationFactory();
        var session = factory.Services.GetRequiredService<SessionService>()
            .CreateSession("owner", "Meister", "Katalogtest");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();

        var initial = await state.GetAsync(session.SessionId, "owner");
        var fox = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            EnemySelection = new CombatEnemySelectionDto { EnemyId = "rotfuchs" }
        }, "owner");
        var foxParticipant = Assert.Single(fox.Snapshot.Participants);
        Assert.Equal("sourceDependent", foxParticipant.OpponentProfile!.CatalogProfile!.Readiness);
        Assert.Null(foxParticipant.OpponentProfile.LeP);
        Assert.False(foxParticipant.OpponentProfile.HasBasicCombatValues);

        var goblin = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = fox.Snapshot.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            EnemySelection = new CombatEnemySelectionDto
            {
                EnemyId = "goblin",
                VariantId = "erfahren",
                WeaponOption = "Speer",
                ArmorOption = "Fellkleidung"
            }
        }, "owner");
        var goblinParticipant = Assert.Single(goblin.Snapshot.Participants,
            participant => participant.OpponentProfile?.CatalogProfile?.CatalogEnemyId == "goblin");
        Assert.Equal("requiresEquipmentValues", goblinParticipant.OpponentProfile!.CatalogProfile!.Readiness);
        Assert.Null(goblinParticipant.OpponentProfile.ArmorRating);
        Assert.False(goblinParticipant.OpponentProfile.HasBasicCombatValues);
    }

    [Fact]
    public async Task Removes_catalog_enemy_and_its_actions_but_not_heroes_or_other_participants()
    {
        using var factory = new TestApplicationFactory();
        var session = factory.Services.GetRequiredService<SessionService>()
            .CreateSession("owner", "Meister", "Katalogtest");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();

        var initial = await state.GetAsync(session.SessionId, "owner");
        var added = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = initial.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Initiative = 12,
            EnemySelection = new CombatEnemySelectionDto { EnemyId = "wolf-gemeine-werte" }
        }, "owner");
        var opponent = Assert.Single(added.Snapshot.Participants);
        Assert.Single(added.Snapshot.Actions, action => action.ParticipantId == opponent.Id);

        var removed = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = added.Snapshot.Revision,
            Kind = CombatSessionMutationKind.RemoveOpponent,
            ParticipantId = opponent.Id
        }, "owner");

        Assert.DoesNotContain(removed.Snapshot.Participants,
            participant => participant.Id == opponent.Id);
        Assert.DoesNotContain(removed.Snapshot.Actions,
            action => action.ParticipantId == opponent.Id);
        Assert.Empty(removed.Snapshot.Participants);
    }
}
