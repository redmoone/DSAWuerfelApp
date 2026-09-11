using System.Xml.Serialization;

namespace DsaWuerfelApp.Core.Dtos;

[XmlRoot("daten")]
public sealed class CombatXmlDatenDto
{
    [XmlElement("config")] public CombatXmlConfigDto Config { get; set; } = new();
    [XmlElement("angaben")] public CombatXmlAngabenDto Angaben { get; set; } = new();
    [XmlElement("eigenschaften")] public CombatXmlEigenschaftenDto Eigenschaften { get; set; } = new();

    [XmlArray("kampfsets")]
    [XmlArrayItem("kampfset")]
    public List<CombatXmlSetDto> Kampfsets { get; set; } = [];

    [XmlArray("sonderfertigkeiten")]
    [XmlArrayItem("sonderfertigkeit")]
    public List<CombatXmlSpecializationDto> Sonderfertigkeiten { get; set; } = [];

    [XmlArray("verbilligtesonderfertigkeiten")]
    [XmlArrayItem("sonderfertigkeit")]
    public List<CombatXmlSpecializationDto> VerguenstigteSonderfertigkeiten { get; set; } = [];

    [XmlArray("vorteile")]
    [XmlArrayItem("vorteil")]
    public List<CombatXmlBenefitDto> Vorteile { get; set; } = [];
}

public sealed class CombatXmlConfigDto
{
    [XmlElement("rsmodell")] public string? Ruestungsmodell { get; set; }
}

public sealed class CombatXmlAngabenDto
{
    [XmlElement("name")] public string? Name { get; set; }
    [XmlElement("wundschwelle")] public string? Wundschwelle { get; set; }
}

public sealed class CombatXmlEigenschaftenDto
{
    [XmlElement("mut")] public CombatXmlValueDto? Mut { get; set; }
    [XmlElement("klugheit")] public CombatXmlValueDto? Klugheit { get; set; }
    [XmlElement("intuition")] public CombatXmlValueDto? Intuition { get; set; }
    [XmlElement("charisma")] public CombatXmlValueDto? Charisma { get; set; }
    [XmlElement("fingerfertigkeit")] public CombatXmlValueDto? Fingerfertigkeit { get; set; }
    [XmlElement("gewandtheit")] public CombatXmlValueDto? Gewandtheit { get; set; }
    [XmlElement("konstitution")] public CombatXmlValueDto? Konstitution { get; set; }
    [XmlElement("koerperkraft")] public CombatXmlValueDto? Koerperkraft { get; set; }
    [XmlElement("attacke")] public CombatXmlValueDto? Attacke { get; set; }
    [XmlElement("parade")] public CombatXmlValueDto? Parade { get; set; }
    [XmlElement("fernkampf-basis")] public CombatXmlValueDto? FernkampfBasis { get; set; }
    [XmlElement("initiative")] public CombatXmlValueDto? Initiative { get; set; }
    [XmlElement("lebensenergie")] public CombatXmlValueDto? Lebensenergie { get; set; }
    [XmlElement("ausdauer")] public CombatXmlValueDto? Ausdauer { get; set; }
    [XmlElement("astralenergie")] public CombatXmlValueDto? Astralenergie { get; set; }
    [XmlElement("karmaenergie")] public CombatXmlValueDto? Karmaenergie { get; set; }
}

public sealed class CombatXmlValueDto
{
    [XmlElement("akt")] public string? Akt { get; set; }
}

public sealed class CombatXmlSetDto
{
    [XmlAttribute("nr")] public string? Number { get; set; }
    [XmlAttribute("tzm")] public string? UsesZonalArmor { get; set; }
    [XmlAttribute("inbenutzung")] public string? IsInUse { get; set; }
    [XmlAttribute("defaultrsmodel")] public string? IsDefault { get; set; }

    [XmlElement("raufen")] public CombatXmlUnarmedDto? Raufen { get; set; }
    [XmlElement("ringen")] public CombatXmlUnarmedDto? Ringen { get; set; }
    [XmlElement("ausweichen")] public string? Dodge { get; set; }
    [XmlElement("geschwindigkeitinklbe")] public string? SpeedWithArmor { get; set; }
    [XmlElement("ini")] public string? Initiative { get; set; }
    [XmlElement("ruestungzonen")] public CombatXmlArmorZonesDto? ArmorZones { get; set; }
    [XmlElement("ruestungeinfach")] public CombatXmlSimpleArmorDto? SimpleArmor { get; set; }
    [XmlElement("nahkampfwaffen")] public CombatXmlMeleeWeaponsDto? MeleeWeapons { get; set; }
    [XmlElement("fernkampfwaffen")] public CombatXmlRangedWeaponsDto? RangedWeapons { get; set; }
    [XmlElement("schilder")] public CombatXmlShieldsDto? Shields { get; set; }
    [XmlElement("ruestungen")] public CombatXmlArmorPiecesDto? ArmorPieces { get; set; }
}

public sealed class CombatXmlUnarmedDto
{
    [XmlElement("at")] public string? Attack { get; set; }
    [XmlElement("pa")] public string? Parry { get; set; }
    [XmlElement("tp")] public string? Damage { get; set; }
}

