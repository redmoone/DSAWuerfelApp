using System.Globalization;

using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components.Combat;

public partial class CombatActionPanel
{
    private static readonly int[] EmptyDice = [];

    // These set parameters remain available for the shared component contract. The Kampf page
    // renders the set selector in the status panel so that context is shown exactly once.
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
    [Parameter] public int? BaseTarget { get; set; }
    [Parameter] public int? EffectiveTarget { get; set; }
    [Parameter] public string TargetSource { get; set; } = "Importierter Zielwert";
    [Parameter] public CombatFacing Facing { get; set; }
    [Parameter] public EventCallback<CombatFacing> FacingChanged { get; set; }
    [Parameter] public CombatRollResultDto? CombatResult { get; set; }
    [Parameter] public AttributeRollResultDto? AttributeResult { get; set; }
    [Parameter] public bool CanConsumeReaction { get; set; }
    [Parameter] public EventCallback ConsumeReactionRequested { get; set; }
    [Parameter] public IReadOnlyList<int> ResultDiceSides { get; set; } = Array.Empty<int>();
    [Parameter] public IReadOnlyList<int> ResultDiceValues { get; set; } = Array.Empty<int>();
    [Parameter] public long ResultVersion { get; set; }
    [Parameter] public IReadOnlyList<RollHistoryEntryDto> History { get; set; } = Array.Empty<RollHistoryEntryDto>();
    [Parameter] public EventCallback<RollHistoryEntryDto> HistoryEntrySelected { get; set; }
    [Parameter] public EventCallback HistoryRequested { get; set; }
    [Parameter] public EventCallback ResultDetailsRequested { get; set; }

    private IEnumerable<ActionOption> MainActions => Actions.Where(action => action.Key is "attack" or "parry" or "shield-parry" or "dodge" or "ranged");
    private IEnumerable<ActionOption> AdditionalActions => Actions.Where(action => action.Key is "damage" or "zone");
    private IEnumerable<ActionOption> HelperActions => Actions.Where(action => action.Key is "wound-helper" or "fumble-helper");
    private ActionOption? SelectedActionOption => Actions.FirstOrDefault(action => action.Key == SelectedAction);

    private string ActionAvailabilityText => IsLoading
        ? "Profil wird geladen"
        : Sets.Count == 0 ? "Profil wird erwartet" : CanRoll ? "Auswahl bereit" : "Auswahl vervollständigen";

    private string EmptyStateTitle => IsLoading ? "Kampfprofil wird geladen." : "Noch kein Kampfprofil geladen.";

    private string EmptyStateText => IsLoading
        ? "Die Werte werden aus dem gespeicherten Heldenimport gelesen."
        : "Waffen, Zielwerte und Sonderfertigkeiten werden aus dem gespeicherten Heldenimport übernommen.";

    private string EffectiveTargetText => EffectiveTarget?.ToString(CultureInfo.InvariantCulture) ?? "—";

    private string TargetSourceText => EffectiveTarget.HasValue
        ? $"{TargetSource}; Basis {FormatNumber(BaseTarget)}"
        : TargetSource;

    private string ActiveRollTitle => AttributeMode
        ? "Eigenschaftsprobe"
        : SelectedActionOption?.Label ?? "Kampfwurf";

    private string ActiveRollSubtitle => AttributeMode
        ? $"{SelectedAttributes.Count} Eigenschaft{(SelectedAttributes.Count == 1 ? string.Empty : "en")} ausgewählt"
        : SelectedActionOption?.Detail ?? "Noch keine Aktion ausgewählt";

    private string RollButtonText => AttributeMode
        ? "Eigenschaften würfeln"
        : SelectedAction switch
        {
            "attack" => "Attacke würfeln",
            "parry" => "Waffenparade würfeln",
            "shield-parry" => "Schildparade würfeln",
            "dodge" => "Ausweichen würfeln",
            "ranged" => "Fernkampf würfeln",
            "damage" => "TP würfeln",
            "zone" => "Trefferzone würfeln",
            "wound-helper" => "Wund-Hilfswurf",
            "fumble-helper" => "Patzer-Hilfswurf",
            _ => "Wurf ausführen"
        };

    private string RollButtonAriaLabel => AttributeMode ? "Eigenschaftsprobe ausführen" : RollButtonText;

    private CombatActionKind? GetActionKind() => SelectedAction switch
    {
        "attack" => CombatActionKind.MeleeAttack,
        "parry" => CombatActionKind.WeaponParry,
        "shield-parry" => CombatActionKind.ShieldParry,
        "dodge" => CombatActionKind.Dodge,
        "ranged" => CombatActionKind.RangedAttack,
        "damage" => CombatActionKind.Damage,
        "zone" => CombatActionKind.HitZone,
        "wound-helper" => CombatActionKind.WoundHelper,
        "fumble-helper" => CombatActionKind.FumbleHelper,
        _ => null
    };

    private async Task HandleWeaponChanged(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        if (!string.IsNullOrWhiteSpace(value))
        {
            await WeaponSelected.InvokeAsync(value);
        }
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

    public sealed record ActionOption(string Key, string Label, string Detail, bool IsAvailable);
}
