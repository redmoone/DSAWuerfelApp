using System.Globalization;
using System.Text.RegularExpressions;

using DsaWuerfelApp.Core.Dtos;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Core.Mappers;

public sealed class HeroCombatMapper
{
    private static readonly AttributeDefinition[] AttributeDefinitions =
    [
        new("MU", "Mut", "mut", "lion.svg"),
        new("KL", "Klugheit", "klugheit", "owl.svg"),
        new("IN", "Intuition", "intuition", "eye.svg"),
        new("CH", "Charisma", "charisma", "mask.svg"),
        new("FF", "Fingerfertigkeit", "fingerfertigkeit", "hand.svg"),
        new("GE", "Gewandtheit", "gewandtheit", "cat.svg"),
        new("KO", "Konstitution", "konstitution", "shield.svg"),
        new("KK", "Körperkraft", "koerperkraft", "muscle.svg")
    ];

    public CombatProfileDto Map(Hero hero, CombatXmlDatenDto source)
    {
        ArgumentNullException.ThrowIfNull(hero);
        ArgumentNullException.ThrowIfNull(source);

        return new CombatProfileDto(
            hero.Id,
            string.IsNullOrWhiteSpace(hero.Name) ? Text(source.Angaben.Name) ?? string.Empty : hero.Name,
            hero.ImportVersion,
            MapResources(source.Eigenschaften),
            ParseInt(source.Angaben.Wundschwelle),
            MapAttributes(source.Eigenschaften),
            source.Kampfsets.Select((set, index) => MapSet(hero.Id, source.Config, set, index)).ToArray(),
            MapSpecializations(source),
            MapBenefits(source));
    }

    private static CombatResourcesDto MapResources(CombatXmlEigenschaftenDto source)
    {
        return new CombatResourcesDto(
            ParseInt(source.Lebensenergie?.Akt),
            ParseInt(source.Ausdauer?.Akt),
            ParseInt(source.Astralenergie?.Akt),
            ParseInt(source.Karmaenergie?.Akt));
    }

    private static CombatAttributeDto[] MapAttributes(CombatXmlEigenschaftenDto source)
    {
        return AttributeDefinitions
            .Select(definition => new CombatAttributeDto(
                definition.Key,
                definition.Label,
                ParseInt(GetValue(source, definition.XmlName)?.Akt),
                definition.IconPath))
            .ToArray();
    }

    private static CombatSetVariantDto MapSet(
        Guid heroId,
        CombatXmlConfigDto config,
        CombatXmlSetDto source,
        int index)
    {
        var number = ParseInt(source.Number) ?? 0;
        var zonalFlag = ParseBool(source.UsesZonalArmor);
        var usesZonalArmor = zonalFlag ?? source.ArmorZones is not null;
        var armorModel = zonalFlag switch
        {
            true => CombatArmorModel.Zone,
            false => CombatArmorModel.Simple,
            _ when source.ArmorZones is not null => CombatArmorModel.Zone,
            _ when source.SimpleArmor is not null => CombatArmorModel.Simple,
            _ => MapConfiguredArmorModel(config.Ruestungsmodell)
        };
        var modelKey = armorModel switch
        {
            CombatArmorModel.Zone => "zone",
            CombatArmorModel.Simple => "simple",
            _ => "unknown"
        };
        var setId = $"{heroId:N}:set:{number}:{modelKey}";

        var weapons = new List<CombatWeaponDto>();
        AddWeapons(weapons, heroId, setId, number, modelKey, CombatWeaponCategory.Melee,
            source.MeleeWeapons?.Weapons);
        AddWeapons(weapons, heroId, setId, number, modelKey, CombatWeaponCategory.Ranged,
            source.RangedWeapons?.Weapons);
        AddWeapons(weapons, heroId, setId, number, modelKey, CombatWeaponCategory.Shield,
            source.Shields?.Weapons);

        return new CombatSetVariantDto(
            setId,
            number,
            armorModel,
            ParseBool(source.IsInUse) ?? false,
            ParseBool(source.IsDefault) ?? false,
            usesZonalArmor,
            ParseInt(source.Dodge),
            ParseInt(source.SpeedWithArmor),
            ParseInt(source.Initiative),
            MapUnarmed(source.Raufen),
            MapUnarmed(source.Ringen),
            MapArmorZones(source.ArmorZones),
            MapSimpleArmor(source.SimpleArmor),
            weapons.ToArray(),
            MapArmorPieces(source.ArmorPieces));
    }

    private static CombatArmorModel MapConfiguredArmorModel(string? model)
    {
        return Text(model)?.ToLowerInvariant() switch
        {
            "zone" or "zonen" or "zonenmodell" => CombatArmorModel.Zone,
            "einfach" or "simple" or "einfaches" => CombatArmorModel.Simple,
            _ => CombatArmorModel.Unknown
        };
    }

    private static CombatUnarmedProfileDto? MapUnarmed(CombatXmlUnarmedDto? source)
    {
        return source is null
            ? null
            : new CombatUnarmedProfileDto(ParseInt(source.Attack), ParseInt(source.Parry), Text(source.Damage));
    }

    private static CombatArmorZonesDto? MapArmorZones(CombatXmlArmorZonesDto? source)
    {
        return source is null
            ? null
            : new CombatArmorZonesDto(
                ParseInt(source.Head),
                ParseInt(source.Chest),
                ParseInt(source.Back),
                ParseInt(source.Abdomen),
                ParseInt(source.LeftArm),
                ParseInt(source.RightArm),
                ParseInt(source.LeftLeg),
                ParseInt(source.RightLeg),
                ParseInt(source.Total),
                ParseInt(source.TotalProtection),
                ParseInt(source.TotalZoneProtection),
                ParseInt(source.Encumbrance));
    }

