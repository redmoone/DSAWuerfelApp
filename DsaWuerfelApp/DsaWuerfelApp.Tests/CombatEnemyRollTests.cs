using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Tests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace DsaWuerfelApp.Tests;

public sealed class CombatEnemyRollTests
{
    [Fact]
    public async Task Catalog_enemy_attack_uses_existing_roll_and_history_contract()
    {
        using var factory = new TestApplicationFactory();
        var session = factory.Services.GetRequiredService<SessionService>()
            .CreateSession("owner", "Meister", "Katalogwürfe");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var handler = factory.Services.GetRequiredService<RollCombatHandler>();

        var attacker = await AddEnemyAsync(state, session.SessionId, "owner", "wolf-gemeine-werte", 20);
        var target = await AddEnemyAsync(state, session.SessionId, "owner", "ghul", 10,
            new CombatEnemySelectionDto { EnemyId = "ghul", AttackId = "gezielter-biss" });
        var attack = await DeclareAttackAsync(state, session.SessionId, "owner", attacker, target, "biss");

        var result = await handler.HandleAsync(new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ParticipantId = attacker.Id,
            ExchangeId = attack.ExchangeId,
            Action = CombatActionKind.MeleeAttack,
            WeaponId = "biss",
            Options = new CombatRuleOptionsDto(SpecialResultsEnabled: false)
        }, "owner", "Meister");

