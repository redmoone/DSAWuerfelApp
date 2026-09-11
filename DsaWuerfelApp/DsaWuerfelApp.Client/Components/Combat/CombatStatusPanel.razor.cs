using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Client.Services;

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
    [Parameter] public int? CurrentAeP { get; set; }
    [Parameter] public int? CurrentKeP { get; set; }
    [Parameter] public int? CurrentInitiative { get; set; }
    [Parameter] public bool IsCombatStarted { get; set; }
    [Parameter] public bool CanStartCombat { get; set; }
    [Parameter] public EventCallback StartCombatRequested { get; set; }
    [Parameter] public EventCallback<CombatResourceKind> ResourceEditRequested { get; set; }
    [Parameter] public EventCallback InitiativeRequested { get; set; }
    [Parameter] public EventCallback WoundsRequested { get; set; }
    [Parameter] public bool CanUndo { get; set; }
    [Parameter] public EventCallback UndoRequested { get; set; }
    [Parameter] public string? PersistenceWarning { get; set; }
    [Parameter] public IReadOnlyDictionary<CombatWoundZone, int?> Wounds { get; set; } =
        new Dictionary<CombatWoundZone, int?>();
    [Parameter] public IReadOnlyList<string> Effects { get; set; } = Array.Empty<string>();

    private int TotalWounds => Wounds.Values.Where(value => value.HasValue).Sum(value => Math.Clamp(value!.Value, 0, 3));

    private static bool ShouldShowResource(CombatResourceKind resource, int? maximum) =>
        resource == CombatResourceKind.LeP ? !maximum.HasValue || maximum.Value > 0 : maximum is > 0;

    private static string FormatResource(int? current, int? maximum)
    {
        if (!current.HasValue)
        {
            return maximum.HasValue ? $"? / {maximum.Value}" : "?";
        }

        return maximum.HasValue ? $"{current.Value} / {maximum.Value}" : current.Value.ToString();
    }

    private static string GetBarWidth(int? current, int? maximum)
    {
        if (!current.HasValue || !maximum.HasValue || maximum.Value <= 0)
        {
            return "0%";
        }

        var ratio = Math.Clamp((double)current.Value / maximum.Value, 0d, 1d);
        return $"{ratio:P0}";
    }

    private static string FormatValue(int? value) => value?.ToString() ?? "—";

    private string GetArmorLabel() => SelectedSet?.ArmorZones is not null ? "Zonenrüstung" : "Einfache Rüstung";
}
