using System.Globalization;

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
    [Parameter] public bool AttributeMode { get; set; }
    [Parameter] public EventCallback<bool> AttributeModeChanged { get; set; }
    [Parameter] public IReadOnlyList<CombatWeaponDto> Weapons { get; set; } = Array.Empty<CombatWeaponDto>();
    [Parameter] public string? SelectedWeaponId { get; set; }
    [Parameter] public EventCallback<string> WeaponSelected { get; set; }
    [Parameter] public string SelectedAction { get; set; } = "attack";
    [Parameter] public EventCallback<string> ActionSelected { get; set; }
    [Parameter] public IReadOnlyList<ActionOption> Actions { get; set; } = Array.Empty<ActionOption>();
    [Parameter] public IReadOnlyList<ProbeSearchEntryDto> Maneuvers { get; set; } = Array.Empty<ProbeSearchEntryDto>();
    [Parameter] public string SelectedProbe { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> ProbeSelected { get; set; }
    [Parameter] public IReadOnlyList<CombatAttributeDto> Attributes { get; set; } = Array.Empty<CombatAttributeDto>();
    [Parameter] public IReadOnlyList<string> SelectedAttributes { get; set; } = Array.Empty<string>();
    [Parameter] public EventCallback<string> AttributeSelected { get; set; }
    [Parameter] public EventCallback<int> AttributeRemoved { get; set; }
    [Parameter] public int Modifier { get; set; }
    [Parameter] public EventCallback<int> ModifierChanged { get; set; }
    [Parameter] public string RollText { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> RollTextChanged { get; set; }
    [Parameter] public EventCallback ResetRequested { get; set; }
    [Parameter] public EventCallback RollRequested { get; set; }
    [Parameter] public bool CanRoll { get; set; }
    [Parameter] public bool IsBusy { get; set; }
    [Parameter] public int? EffectiveTarget { get; set; }
    [Parameter] public string TargetSource { get; set; } = "Importierter Zielwert";
    [Parameter] public CombatRollResultDto? CombatResult { get; set; }
    [Parameter] public AttributeRollResultDto? AttributeResult { get; set; }
    [Parameter] public bool CanConsumeReaction { get; set; }
    [Parameter] public EventCallback ConsumeReactionRequested { get; set; }
    [Parameter] public IReadOnlyList<int> ResultDiceSides { get; set; } = Array.Empty<int>();
    [Parameter] public IReadOnlyList<int> ResultDiceValues { get; set; } = Array.Empty<int>();
    [Parameter] public long ResultVersion { get; set; }
    [Parameter] public EventCallback ResultDetailsRequested { get; set; }

    private string ActionAvailabilityText => IsLoading
        ? "Profil wird geladen"
        : Sets.Count == 0 ? "Profil wird erwartet" : "Auswahl bereit";

    private string EmptyStateTitle => IsLoading ? "Kampfprofil wird geladen." : "Noch kein Kampfprofil geladen.";

    private string EmptyStateText => IsLoading
        ? "Die Werte werden aus dem gespeicherten Heldenimport gelesen."
        : "Waffen, Zielwerte und Sonderfertigkeiten werden aus dem gespeicherten Heldenimport übernommen.";

    private string EffectiveTargetText => EffectiveTarget?.ToString(CultureInfo.InvariantCulture) ?? "—";

    private string TargetSourceText => EffectiveTarget.HasValue
        ? $"{TargetSource}; Situativ {FormatModifier(Modifier)}"
        : "Für diese Auswahl ist kein Zielwert importiert.";

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

    private static string GetWeaponValue(CombatWeaponDto weapon) => weapon.Category switch
    {
        CombatWeaponCategory.Ranged => FormatValue("FK", weapon.RangedValue),
        CombatWeaponCategory.Shield => FormatValue("PA", weapon.Parry),
        _ => $"AT {FormatNumber(weapon.Attack)} · PA {FormatNumber(weapon.Parry)}"
    };

    private static string FormatValue(string label, int? value) => $"{label} {FormatNumber(value)}";

    private static string FormatNumber(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "—";

    private static string FormatModifier(int value) => value > 0 ? $"+{value}" : value.ToString(CultureInfo.InvariantCulture);

    private static string GetResultSummary(CombatRollResultDto result)
    {
        if (result.Snapshot.Damage is { } damage)
        {
            return $"TP {damage.Total}";
        }

        return result.Snapshot.EffectiveTarget is { } target
            ? $"Wurf {string.Join(" / ", result.Snapshot.LabeledRolls.Select(roll => roll.Value))} · Ziel {target}"
            : string.Join(" / ", result.Snapshot.LabeledRolls.Select(roll => $"W{roll.Sides} {roll.Value}"));
    }

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