        Assert.Null(result.HeroName);
        Assert.Equal(attacker.Id, result.ParticipantId);
        Assert.Equal(attacker.Name, result.ParticipantName);
        Assert.Equal(attacker.Id, result.Snapshot.ParticipantId);
        Assert.Equal(10, result.Snapshot.BaseValue);
        Assert.Equal("DSA 4.1-Katalog", result.Snapshot.ValuesSource);
        Assert.Equal(attacker.Name, result.HistoryEntry.Context?.Snapshot?.ParticipantName);
        Assert.Null(result.HistoryEntry.Context?.Snapshot?.HeroName);
        Assert.NotNull(result.CombatSessionSnapshot?.ActiveExchange?.AttackResult);
    }

    [Fact]
    public async Task Catalog_enemy_defense_and_damage_update_existing_runtime_state()
    {
        using var factory = new TestApplicationFactory();
        var session = factory.Services.GetRequiredService<SessionService>()
            .CreateSession("owner", "Meister", "Katalogwürfe");
        var state = factory.Services.GetRequiredService<CombatSessionStateService>();
        var handler = factory.Services.GetRequiredService<RollCombatHandler>();

        var attacker = await AddEnemyAsync(state, session.SessionId, "owner", "wolf-gemeine-werte", 20);
        var target = await AddEnemyAsync(state, session.SessionId, "owner", "ghul", 10,
            new CombatEnemySelectionDto { EnemyId = "ghul", AttackId = "gezielter-biss" });
        var attack = await DeclareAttackAsync(state, session.SessionId, "owner", attacker, target, "biss");

        var attackRequest = new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ParticipantId = attacker.Id,
            ExchangeId = attack.ExchangeId,
            Action = CombatActionKind.MeleeAttack,
            WeaponId = "biss"
        };
        var attackResult = new CombatRollResultDto(
            Guid.NewGuid(),
            attackRequest.RequestId,
            session.SessionId,
            "owner",
            "Meister",
            null,
            CombatOutcome.Success,
            new CombatRollSnapshotDto
            {
                EntryId = Guid.NewGuid(),
                RequestId = attackRequest.RequestId,
                SessionId = session.SessionId,
                ParticipantId = attacker.Id,
                ExchangeId = attack.ExchangeId,
                Action = CombatActionKind.MeleeAttack,
                BaseValue = 10,
                EffectiveTarget = 10,
                Outcome = CombatOutcome.Success,
                StatusLabel = "Attacke gelungen",
                LabeledRolls = [new CombatLabeledRollDto("Hauptwurf", 20, 5)]
            },
            [new DiceRollDto(20, 5)],
            EmptyHistory());
        var afterAttack = await state.BindAttackRollAsync(attackRequest, attackResult, "owner");
        Assert.Equal(CombatExchangeStatus.DefenseOpen, afterAttack.ActiveExchange!.Status);

        var defense = await handler.HandleAsync(new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ParticipantId = target.Id,
            ExchangeId = attack.ExchangeId,
            Action = CombatActionKind.WeaponParry,
            Modifiers = [new CombatModifierDto("Test", -999)],
            Options = new CombatRuleOptionsDto(SpecialResultsEnabled: false)
        }, "owner", "Meister");
        Assert.Equal(target.Id, defense.ParticipantId);
        Assert.Equal(9, defense.Snapshot.BaseValue);
        Assert.Equal(CombatExchangeStatus.Completed, defense.CombatSessionSnapshot!.ActiveExchange!.Status);
        Assert.NotNull(defense.CombatSessionSnapshot.ActiveExchange.Zone);
        Assert.NotNull(defense.CombatSessionSnapshot.ActiveExchange.Damage);

        var beforeDamage = afterAttack.Participants.Single(participant => participant.Id == target.Id)
            .RuntimeState!.CurrentLeP;
        var targetAfterDamage = defense.CombatSessionSnapshot.Participants.Single(participant => participant.Id == target.Id);
        Assert.Equal(target.Id, defense.ParticipantId);
        Assert.True(defense.CombatSessionSnapshot.ActiveExchange.Damage!.Total >= 4);
        Assert.True(targetAfterDamage.RuntimeState!.CurrentLeP < beforeDamage);

        var undone = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExpectedRevision = defense.CombatSessionSnapshot.Revision,
            Kind = CombatSessionMutationKind.Undo
        }, "owner");
        var targetAfterUndo = undone.Snapshot.Participants.Single(participant => participant.Id == target.Id);
        Assert.Equal(beforeDamage, targetAfterUndo.RuntimeState!.CurrentLeP);
        Assert.Null(undone.Snapshot.ActiveExchange);
    }

    private static async Task<CombatSessionParticipantDto> AddEnemyAsync(
        CombatSessionStateService state,
        string sessionId,
        string userId,
        string enemyId,
        int initiative,
        CombatEnemySelectionDto? selection = null)
    {
        var current = await state.GetAsync(sessionId, userId);
        var result = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = sessionId,
            ExpectedRevision = current.Revision,
            Kind = CombatSessionMutationKind.AddOpponent,
            Initiative = initiative,
            EnemySelection = selection ?? new CombatEnemySelectionDto { EnemyId = enemyId }
        }, userId);
        return Assert.Single(result.Snapshot.Participants,
            participant => participant.OpponentProfile?.CatalogProfile?.CatalogEnemyId == enemyId);
    }

    private static async Task<CombatAttackExchangeDto> DeclareAttackAsync(
        CombatSessionStateService state,
        string sessionId,
        string userId,
        CombatSessionParticipantDto attacker,
        CombatSessionParticipantDto target,
        string attackId)
    {
        var current = await state.GetAsync(sessionId, userId);
        var action = current.Actions.Single(item => item.ParticipantId == attacker.Id &&
                                                     item.Round == current.Round &&
                                                     item.State == CombatActionEntryState.Open);
        var result = await state.MutateAsync(new CombatSessionMutationRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = sessionId,
            ExpectedRevision = current.Revision,
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action.Id,
            ActionKind = CombatActionKind.MeleeAttack,
            ExchangeId = Guid.NewGuid().ToString("N"),
            WeaponId = attackId,
            WeaponName = attackId,
            PhaseInitiative = action.PhaseInitiative
        }, userId);
        return result.Snapshot.ActiveExchange!;
    }

    private static RollHistoryEntryDto EmptyHistory() => new(
        "Meister",
        DateTime.UtcNow,
        [],
        0,
        0,
        null);
}
