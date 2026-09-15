namespace DsaWuerfelApp.Shared;

public sealed record CombatEnemySelectionDto
{
    public string EnemyId { get; init; } = string.Empty;
    public string? VariantId { get; init; }
    public string? AttackId { get; init; }
    public string? WeaponOption { get; init; }
    public string? ArmorOption { get; init; }
    public int? LeP { get; init; }
}

public sealed record CombatEnemyAdapterResultDto(
    bool IsValid,
    string? Message,
    CombatEnemyResolvedProfileDto? Profile)
{
    public bool IsCombatReady => Profile?.CombatReady == true;
}

public sealed record CombatEnemyResolvedProfileDto
{
    public string CatalogEnemyId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public bool CatalogCombatReady { get; init; }
    public bool CombatReady { get; init; }
    public string Readiness { get; init; } = string.Empty;
    public string? VariantId { get; init; }
    public string? VariantLabel { get; init; }
    public string? AttackId { get; init; }
    public string? WeaponSelection { get; init; }
    public string? ArmorSelection { get; init; }
    public int? LeP { get; init; }
    public int? LePSourceMin { get; init; }
    public int? LePSourceMax { get; init; }
    public int? AuP { get; init; }
    public string? AuPTracking { get; init; }
    public string InitiativeNotation { get; init; } = string.Empty;
    public int? InitiativeBase { get; init; }
    public int InitiativeDiceCount { get; init; }
    public int InitiativeDiceSides { get; init; }
    public string DefenseMode { get; init; } = string.Empty;
    public string? DefenseSourceNotation { get; init; }
    public int? Attack { get; init; }
    public int? Parry { get; init; }
    public int? Dodge { get; init; }
    public int? RangedValue { get; init; }
    public int? MovementGs { get; init; }
    public int? Mr { get; init; }
    public string? MrRaw { get; init; }
    public int? WoundThreshold { get; init; }
    public CombatEnemyArmorDto Armor { get; init; } = new();
    public CombatEnemyAttackDto[] Attacks { get; init; } = [];
    public CombatEnemySourceReferenceDto[] SourceRefs { get; init; } = [];
    public CombatEnemySpecialRuleDto[] SpecialRules { get; init; } = [];
    public string[] SpecialRuleRefs { get; init; } = [];
    public string[] SourceNotes { get; init; } = [];
}
