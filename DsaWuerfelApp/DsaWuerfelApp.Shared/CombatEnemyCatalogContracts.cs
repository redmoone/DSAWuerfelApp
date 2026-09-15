using System.Text.Json;
using System.Text.Json.Serialization;

namespace DsaWuerfelApp.Shared;

public sealed record CombatEnemyCatalogDto
{
    public CombatEnemyCatalogFormatDto Format { get; init; } = new();
    public CombatEnemyRulesetDto Ruleset { get; init; } = new();
    public CombatEnemySourceDto[] Sources { get; init; } = [];
    public CombatEnemySpecialRuleDefinitionDto[] SpecialRuleDefinitions { get; init; } = [];
    public CombatEnemyCatalogEntryDto[] Enemies { get; init; } = [];
}

public sealed record CombatEnemyCatalogFormatDto
{
    public string Id { get; init; } = string.Empty;
    public int SchemaVersion { get; init; }
    public string Language { get; init; } = string.Empty;
    public string IntendedUse { get; init; } = string.Empty;
}

public sealed record CombatEnemyRulesetDto
{
    public string Name { get; init; } = string.Empty;
    public string Edition { get; init; } = string.Empty;
    public bool Strict { get; init; }
    public string[] DoNotMixWith { get; init; } = [];
    public string SourcePolicy { get; init; } = string.Empty;
}

public sealed record CombatEnemySourceDto
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Edition { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
}

public sealed record CombatEnemySourceReferenceDto
{
    public string SourceId { get; init; } = string.Empty;
    public int? PrintedPage { get; init; }
    public int[] PrintedPages { get; init; } = [];
    public string? Section { get; init; }
}

public sealed record CombatEnemyCatalogEntryDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public bool CombatReady { get; init; }
    public string Readiness { get; init; } = string.Empty;
    public CombatEnemySourceReferenceDto[] SourceRefs { get; init; } = [];
    public CombatEnemyBehaviorDto? Behavior { get; init; }
    public CombatEnemyBodyDto? Body { get; init; }
    public Dictionary<string, JsonElement>? Attributes { get; init; }
    public CombatEnemyCombatDto? Combat { get; init; }
    public CombatEnemyCombatVariantDto[] CombatVariants { get; init; } = [];
    public CombatEnemyArmorDto Armor { get; init; } = new();
    public CombatEnemyAttackDto[] Attacks { get; init; } = [];
    public CombatEnemySpecialRuleDto[] SpecialRules { get; init; } = [];
    public CombatEnemyEquipmentDto? Equipment { get; init; }
    public string[] GmNotes { get; init; } = [];
}

public sealed record CombatEnemyBehaviorDto
{
    public string? CombatMotivation { get; init; }
    public string? RetreatRuleRef { get; init; }
    public string[] SourceNotes { get; init; } = [];
}

public sealed record CombatEnemyBodyDto
{
    public string? SizeDescription { get; init; }
    public string? WeightDescription { get; init; }
}

public sealed record CombatEnemyCombatVariantDto
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public CombatEnemyCombatDto Combat { get; init; } = new();
    public string[] SpecialRuleRefs { get; init; } = [];
}

public sealed record CombatEnemyCombatDto
{
    public CombatEnemyInitiativeDto Initiative { get; init; } = new();
    public CombatEnemyDefenseDto Defense { get; init; } = new();
    public CombatEnemyResourcesDto Resources { get; init; } = new();
    public CombatEnemyResistancesDto Resistances { get; init; } = new();
    public CombatEnemyWoundsDto Wounds { get; init; } = new();
    public CombatEnemyMovementDto Movement { get; init; } = new();
    public int? AttackValue { get; init; }
    public int? RangedValue { get; init; }
}

public sealed record CombatEnemyInitiativeDto
{
    public string Notation { get; init; } = string.Empty;
    public int? Base { get; init; }
    public CombatEnemyDiceDto Dice { get; init; } = new();
}

public sealed record CombatEnemyDiceDto
{
    public int Count { get; init; }
    public int Sides { get; init; }
}

