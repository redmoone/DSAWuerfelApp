using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Tests;

public sealed class CombatRollRulesTests
{
    [Fact]
    public void Effective_target_applies_all_modifiers()
    {
        var target = CombatRollRules.ResolveEffectiveTarget(
            14,
            [new CombatModifierDto("Maneuver", -2), new CombatModifierDto("Situation", 1)]);

        Assert.Equal(13, target);
    }

    [Fact]
    public void Successful_attack_requires_a_defense_decision()
    {
        var evaluation = CombatRollRules.Evaluate(CombatActionKind.MeleeAttack, 14, [], 10);

        Assert.True(evaluation.IsValid);
        Assert.True(evaluation.IsSuccessful);
        Assert.True(evaluation.RequiresDefenseDecision);
        Assert.Equal(CombatOutcome.Success, evaluation.Outcome);
    }

    [Fact]
    public void Confirmed_attack_one_is_critical()
    {
        var evaluation = CombatRollRules.Evaluate(CombatActionKind.MeleeAttack, 14, [], 1, 12);

        Assert.Equal(CombatOutcome.Critical, evaluation.Outcome);
        Assert.True(evaluation.IsCritical);
        Assert.True(evaluation.RequiresDefenseDecision);
    }

    [Fact]
    public void Unconfirmed_attack_one_is_lucky()
    {
        var evaluation = CombatRollRules.Evaluate(CombatActionKind.MeleeAttack, 14, [], 1, 20);

        Assert.Equal(CombatOutcome.Lucky, evaluation.Outcome);
        Assert.True(evaluation.IsSuccessful);
        Assert.False(evaluation.IsCritical);
    }

    [Fact]
    public void Confirmed_parry_one_is_lucky_but_unconfirmed_parry_is_success()
    {
        var confirmed = CombatRollRules.Evaluate(CombatActionKind.WeaponParry, 12, [], 1, 10);
        var unconfirmed = CombatRollRules.Evaluate(CombatActionKind.WeaponParry, 12, [], 1, 20);

        Assert.Equal(CombatOutcome.Lucky, confirmed.Outcome);
        Assert.Equal(CombatOutcome.Success, unconfirmed.Outcome);
        Assert.False(confirmed.RequiresDefenseDecision);
        Assert.False(unconfirmed.RequiresDefenseDecision);
    }

    [Fact]
    public void Ranged_fumble_control_uses_the_unmodified_target()
    {
        var pending = CombatRollRules.Evaluate(
            CombatActionKind.RangedAttack,
            20,
            [new CombatModifierDto("Entfernung", -5)],
            20,
            unmodifiedBaseValue: 19);
        var fumble = CombatRollRules.Evaluate(
            CombatActionKind.RangedAttack,
            20,
            [new CombatModifierDto("Entfernung", -5)],
            20,
            controlRoll: 20,
            unmodifiedBaseValue: 19);

        Assert.False(pending.IsValid);
        Assert.Equal(15, pending.EffectiveTarget);
        Assert.Equal(19, pending.ControlTarget);
        Assert.Equal(CombatOutcome.Fumble, fumble.Outcome);
        Assert.True(fumble.IsFumble);
    }

    [Fact]
    public void Ranged_lucky_one_requires_penalty_within_base_value()
    {
        var evaluation = CombatRollRules.Evaluate(
            CombatActionKind.RangedAttack,
            10,
            [new CombatModifierDto("Entfernung", -11)],
            1);

        Assert.Equal(CombatOutcome.Failure, evaluation.Outcome);
        Assert.False(evaluation.IsSuccessful);
        Assert.Contains("Glücksbedingung", evaluation.StatusLabel);
    }

    [Fact]
    public void Damage_calculation_keeps_the_full_expression_and_clamps_at_zero()
    {
        var calculated = CombatRollRules.CalculateDamage(5, 4, -1, 2, 3, isCritical: true);
        var clamped = CombatRollRules.CalculateDamage(1, -10, 0, 1, 0);

        Assert.Equal(19, calculated.Total);
        Assert.True(calculated.IsCritical);
        Assert.Equal(0, clamped.Total);
    }
}
