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
    public void Evaluation_preserves_modifier_breakdown_and_rule_source()
    {
        var modifiers = new[]
        {
            new CombatModifierDto("Wunden", -2, "WdS S. 83"),
            new CombatModifierDto("Entfernung", 1, "Kampfset")
        };

        var evaluation = CombatRollRules.Evaluate(
            CombatActionKind.MeleeAttack,
            14,
            modifiers,
            10,
            valuesSource: "Import + laufender Kampfzustand",
            ruleNotes: ["WdS S. 83: Wunden berücksichtigt."]);

        Assert.Equal(14, evaluation.BaseValue);
        Assert.Equal(13, evaluation.EffectiveTarget);
        Assert.Equal(modifiers, evaluation.Modifiers);
        Assert.Equal("Import + laufender Kampfzustand", evaluation.ValuesSource);
        Assert.Equal(["WdS S. 83: Wunden berücksichtigt."], evaluation.RuleNotes);
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
    public void Missed_attack_completes_without_a_hit()
    {
        var attack = CombatRollRules.Evaluate(CombatActionKind.MeleeAttack, 10, [], 18);

        var decision = CombatRollRules.ResolveAttackDecision(
            attack,
            [CombatActionKind.WeaponParry, CombatActionKind.Dodge]);

        Assert.True(decision.IsValid);
        Assert.Equal(CombatExchangeStatus.Completed, decision.Status);
        Assert.False(decision.IsHit);
    }

    [Fact]
    public void Successful_attack_opens_only_the_allowed_defense_actions()
    {
        var attack = CombatRollRules.Evaluate(CombatActionKind.MeleeAttack, 14, [], 10);

        var decision = CombatRollRules.ResolveAttackDecision(
            attack,
            [CombatActionKind.WeaponParry, CombatActionKind.Dodge]);

        Assert.Equal(CombatExchangeStatus.DefenseOpen, decision.Status);
        Assert.Equal(
            [CombatActionKind.WeaponParry, CombatActionKind.Dodge],
            decision.AllowedDefenseActions);
    }

    [Fact]
    public void Successful_and_failed_defense_resolve_to_avoided_or_hit()
    {
        var attack = CombatRollRules.ResolveAttackDecision(
            CombatRollRules.Evaluate(CombatActionKind.MeleeAttack, 14, [], 10),
            [CombatActionKind.WeaponParry, CombatActionKind.Dodge]);
        var successfulParry = CombatRollRules.Evaluate(CombatActionKind.WeaponParry, 12, [], 8);
        var failedDodge = CombatRollRules.Evaluate(CombatActionKind.Dodge, 8, [], 15);

        var avoided = CombatRollRules.ResolveDefenseDecision(attack, successfulParry);
        var hit = CombatRollRules.ResolveDefenseDecision(attack, failedDodge);

        Assert.Equal(CombatExchangeStatus.Avoided, avoided.Status);
        Assert.False(avoided.IsHit);
        Assert.Equal(CombatExchangeStatus.Hit, hit.Status);
        Assert.True(hit.IsHit);
    }

    [Fact]
    public void Missing_reaction_can_be_resolved_by_the_explicit_rule_option()
    {
        var attack = CombatRollRules.Evaluate(CombatActionKind.MeleeAttack, 14, [], 10);

        var hit = CombatRollRules.ResolveAttackDecision(attack, [], allowUnopposedHit: true);
        var unresolved = CombatRollRules.ResolveAttackDecision(attack, [], allowUnopposedHit: false);

        Assert.Equal(CombatExchangeStatus.Hit, hit.Status);
        Assert.True(hit.IsHit);
        Assert.Equal(CombatExchangeStatus.Completed, unresolved.Status);
        Assert.False(unresolved.IsHit);
    }

    [Fact]
    public void Defense_of_the_wrong_type_is_rejected()
    {
        var attack = CombatRollRules.ResolveAttackDecision(
            CombatRollRules.Evaluate(CombatActionKind.MeleeAttack, 14, [], 10),
            [CombatActionKind.WeaponParry]);
        var dodge = CombatRollRules.Evaluate(CombatActionKind.Dodge, 12, [], 8);

        var decision = CombatRollRules.ResolveDefenseDecision(attack, dodge);

        Assert.False(decision.IsValid);
        Assert.Equal(CombatExchangeStatus.Cancelled, decision.Status);
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

    [Theory]
    [InlineData(4, 5, 0)]
    [InlineData(5, 5, 0)]
    [InlineData(9, 4, 5)]
    public void Armor_reduces_damage_to_non_negative_structure_points(
        int rawDamage,
        int armorRating,
        int expectedStructurePoints)
    {
        var calculated = CombatRollRules.CalculateDamage(
            rawDamage,
            weaponBonus: 0,
            preMultiplierModifier: 0,
            multiplier: 1,
            postMultiplierModifier: 0,
            armorRating: armorRating);

        Assert.Equal(rawDamage, calculated.Total);
        Assert.Equal(armorRating, calculated.ArmorRating);
        Assert.Equal(expectedStructurePoints, calculated.StructurePoints);
    }

    [Fact]
    public void Unknown_armor_keeps_structure_points_unknown()
    {
        var calculated = CombatRollRules.CalculateDamage(9, 0, 0, 1, 0);

        Assert.Null(calculated.ArmorRating);
        Assert.Null(calculated.StructurePoints);
    }

    [Theory]
    [InlineData("1W+4", 1, 6, 4)]
    [InlineData("2W6-1", 2, 6, -1)]
    public void Damage_notation_supports_dsa_four_point_one_w_six_shorthand(
        string notation,
        int expectedCount,
        int expectedSides,
        int expectedBonus)
    {
        Assert.True(CombatRollRules.TryParseDamageNotation(
            notation,
            out var count,
            out var sides,
            out var bonus));

        Assert.Equal(expectedCount, count);
        Assert.Equal(expectedSides, sides);
        Assert.Equal(expectedBonus, bonus);
    }

    [Fact]
    public void Negative_armor_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CombatRollRules.CalculateDamage(9, 0, 0, 1, 0, armorRating: -1));
    }
}