public sealed class CombatXmlArmorZonesDto
{
    [XmlElement("kopf")] public string? Head { get; set; }
    [XmlElement("brust")] public string? Chest { get; set; }
    [XmlElement("ruecken")] public string? Back { get; set; }
    [XmlElement("bauch")] public string? Abdomen { get; set; }
    [XmlElement("linkerarm")] public string? LeftArm { get; set; }
    [XmlElement("rechterarm")] public string? RightArm { get; set; }
    [XmlElement("linkesbein")] public string? LeftLeg { get; set; }
    [XmlElement("rechtesbein")] public string? RightLeg { get; set; }
    [XmlElement("gesamt")] public string? Total { get; set; }
    [XmlElement("gesamtschutz")] public string? TotalProtection { get; set; }
    [XmlElement("gesamtzonenschutz")] public string? TotalZoneProtection { get; set; }
    [XmlElement("behinderung")] public string? Encumbrance { get; set; }
}

public sealed class CombatXmlSimpleArmorDto
{
    [XmlElement("gesamt")] public string? Total { get; set; }
    [XmlElement("behinderung")] public string? Encumbrance { get; set; }
}

public sealed class CombatXmlMeleeWeaponsDto
{
    [XmlAttribute("inbenutzung")] public string? IsInUse { get; set; }

    [XmlElement("nahkampfwaffe")]
    public List<CombatXmlWeaponDto> Weapons { get; set; } = [];
}

public sealed class CombatXmlRangedWeaponsDto
{
    [XmlAttribute("inbenutzung")] public string? IsInUse { get; set; }

    [XmlElement("fernkampfwaffe")]
    public List<CombatXmlWeaponDto> Weapons { get; set; } = [];
}

public sealed class CombatXmlShieldsDto
{
    [XmlAttribute("inbenutzung")] public string? IsInUse { get; set; }

    [XmlElement("schild")]
    public List<CombatXmlWeaponDto> Weapons { get; set; } = [];
}

public sealed class CombatXmlWeaponDto
{
    [XmlElement("nummer")] public string? Number { get; set; }
    [XmlElement("möglich")] public string? IsAvailable { get; set; }
    [XmlElement("name")] public string? Name { get; set; }
    [XmlElement("tp")] public string? BaseDamage { get; set; }
    [XmlElement("tpinkl")] public string? CalculatedDamage { get; set; }
    [XmlElement("wm")] public string? WeaponModifier { get; set; }
    [XmlElement("ini")] public string? Initiative { get; set; }
    [XmlElement("dk")] public string? DistanceClass { get; set; }
    [XmlElement("tpkk")] public CombatXmlDamageThresholdDto? DamageThreshold { get; set; }
    [XmlElement("at")] public string? Attack { get; set; }
    [XmlElement("pa")] public string? Parry { get; set; }
    [XmlElement("waffentalent")] public string? WeaponTalent { get; set; }
    [XmlElement("kampftalent")] public string? CombatTalent { get; set; }
    [XmlElement("be")] public string? Encumbrance { get; set; }
    [XmlElement("mod")] public string? ShieldModifier { get; set; }
    [XmlElement("typ")] public string? ShieldType { get; set; }
    [XmlElement("bfmin")] public string? BreakageMinimum { get; set; }
    [XmlElement("bfakt")] public string? Breakage { get; set; }
    [XmlElement("bf")] public string? BreakageCurrent { get; set; }
    [XmlElement("ladezeit")] public string? ReloadTime { get; set; }

    [XmlElement("reichweite")] public List<string> Ranges { get; set; } = [];
    [XmlElement("tpmod")] public List<string> RangeDamageModifiers { get; set; } = [];
}

public sealed class CombatXmlDamageThresholdDto
{
    [XmlText] public string? Text { get; set; }
    [XmlAttribute("schwelle")] public string? Threshold { get; set; }
}

public sealed class CombatXmlArmorPiecesDto
{
    [XmlAttribute("inbenutzung")] public string? IsInUse { get; set; }

    [XmlElement("ruestung")]
    public List<CombatXmlArmorPieceDto> Pieces { get; set; } = [];
}

public sealed class CombatXmlArmorPieceDto
{
    [XmlElement("nummer")] public string? Number { get; set; }
    [XmlElement("name")] public string? Name { get; set; }
    [XmlElement("grundlage")] public string? Basis { get; set; }
    [XmlElement("rs")] public string? RawRs { get; set; }
    [XmlElement("be")] public string? RawBe { get; set; }
    [XmlElement("kopf")] public string? Head { get; set; }
    [XmlElement("brust")] public string? Chest { get; set; }
    [XmlElement("ruecken")] public string? Back { get; set; }
    [XmlElement("bauch")] public string? Abdomen { get; set; }
    [XmlElement("linkerarm")] public string? LeftArm { get; set; }
    [XmlElement("rechterarm")] public string? RightArm { get; set; }
    [XmlElement("linkesbein")] public string? LeftLeg { get; set; }
    [XmlElement("rechtesbein")] public string? RightLeg { get; set; }
    [XmlElement("gesamt")] public string? Total { get; set; }
    [XmlElement("gesamtzonenschutz")] public string? TotalZoneProtection { get; set; }
    [XmlElement("behinderung")] public string? Encumbrance { get; set; }
}

public sealed class CombatXmlSpecializationDto
{
    [XmlElement("name")] public string? Name { get; set; }
    [XmlElement("nameausfuehrlich")] public string? DetailedName { get; set; }
    [XmlElement("bezeichner")] public string? Identifier { get; set; }

    [XmlElement("bereich")]
    public List<string> Categories { get; set; } = [];
}

public sealed class CombatXmlBenefitDto
{
    [XmlElement("name")] public string? Name { get; set; }
    [XmlElement("bezeichner")] public string? Identifier { get; set; }
}