    private static CombatSimpleArmorDto? MapSimpleArmor(CombatXmlSimpleArmorDto? source)
    {
        return source is null ? null : new CombatSimpleArmorDto(ParseInt(source.Total), ParseInt(source.Encumbrance));
    }

    private static CombatArmorPieceDto[] MapArmorPieces(CombatXmlArmorPiecesDto? source)
    {
        return source?.Pieces.Select(piece => new CombatArmorPieceDto(
            ParseInt(piece.Number),
            Text(piece.Name) ?? string.Empty,
            Text(piece.Basis),
            Text(piece.RawRs),
            Text(piece.RawBe),
            ParseInt(piece.Head),
            ParseInt(piece.Chest),
            ParseInt(piece.Back),
            ParseInt(piece.Abdomen),
            ParseInt(piece.LeftArm),
            ParseInt(piece.RightArm),
            ParseInt(piece.LeftLeg),
            ParseInt(piece.RightLeg),
            ParseInt(piece.Total),
            ParseInt(piece.TotalZoneProtection),
            ParseInt(piece.Encumbrance))).ToArray() ?? [];
    }

    private static void AddWeapons(
        List<CombatWeaponDto> target,
        Guid heroId,
        string setId,
        int setNumber,
        string modelKey,
        CombatWeaponCategory category,
        IReadOnlyList<CombatXmlWeaponDto>? sources)
    {
        if (sources is null)
        {
            return;
        }

        var categoryKey = category switch
        {
            CombatWeaponCategory.Melee => "melee",
            CombatWeaponCategory.Ranged => "ranged",
            CombatWeaponCategory.Shield => "shield",
            _ => "unarmed"
        };

        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var number = ParseInt(source.Number);
            var identity = number?.ToString(CultureInfo.InvariantCulture) ?? $"index-{index + 1}";
            var id = $"{heroId:N}:set:{setNumber}:{modelKey}:weapon:{categoryKey}:{identity}";
            var ranges = ParseNumbers(source.Ranges);
            var rangeModifiers = ParseNumbers(source.RangeDamageModifiers);
            var weapon = new CombatWeaponDto(
                id,
                category,
                number,
                Text(source.Name) ?? string.Empty,
                ParseBool(source.IsAvailable),
                category == CombatWeaponCategory.Ranged ? null : ParseInt(source.Attack),
                ParseInt(source.Parry),
                category == CombatWeaponCategory.Ranged ? ParseInt(source.Attack) : null,
                Text(source.BaseDamage),
                Text(source.CalculatedDamage),
                Text(source.WeaponModifier),
                ParseInt(source.Initiative),
                Text(source.DistanceClass),
                Text(source.DamageThreshold?.Text),
                Text(source.WeaponTalent) ?? Text(source.CombatTalent),
                Text(source.Encumbrance),
                ranges,
                rangeModifiers,
                ParseInt(source.ReloadTime),
                Text(source.ShieldType),
                Text(source.ShieldModifier),
                Text(source.BreakageMinimum),
                Text(source.BreakageCurrent),
                Text(source.Breakage))
            {
                RangeText = JoinRaw(source.Ranges),
                RangeDamageModifierText = JoinRaw(source.RangeDamageModifiers)
            };

            target.Add(weapon);
        }
    }

    private static CombatSpecializationDto[] MapSpecializations(CombatXmlDatenDto source)
    {
        var learned = source.Sonderfertigkeiten
            .Select(item => MapSpecialization(item, true))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
        var discounted = source.VerguenstigteSonderfertigkeiten
            .Select(item => MapSpecialization(item, false))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();

        return learned.Concat(discounted).ToArray();
    }

    private static CombatSpecializationDto? MapSpecialization(CombatXmlSpecializationDto source, bool learned)
    {
        var name = Text(source.DetailedName) ?? Text(source.Name) ?? Text(source.Identifier);
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new CombatSpecializationDto(
                name,
                Text(source.Identifier),
                source.Categories.Select(Text).Where(category => category is not null).Select(category => category!).Distinct().ToArray(),
                learned);
    }

    private static CombatBenefitDto[] MapBenefits(CombatXmlDatenDto source)
    {
        return source.Vorteile
            .Select(item => new CombatBenefitDto(Text(item.Name) ?? Text(item.Identifier) ?? string.Empty, Text(item.Identifier)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray();
    }

    private static CombatXmlValueDto? GetValue(CombatXmlEigenschaftenDto source, string name)
    {
        return name switch
        {
            "mut" => source.Mut,
            "klugheit" => source.Klugheit,
            "intuition" => source.Intuition,
            "charisma" => source.Charisma,
            "fingerfertigkeit" => source.Fingerfertigkeit,
            "gewandtheit" => source.Gewandtheit,
            "konstitution" => source.Konstitution,
            "koerperkraft" => source.Koerperkraft,
            _ => null
        };
    }

    private static int[] ParseNumbers(IEnumerable<string> values)
    {
        var parsed = new List<int>();
        foreach (var value in values)
        {
            foreach (Match match in Regex.Matches(value ?? string.Empty, @"-?\d+"))
            {
                if (int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    parsed.Add(number);
                }
            }
        }

        return parsed.ToArray();
    }

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool? ParseBool(string? value)
    {
        return bool.TryParse(value?.Trim(), out var parsed) ? parsed : null;
    }

    private static string? Text(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? JoinRaw(IEnumerable<string> values)
    {
        var items = values.Select(Text).Where(item => item is not null).Select(item => item!).ToArray();
        return items.Length == 0 ? null : string.Join(" / ", items);
    }

    private sealed record AttributeDefinition(string Key, string Label, string XmlName, string IconPath);
}
