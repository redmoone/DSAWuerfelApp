using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components.Combat;

public partial class CombatBodyPanel
{
    private static readonly ZoneRow[] Zones =
    [
        new(CombatWoundZone.Head, "Kopf", [CombatArmorZone.Head]),
        new(CombatWoundZone.Torso, "Torso", [CombatArmorZone.Chest, CombatArmorZone.Back]),
        new(CombatWoundZone.Abdomen, "Bauch", [CombatArmorZone.Abdomen]),
        new(CombatWoundZone.LeftArm, "Linker Arm", [CombatArmorZone.LeftArm]),
        new(CombatWoundZone.RightArm, "Rechter Arm", [CombatArmorZone.RightArm]),
        new(CombatWoundZone.LeftLeg, "Linkes Bein", [CombatArmorZone.LeftLeg]),
        new(CombatWoundZone.RightLeg, "Rechtes Bein", [CombatArmorZone.RightLeg])
    ];

    [Parameter] public CombatSetVariantDto? SelectedSet { get; set; }
    [Parameter] public CombatFacing Facing { get; set; } = CombatFacing.Front;
    [Parameter] public EventCallback<CombatFacing> FacingChanged { get; set; }
    [Parameter] public CombatWoundZone? SelectedZone { get; set; }
    [Parameter] public EventCallback<CombatWoundZone> ZoneSelected { get; set; }
    [Parameter] public IReadOnlyDictionary<CombatWoundZone, int?> Wounds { get; set; } =
        new Dictionary<CombatWoundZone, int?>();
    [Parameter] public EventCallback<WoundChange> WoundChanged { get; set; }
    [Parameter] public bool IsBusy { get; set; }

    private bool UsesZonalArmor => SelectedSet?.UsesZonalArmor == true ||
                                   SelectedSet?.ArmorModel == CombatArmorModel.Zone ||
                                   SelectedSet?.ArmorZones is not null;

    private int? GetArmorValue(CombatArmorZone zone)
    {
        var armor = SelectedSet?.ArmorZones;
        return zone switch
        {
            CombatArmorZone.Head => armor?.Head,
            CombatArmorZone.Chest => armor?.Chest,
            CombatArmorZone.Back => armor?.Back,
            CombatArmorZone.Abdomen => armor?.Abdomen,
            CombatArmorZone.LeftArm => armor?.LeftArm,
            CombatArmorZone.RightArm => armor?.RightArm,
            CombatArmorZone.LeftLeg => armor?.LeftLeg,
            CombatArmorZone.RightLeg => armor?.RightLeg,
            _ => null
        };
    }

    private int? GetWoundValue(CombatWoundZone zone) => Wounds.TryGetValue(zone, out var value) ? value : null;

    private static string FormatValue(int? value) => value?.ToString() ?? "—";

    private static string GetArmorLabel(CombatArmorZone zone) => zone switch
    {
        CombatArmorZone.Head => "Kopf",
        CombatArmorZone.Chest => "Brust",
        CombatArmorZone.Back => "Rücken",
        CombatArmorZone.Abdomen => "Bauch",
        CombatArmorZone.LeftArm => "Linker Arm",
        CombatArmorZone.RightArm => "Rechter Arm",
        CombatArmorZone.LeftLeg => "Linkes Bein",
        CombatArmorZone.RightLeg => "Rechtes Bein",
        _ => zone.ToString()
    };

    private string GetAccessibleLabel(ZoneRow zone)
    {
        var armorText = !UsesZonalArmor
            ? SelectedSet?.SimpleArmor is not null ? "Gesamtrüstung" : "nicht importiert"
            : string.Join(", ", zone.ArmorZones.Select(armorZone => $"{GetArmorLabel(armorZone)} RS {FormatValue(GetArmorValue(armorZone))}"));
        var woundText = GetWoundValue(zone.WoundZone)?.ToString() ?? "nicht gesetzt";
        return $"{zone.Label}, {armorText}, Wunden {woundText}";
    }

    public sealed record WoundChange(CombatWoundZone Zone, int? Value);

    private sealed record ZoneRow(CombatWoundZone WoundZone, string Label, CombatArmorZone[] ArmorZones);
}
