using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Tests;

public sealed class CombatRuntimeModifierRulesTests
{
    [Fact]
    public void Zonal_wounds_apply_their_static_attack_and_parry_effects()
    {
        var profile = CreateProfile(40, 30);
        var set = CreateSet(zonal: true);
        var state = CreateState(
            20,
            20,
            (CombatWoundZone.Torso, 1),
            (CombatWoundZone.LeftLeg, 2),
            (CombatWoundZone.LeftArm, 1));

        var result = CombatRuntimeModifierRules.Resolve(
            profile,
            set,
            state,
            CombatActionKind.MeleeAttack,
            new CombatRuleOptionsDto(LowLePEnabled: true));

        Assert.Collection(
            result.Modifiers,
            torso =>
            {
                Assert.Equal("Brustwunden", torso.Label);
                Assert.Equal(-1, torso.Value);
                Assert.Equal("WdS S. 107–110", torso.Source);
            },
            leg =>
            {
                Assert.Equal("Wunden linkes Bein", leg.Label);
                Assert.Equal(-4, leg.Value);
                Assert.Equal("WdS S. 107–110", leg.Source);
            });
        Assert.Contains(result.RuleNotes, note => note.Contains("Armwunden", StringComparison.Ordinal));
    }

    [Fact]
    public void Low_resource_thresholds_are_strict_and_cumulative_by_level()
    {
        Assert.Equal(0, CombatRuntimeModifierRules.GetLowLePPenalty(20, 40));
        Assert.Equal(1, CombatRuntimeModifierRules.GetLowLePPenalty(19, 40));
        Assert.Equal(2, CombatRuntimeModifierRules.GetLowLePPenalty(13, 40));
        Assert.Equal(3, CombatRuntimeModifierRules.GetLowLePPenalty(9, 40));

        Assert.Equal(0, CombatRuntimeModifierRules.GetLowAuPPenalty(10, 30));
        Assert.Equal(1, CombatRuntimeModifierRules.GetLowAuPPenalty(9, 30));
        Assert.Equal(2, CombatRuntimeModifierRules.GetLowAuPPenalty(7, 30));
    }

    [Fact]
    public void Simple_wounds_and_low_lep_are_combined_but_low_aup_does_not_change_fk()
    {
        var profile = CreateProfile(100, 100);
        var set = CreateSet(zonal: false);
        var state = CreateState(
            49,
            24,
            (CombatWoundZone.Head, 1),
            (CombatWoundZone.Torso, 1));

        var result = CombatRuntimeModifierRules.Resolve(
            profile,
            set,
            state,
            CombatActionKind.RangedAttack,
            new CombatRuleOptionsDto(LowLePEnabled: true));

        Assert.Equal([-4, -1], result.Modifiers.Select(modifier => modifier.Value).ToArray());
        Assert.All(result.Modifiers, modifier => Assert.Equal("WdS S. 83", modifier.Source));
        Assert.DoesNotContain(result.RuleNotes, note => note.Contains("AuP 24/100", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_runtime_values_are_reported_without_becoming_zero()
    {
        var profile = CreateProfile(20, 30);
        var set = CreateSet(zonal: true);
        var state = new CombatRuntimeStateDto
        {
            IsStarted = true,
            CurrentLeP = null,
            CurrentAuP = null,
            Wounds = new Dictionary<CombatWoundZone, int?>
            {
                [CombatWoundZone.RightLeg] = 1
            }
        };

        var result = CombatRuntimeModifierRules.Resolve(
            profile,
            set,
            state,
            CombatActionKind.MeleeAttack,
            new CombatRuleOptionsDto(LowLePEnabled: true));

        Assert.Single(result.Modifiers);
        Assert.Equal(-2, result.Modifiers[0].Value);
        Assert.Contains(result.RuleNotes, note => note.Contains("Wundstand nicht erfasst", StringComparison.Ordinal));
        Assert.Contains(result.RuleNotes, note => note.Contains("LeP oder LeP-Maximum nicht erfasst", StringComparison.Ordinal));
        Assert.Contains(result.RuleNotes, note => note.Contains("AuP oder AuP-Maximum nicht erfasst", StringComparison.Ordinal));
    }

    private static CombatProfileDto CreateProfile(int lep, int aup)
    {
        var set = CreateSet(zonal: true);
        return new CombatProfileDto(
            Guid.NewGuid(),
            "Regeltest",
            1,
            new CombatResourcesDto(lep, aup, null, null),
            4,
            [],
            [set],
            [],
            []);
    }

    private static CombatSetVariantDto CreateSet(bool zonal)
    {
        return new CombatSetVariantDto(
            "set-1",
            1,
            zonal ? CombatArmorModel.Zone : CombatArmorModel.Simple,
            false,
            true,
            zonal,
            12,
            8,
            10,
            null,
            null,
            zonal ? new CombatArmorZonesDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) : null,
            zonal ? null : new CombatSimpleArmorDto(0, 0),
            [],
            []);
    }

    private static CombatRuntimeStateDto CreateState(
        int? lep,
        int? aup,
        params (CombatWoundZone Zone, int? Value)[] entries)
    {
        var wounds = Enum.GetValues<CombatWoundZone>()
            .ToDictionary(zone => zone, _ => (int?)0);
        foreach (var (zone, value) in entries)
        {
            wounds[zone] = value;
        }

        return new CombatRuntimeStateDto
        {
            IsStarted = true,
            CurrentLeP = lep,
            CurrentAuP = aup,
            Wounds = wounds
        };
    }
}