public sealed record CombatEnemyDefenseDto
{
    public string Mode { get; init; } = string.Empty;
    public int? ParryValue { get; init; }
    public string? SourceNotation { get; init; }
    public bool? InitiativeLossOnEvasion { get; init; }
    public string? RuleRef { get; init; }
}

public sealed record CombatEnemyResourcesDto
{
    public CombatEnemyResourceDto LeP { get; init; } = new();
    public CombatEnemyResourceDto AuP { get; init; } = new();
}

public sealed record CombatEnemyResourceDto
{
    public int? Maximum { get; init; }
    public int? Initial { get; init; }
    public CombatEnemyRangeDto? SourceRange { get; init; }
    public string? Tracking { get; init; }
}

public sealed record CombatEnemyRangeDto
{
    public int? Min { get; init; }
    public int? Max { get; init; }
}

public sealed record CombatEnemyResistancesDto
{
    public int? Ko { get; init; }
    public int? Mr { get; init; }
    public string? MrRaw { get; init; }
    public int? Gw { get; init; }
}

public sealed record CombatEnemyWoundsDto
{
    public CombatEnemyWoundThresholdDto Threshold { get; init; } = new();
    public string? RuleRef { get; init; }
}

public sealed record CombatEnemyWoundThresholdDto
{
    public string? Kind { get; init; }
    public int? Value { get; init; }
}

public sealed record CombatEnemyMovementDto
{
    public int? Gs { get; init; }
    public string? SourceNotation { get; init; }
    public CombatEnemyMovementAlternateDto? Alternate { get; init; }
}

public sealed record CombatEnemyMovementAlternateDto
{
    public string? State { get; init; }
    public int? Gs { get; init; }
}

public sealed record CombatEnemyArmorDto
{
    public string Model { get; init; } = string.Empty;
    public int? NaturalRs { get; init; }
    public int? EquipmentRs { get; init; }
    public int? TotalRs { get; init; }
    public string? SourceExpression { get; init; }
    public bool UsesZonalArmor { get; init; }
    public int? Head { get; init; }
    public int? Chest { get; init; }
    public int? Back { get; init; }
    public int? Abdomen { get; init; }
    public int? LeftArm { get; init; }
    public int? RightArm { get; init; }
    public int? LeftLeg { get; init; }
    public int? RightLeg { get; init; }
}

public sealed record CombatEnemyAttackDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string? DistanceClass { get; init; }
    public int? Attack { get; init; }
    public int? Parry { get; init; }
    public int? RangedValue { get; init; }
    public string? SourceNotation { get; init; }
    public CombatEnemyDamageNotationDto? Damage { get; init; }
    public string[] SpecialRuleRefs { get; init; } = [];
    public CombatEnemyAvailabilityDto? Availability { get; init; }
}

public sealed record CombatEnemyDamageNotationDto
{
    public string Notation { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
}

public sealed record CombatEnemyAvailabilityDto
{
    public string Kind { get; init; } = string.Empty;
    public string[] Conditions { get; init; } = [];
}

public sealed record CombatEnemySpecialRuleDto
{
    public string RuleId { get; init; } = string.Empty;
    public string? SourceNotation { get; init; }
    public string Resolution { get; init; } = string.Empty;
    public Dictionary<string, JsonElement>? Parameters { get; init; }
    public CombatEnemySourceReferenceDto[] SourceRefs { get; init; } = [];
}

public sealed record CombatEnemySpecialRuleDefinitionDto
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Resolution { get; init; } = string.Empty;
    public Dictionary<string, JsonElement>? Parameters { get; init; }
    public CombatEnemySourceReferenceDto[] SourceRefs { get; init; } = [];
}

public sealed record CombatEnemyEquipmentDto
{
    public string[] WeaponOptions { get; init; } = [];
    public string[] ArmorOptions { get; init; } = [];
    public string? DamageSource { get; init; }
    public bool SelectionRequired { get; init; }
}
