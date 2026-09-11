using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components.Combat;

public partial class CombatBodyPanel
{
    private static readonly ZoneRow[] Zones =
    [
        new(CombatArmorZone.Head, "Kopf"),
        new(CombatArmorZone.Chest, "Brust"),
        new(CombatArmorZone.Back, "Rücken"),
        new(CombatArmorZone.Abdomen, "Bauch"),
        new(CombatArmorZone.LeftArm, "Linker Arm"),
        new(CombatArmorZone.RightArm, "Rechter Arm"),
        new(CombatArmorZone.LeftLeg, "Linkes Bein"),
        new(CombatArmorZone.RightLeg, "Rechtes Bein")
    ];

    [Parameter] public CombatSetVariantDto? SelectedSet { get; set; }
    [Parameter] public CombatFacing Facing { get; set; } = CombatFacing.Front;
    [Parameter] public EventCallback<CombatFacing> FacingChanged { get; set; }
    [Parameter] public CombatWoundZone? SelectedZone { get; set; }
    [Parameter] public EventCallback<CombatWoundZone> ZoneSelected { get; set; }
    [Parameter] public IReadOnlyDictionary<CombatWoundZone, int?> Wounds { get; set; } =
        new Dictionary<CombatWoundZone, int?>();

    private int? GetArmorValue(CombatArmorZone zone)
    {
        var armor = SelectedSet?.ArmorZones;
        if (armor is null)
        {
            return null;
        }

        return zone switch
        {
            CombatArmorZone.Head => armor.Head,
            CombatArmorZone.Chest => armor.Chest,
            CombatArmorZone.Back => armor.Back,
            CombatArmorZone.Abdomen => armor.Abdomen,
            CombatArmorZone.LeftArm => armor.LeftArm,
            CombatArmorZone.RightArm => armor.RightArm,
            CombatArmorZone.LeftLeg => armor.LeftLeg,
            CombatArmorZone.RightLeg => armor.RightLeg,
            _ => null
        };
    }

    private static CombatWoundZone GetWoundZone(CombatArmorZone zone) => zone switch
    {
        CombatArmorZone.Head => CombatWoundZone.Head,
        CombatArmorZone.Chest or CombatArmorZone.Back => CombatWoundZone.Torso,
        CombatArmorZone.Abdomen => CombatWoundZone.Abdomen,
        CombatArmorZone.LeftArm => CombatWoundZone.LeftArm,
        CombatArmorZone.RightArm => CombatWoundZone.RightArm,
        CombatArmorZone.LeftLeg => CombatWoundZone.LeftLeg,
        CombatArmorZone.RightLeg => CombatWoundZone.RightLeg,
        _ => CombatWoundZone.Torso
    };

    private string GetWoundMarkers(CombatWoundZone zone)
    {
        var value = GetWoundValue(zone);
        return value.HasValue ? $"Wunden {Math.Clamp(value.Value, 0, 3)}/3" : "Wunden ?/3";
    }

    private int? GetWoundValue(CombatWoundZone zone) => Wounds.TryGetValue(zone, out var value) ? value : null;

    private static string FormatValue(int? value) => value?.ToString() ?? "—";

    private string GetAccessibleLabel(ZoneRow zone, CombatWoundZone woundZone)
    {
        var armorText = GetArmorValue(zone.Zone)?.ToString() ?? "nicht importiert";
        var woundText = GetWoundValue(woundZone)?.ToString() ?? "nicht gesetzt";
        return $"{zone.Label}, Rüstungsschutz {armorText}, Wunden {woundText}";
    }

    private sealed record ZoneRow(CombatArmorZone Zone, string Label);
}
