using System.Text;

using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Services;
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
        var nextRound = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = held.Snapshot.Revision,
            Kind = CombatSessionMutationKind.NewRound
        }, "owner");

        var nextParticipant = Assert.Single(nextRound.Snapshot.Participants, current => current.HeroId == hero.Id);
        Assert.Equal(rolled.Snapshot.Round + 1, nextRound.Snapshot.Round);
        Assert.Equal(participant.CurrentInitiative, nextParticipant.CurrentInitiative);
        Assert.Equal(participant.StartRoll, nextParticipant.StartRoll);
        Assert.Contains(nextRound.Snapshot.Actions, current => current.Id == action.Id &&
            current.State == CombatActionEntryState.Held && current.Round == nextRound.Snapshot.Round);
        Assert.Contains(nextRound.Snapshot.Actions, current => current.ParticipantId == participant.Id &&
            current.State == CombatActionEntryState.Open && current.Round == nextRound.Snapshot.Round);
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
                  <kampfsets><kampfset nr="1" tzm="true" inbenutzung="true"><ini>11</ini><ausweichen>13</ausweichen><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein></ruestungzonen></kampfset></kampfsets>
                </daten>
                """)
        };
        db.Heroes.Add(hero);
        await db.SaveChangesAsync();
        return hero;
    }
}
