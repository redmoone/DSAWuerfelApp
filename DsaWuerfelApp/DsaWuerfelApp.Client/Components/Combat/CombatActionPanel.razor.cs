using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components.Combat;

public partial class CombatActionPanel
{
    private static readonly int[] EmptyDice = [];

    [Parameter] public IReadOnlyList<CombatSetVariantDto> Sets { get; set; } = Array.Empty<CombatSetVariantDto>();
    [Parameter] public bool IsLoading { get; set; }
    [Parameter] public string? SelectedSetId { get; set; }
    [Parameter] public EventCallback<string> SetSelected { get; set; }
    [Parameter] public IReadOnlyList<CombatWeaponDto> Weapons { get; set; } = Array.Empty<CombatWeaponDto>();
    [Parameter] public string? SelectedWeaponId { get; set; }
    [Parameter] public EventCallback<string> WeaponSelected { get; set; }
    [Parameter] public string SelectedAction { get; set; } = "attack";
    [Parameter] public EventCallback<string> ActionSelected { get; set; }
    [Parameter] public IReadOnlyList<ActionOption> Actions { get; set; } = DefaultActions;
    [Parameter] public IReadOnlyList<ProbeSearchEntryDto> Maneuvers { get; set; } = Array.Empty<ProbeSearchEntryDto>();
    [Parameter] public string SelectedProbe { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> ProbeSelected { get; set; }
    [Parameter] public IReadOnlyList<CombatAttributeDto> Attributes { get; set; } = Array.Empty<CombatAttributeDto>();
    [Parameter] public string? SelectedAttribute { get; set; }
    [Parameter] public EventCallback<string> AttributeSelected { get; set; }
    [Parameter] public int Modifier { get; set; }
    [Parameter] public EventCallback<int> ModifierChanged { get; set; }
    [Parameter] public string RollText { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> RollTextChanged { get; set; }
    [Parameter] public EventCallback ResetRequested { get; set; }
    [Parameter] public EventCallback RollRequested { get; set; }

    private static readonly ActionOption[] DefaultActions =
    [
        new("attack", "Attacke", "Wert aus dem Kampfset", true),
        new("parry", "Parade", "Wert aus dem Kampfset", true),
        new("dodge", "Ausweichen", "Wert aus dem Kampfset", true),
        new("ranged", "Fernkampf", "Nur mit importierter FK-Waffe", false)
    ];

    private string ActionAvailabilityText => IsLoading
        ? "Profil wird geladen"
        : Sets.Count == 0 ? "Profil wird erwartet" : "Auswahl bereit";

    private string EmptyStateTitle => IsLoading ? "Kampfprofil wird geladen." : "Noch kein Kampfprofil geladen.";

    private string EmptyStateText => IsLoading
        ? "Die Werte werden aus dem gespeicherten Heldenimport gelesen."
        : "Waffen, Werte und Sonderfertigkeiten werden aus dem gespeicherten Heldenimport übernommen.";

    private string SelectionSummary
    {
        get
        {
            var action = Actions.FirstOrDefault(item => item.Key == SelectedAction);
            var weapon = Weapons.FirstOrDefault(item => item.Id == SelectedWeaponId);
            var actionText = action?.Label ?? "Keine Aktion";
            return weapon is null ? actionText : $"{weapon.Name} · {actionText} · {GetWeaponValue(weapon)}";
        }
    }

    private static string GetSetLabel(CombatSetVariantDto set)
    {
        var model = set.ArmorModel switch
        {
            CombatArmorModel.Zone => "Zonenrüstung",
            CombatArmorModel.Simple => "Einfache Rüstung",
            _ => "Modell unbekannt"
        };
        return $"Set {set.Number} · {model}";
    }

    private static string GetWeaponLabel(CombatWeaponDto weapon)
    {
        var number = weapon.Number is { } value ? $" Nr. {value}" : string.Empty;
        return $"{weapon.Name}{number}";
    }

    private static string GetWeaponValue(CombatWeaponDto weapon)
    {
        return weapon.Category switch
        {
            CombatWeaponCategory.Ranged => FormatValue("FK", weapon.RangedValue),
            CombatWeaponCategory.Shield => FormatValue("PA", weapon.Parry),
            _ => $"AT {FormatNumber(weapon.Attack)} · PA {FormatNumber(weapon.Parry)}"
        };
    }

    private static string FormatValue(string label, int? value) => $"{label} {FormatNumber(value)}";

    private static string FormatNumber(int? value) => value?.ToString() ?? "—";

    private async Task HandleSetChanged(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        if (!string.IsNullOrWhiteSpace(value))
        {
            await SetSelected.InvokeAsync(value);
        }
    }

    public sealed record ActionOption(string Key, string Label, string Detail, bool IsAvailable);
}
