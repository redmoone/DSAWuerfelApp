using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components.Combat;

public partial class CombatStatusPanel
{
    [Parameter] public string HeroName { get; set; } = "Kein aktiver Held";
    [Parameter] public string? SessionName { get; set; }
    [Parameter] public CombatProfileDto? Profile { get; set; }
    [Parameter] public bool IsLoading { get; set; }
    [Parameter] public string? ErrorMessage { get; set; }
    [Parameter] public CombatSetVariantDto? SelectedSet { get; set; }
    [Parameter] public int? CurrentLeP { get; set; }
    [Parameter] public int? CurrentAuP { get; set; }
    [Parameter] public int? CurrentInitiative { get; set; }
    [Parameter] public IReadOnlyDictionary<CombatWoundZone, int?> Wounds { get; set; } =
        new Dictionary<CombatWoundZone, int?>();
    [Parameter] public IReadOnlyList<string> Effects { get; set; } = Array.Empty<string>();

    private int TotalWounds => Wounds.Values.Where(value => value.HasValue).Sum(value => Math.Clamp(value!.Value, 0, 3));

    private string FormatCurrent(int? current, int? maximum)
    {
        return current.HasValue && maximum.HasValue ? $"{current} / {maximum}" : FormatValue(current ?? maximum);
    }

    private static string GetResourceState(int? current) => current.HasValue ? "laufend" : "noch nicht gestartet";

    private int? GetTotalArmor()
    {
        if (SelectedSet?.ArmorZones is { } zones)
        {
            return zones.TotalZoneProtection ?? zones.TotalProtection ?? zones.Total;
        }

        return SelectedSet?.SimpleArmor?.Total;
    }

    private int? GetEncumbrance() => SelectedSet?.ArmorZones?.Encumbrance ?? SelectedSet?.SimpleArmor?.Encumbrance;

    private string GetArmorLabel() => SelectedSet?.ArmorZones is not null ? "Zonenmodell" : "Einfaches Modell";

    private static string FormatValue(int? value) => value?.ToString() ?? "—";
}
