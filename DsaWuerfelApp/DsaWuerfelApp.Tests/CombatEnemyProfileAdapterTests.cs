using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Tests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace DsaWuerfelApp.Tests;

public sealed class CombatEnemyProfileAdapterTests
{
    [Fact]
    public void Maps_ready_animal_values_without_normalizing_source_notation()
    {
        using var factory = new TestApplicationFactory();
        var adapter = factory.Services.GetRequiredService<CombatEnemyProfileAdapter>();

        var result = adapter.Resolve(new CombatEnemySelectionDto { EnemyId = "wolf-gemeine-werte" });

        Assert.True(result.IsValid);
        Assert.True(result.IsCombatReady);
        Assert.Equal("9+1W6", result.Profile!.InitiativeNotation);
        Assert.Equal(9, result.Profile.InitiativeBase);
        Assert.Equal(1, result.Profile.InitiativeDiceCount);
        Assert.Equal(6, result.Profile.InitiativeDiceSides);
        Assert.Equal(23, result.Profile.LeP);
        Assert.Equal(100, result.Profile.AuP);
        Assert.Equal("sourceOnly", result.Profile.AuPTracking);
        Assert.Equal("masterfulEvasion", result.Profile.DefenseMode);
        Assert.Null(result.Profile.Parry);
        Assert.Equal(7, result.Profile.Dodge);
        Assert.Equal(2, result.Profile.Armor.TotalRs);
        Assert.Equal(11, result.Profile.WoundThreshold);
        Assert.Equal("1W6+3", Assert.Single(result.Profile.Attacks).Damage!.Notation);
        Assert.Equal("H", result.Profile.Attacks[0].DistanceClass);
    }

    [Fact]
    public void Keeps_attack_selection_explicit_for_profiles_with_multiple_attacks()
    {
        using var factory = new TestApplicationFactory();
        var adapter = factory.Services.GetRequiredService<CombatEnemyProfileAdapter>();

        var unresolved = adapter.Resolve(new CombatEnemySelectionDto { EnemyId = "borkenbaer" });
        var resolved = adapter.Resolve(new CombatEnemySelectionDto
        {
            EnemyId = "borkenbaer",
            AttackId = "tatze"
        });

        Assert.True(unresolved.IsValid);
        Assert.False(unresolved.IsCombatReady);
        Assert.Equal("requiresAttackSelection", unresolved.Profile!.Readiness);
        Assert.Equal(2, unresolved.Profile.Attacks.Length);
        Assert.True(resolved.IsCombatReady);
        Assert.Equal("tatze", resolved.Profile!.AttackId);
    }

    [Fact]
    public void Requires_a_master_decision_for_source_ranges_and_does_not_invent_humanoid_equipment_values()
    {
        using var factory = new TestApplicationFactory();
        var adapter = factory.Services.GetRequiredService<CombatEnemyProfileAdapter>();

        var foxUnresolved = adapter.Resolve(new CombatEnemySelectionDto { EnemyId = "rotfuchs" });
        var foxResolved = adapter.Resolve(new CombatEnemySelectionDto { EnemyId = "rotfuchs", LeP = 14 });
        var humanoid = adapter.Resolve(new CombatEnemySelectionDto
        {
            EnemyId = "goblin",
            VariantId = "erfahren",
            WeaponOption = "Speer",
            ArmorOption = "Fellkleidung"
        });

        Assert.False(foxUnresolved.IsCombatReady);
        Assert.Equal("sourceDependent", foxUnresolved.Profile!.Readiness);
        Assert.Equal(13, foxUnresolved.Profile.LePSourceMin);
        Assert.Equal(16, foxUnresolved.Profile.LePSourceMax);
        Assert.True(foxResolved.IsCombatReady);
        Assert.Equal(14, foxResolved.Profile!.LeP);

        Assert.True(humanoid.IsValid);
        Assert.False(humanoid.IsCombatReady);
        Assert.Equal("requiresEquipmentValues", humanoid.Profile!.Readiness);
        Assert.Contains("TP-/RS", humanoid.Message, StringComparison.Ordinal);
    }
}
