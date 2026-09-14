using System.Text.Json;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Tests;

public sealed class CombatAttackExchangeContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Attack_exchange_round_trips_through_a_session_snapshot()
    {
        var exchange = ValidExchange();
        var snapshot = new CombatSessionSnapshotDto
        {
            SessionId = "session-1",
            Revision = 7,
            ActiveExchange = exchange
        };

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var restored = JsonSerializer.Deserialize<CombatSessionSnapshotDto>(json, JsonOptions);

        Assert.NotNull(restored?.ActiveExchange);
        Assert.Equal(exchange.ExchangeId, restored!.ActiveExchange!.ExchangeId);
        Assert.Equal(exchange.TargetParticipantId, restored.ActiveExchange.TargetParticipantId);
        Assert.Equal(exchange.Status, restored.ActiveExchange.Status);
        Assert.Equal(exchange.AllowedDefenseActions, restored.ActiveExchange.AllowedDefenseActions);
        Assert.Equal(exchange.AttackResult, restored.ActiveExchange.AttackResult);
    }

    [Fact]
    public void Older_snapshots_without_an_exchange_remain_compatible()
    {
        const string json = """
            {"sessionId":"session-1","revision":3,"round":1,"participants":[],"actions":[]}
            """;

        var snapshot = JsonSerializer.Deserialize<CombatSessionSnapshotDto>(json, JsonOptions);

        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.ActiveExchange);
    }

    [Fact]
    public void Exchange_identity_rejects_empty_or_self_target_ids()
    {
        var valid = ValidExchange();

        Assert.True(CombatAttackExchangeRules.HasValidIdentity(valid));
        Assert.False(CombatAttackExchangeRules.HasValidIdentity(valid with { ExchangeId = string.Empty }));
        Assert.False(CombatAttackExchangeRules.HasValidIdentity(valid with { RequestId = Guid.Empty }));
        Assert.False(CombatAttackExchangeRules.HasValidIdentity(
            valid with { TargetParticipantId = valid.AttackerParticipantId }));
    }

    [Fact]
    public void Exchange_status_transitions_allow_only_the_defined_flow()
    {
        Assert.True(CombatAttackExchangeRules.CanTransition(
            CombatExchangeStatus.Declared,
            CombatExchangeStatus.AttackOpen));
        Assert.True(CombatAttackExchangeRules.CanTransition(
            CombatExchangeStatus.DefenseOpen,
            CombatExchangeStatus.Hit));
        Assert.True(CombatAttackExchangeRules.CanTransition(
            CombatExchangeStatus.Avoided,
            CombatExchangeStatus.Completed));

        Assert.False(CombatAttackExchangeRules.CanTransition(
            CombatExchangeStatus.Completed,
            CombatExchangeStatus.DefenseOpen));
        Assert.False(CombatAttackExchangeRules.CanTransition(
            CombatExchangeStatus.Declared,
            CombatExchangeStatus.DamageOpen));
    }

    private static CombatAttackExchangeDto ValidExchange() => new()
    {
        ExchangeId = "exchange-1",
        SessionId = "session-1",
        RequestId = Guid.NewGuid(),
        Revision = 7,
        AttackerParticipantId = "hero:attacker",
        TargetParticipantId = "opponent:target",
        Round = 2,
        PhaseInitiative = 17,
        SetId = "set-1",
        WeaponId = "weapon-1",
        WeaponName = "Schwert",
        AttackKind = CombatActionKind.MeleeAttack,
        Status = CombatExchangeStatus.DefenseOpen,
        ActionConsumed = true,
        AttackResult = new CombatRollEvaluationDto(
            true,
            null,
            CombatActionKind.MeleeAttack,
            12,
            12,
            null,
            8,
            null,
            CombatOutcome.Success,
            true,
            false,
            false,
            true,
            "Attacke gelungen"),
        AllowedDefenseActions = [CombatActionKind.WeaponParry, CombatActionKind.Dodge],
        HistoryEntryIds = [Guid.NewGuid()],
        RuleNote = "Abwehrentscheidung offen"
    };
}
