using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Tests;

public sealed class CombatActionPreparationTests
{
    [Fact]
    public void Attack_preparation_keeps_snapshot_participants_and_explicit_runtime_state()
    {
        var attacker = new CombatSessionParticipantDto
        {
            Id = "hero",
            Name = "Darian",
            Kind = CombatParticipantKind.Hero,
            HeroId = Guid.NewGuid(),
            RuntimeState = new CombatRuntimeStateDto { IsStarted = true, CurrentLeP = 22 }
        };
        var target = new CombatSessionParticipantDto
        {
            Id = "goblin",
            Name = "Goblin",
            Kind = CombatParticipantKind.Opponent,
            OpponentProfile = new CombatOpponentProfileDto(10, 8, 7, 1, 12)
        };
        var snapshot = new CombatSessionSnapshotDto
        {
            SessionId = "session",
            Revision = 7,
            Participants = [attacker, target]
        };
        var action = new CombatSessionActionDto
        {
            Id = "attack-action",
            ParticipantId = attacker.Id,
            ActionKind = CombatActionKind.MeleeAttack
        };
        var runtime = new CombatRuntimeStateDto { IsStarted = true, CurrentLeP = 21 };

        var result = CombatActionPreparation.PrepareAttack(
            snapshot,
            attacker,
            target,
            action,
            CombatActionKind.MeleeAttack,
            "set-1",
            "sword",
            "Schwert",
            2,
            CombatFacing.Front,
            runtime,
            null,
            "  gezielter Hieb  ");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Request);
        Assert.Equal(snapshot.Revision, result.Request.ExpectedRevision);
        Assert.Equal(attacker.Id, result.Request.ParticipantId);
        Assert.Equal(target.Id, result.Request.TargetParticipantId);
        Assert.Equal(runtime, result.Request.RuntimeState);
        Assert.Equal("gezielter Hieb", result.Request.Note);
        Assert.Equal(2, Assert.Single(result.Request.Modifiers).Value);
    }

    [Fact]
    public void Defense_preparation_rejects_a_defender_that_is_not_the_exchange_target()
    {
        var snapshot = new CombatSessionSnapshotDto { SessionId = "session", Revision = 3 };
        var exchange = new CombatAttackExchangeDto
        {
            ExchangeId = "exchange",
            SessionId = snapshot.SessionId,
            AttackerParticipantId = "attacker",
            TargetParticipantId = "target",
            AllowedDefenseActions = [CombatActionKind.Dodge]
        };
        var wrongDefender = new CombatSessionParticipantDto
        {
            Id = "other",
            Kind = CombatParticipantKind.Hero,
            HeroId = Guid.NewGuid()
        };

        var result = CombatActionPreparation.PrepareDefense(
            snapshot,
            exchange,
            wrongDefender,
            CombatActionKind.Dodge,
            "set-1",
            null,
            null,
            0,
            null,
            null);

        Assert.False(result.IsSuccess);
        Assert.Contains("Abwehrer", result.Error);
    }
}
