using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Pages;

public partial class Kampf : IDisposable
{
    [Inject] public ActiveHeroState ActiveHeroState { get; set; } = null!;
    [Inject] public SessionState SessionState { get; set; } = null!;
    [Inject] public WuerfelState WuerfelState { get; set; } = null!;
    [Inject] public WuerfelFacade WuerfelFacade { get; set; } = null!;

    private readonly Dictionary<CombatWoundZone, int?> _wounds = Enum
        .GetValues<CombatWoundZone>()
        .ToDictionary(zone => zone, _ => (int?)null);

    private CombatFacing _facing = CombatFacing.Front;
    private CombatWoundZone? _selectedZone;
    private string? _selectedSetId;
    private string? _selectedWeaponId;
    private string _selectedAction = "attack";
    private string _selectedProbe = string.Empty;
    private string? _selectedAttribute;
    private int _modifier;
    private string _rollText = string.Empty;
    private string? _notice;

    private Hero? ActiveHero => ActiveHeroState.CurrentHero;
    private CombatProfileDto? Profile => null;
    private IReadOnlyList<CombatSetVariantDto> Sets => Array.Empty<CombatSetVariantDto>();
    private IReadOnlyList<CombatWeaponDto> Weapons => Array.Empty<CombatWeaponDto>();
    private IReadOnlyList<CombatAttributeDto> Attributes => Array.Empty<CombatAttributeDto>();
    private IReadOnlyList<ProbeSearchEntryDto> Maneuvers => Array.Empty<ProbeSearchEntryDto>();
    private IReadOnlyList<string> Effects => Array.Empty<string>();
    private CombatSetVariantDto? SelectedSet => null;
    private int? CurrentLeP => null;
    private int? CurrentAuP => null;
    private int? CurrentInitiative => null;
    private IReadOnlyDictionary<CombatWoundZone, int?> Wounds => _wounds;
    private bool HasCombatContext => ActiveHero is not null && Profile is not null;

    private string? SelectedSetId => _selectedSetId;
    private string? SelectedWeaponId => _selectedWeaponId;
    private string SelectedAction => _selectedAction;
    private string SelectedProbe => _selectedProbe;
    private string? SelectedAttribute => _selectedAttribute;
    private int Modifier => _modifier;
    private string RollText => _rollText;
    private CombatFacing Facing => _facing;
    private CombatWoundZone? SelectedZone => _selectedZone;

    private string HeroDisplayName => ActiveHero?.Name ?? "Kein aktiver Held";
    private string ProfileStatus => ActiveHero is null ? "Aktiven Helden wählen" : "Kampfprofil wird angeschlossen";
    private string CombatInformationSummary => Profile is null
        ? "Wähle einen aktiven Helden, um Kampfwerte aus dem gespeicherten Import zu laden."
        : "Ausgewählte Kampfaktion und Quellwerte des Helden.";

    private string SelectionSummary => _selectedAction switch
    {
        "parry" => "Parade",
        "dodge" => "Ausweichen",
        "ranged" => "Fernkampf",
        _ => "Attacke"
    };

    private IReadOnlyList<InitiativeEntry> InitiativeEntries =>
        SessionState.ActiveSession?.Players
            .Select(player => new InitiativeEntry(player.Name, null))
            .Prepend(new InitiativeEntry(HeroDisplayName, CurrentInitiative))
            .ToArray() ??
        (ActiveHero is null ? Array.Empty<InitiativeEntry>() : [new InitiativeEntry(HeroDisplayName, CurrentInitiative)]);

    protected override async Task OnInitializedAsync()
    {
        ActiveHeroState.Changed += HandleStateChanged;
        SessionState.ActiveSessionChanged += HandleStateChanged;
        WuerfelState.Changed += HandleStateChanged;
        await ActiveHeroState.EnsureLoadedAsync();
        await WuerfelFacade.AttachAsync();
    }

    public void Dispose()
    {
        ActiveHeroState.Changed -= HandleStateChanged;
        SessionState.ActiveSessionChanged -= HandleStateChanged;
        WuerfelState.Changed -= HandleStateChanged;
        WuerfelFacade.Detach();
    }

    private void HandleStateChanged() => _ = InvokeAsync(StateHasChanged);

    private Task HandleSetSelected(string setId)
    {
        _selectedSetId = setId;
        _notice = "Kampfsetauswahl wird mit dem Kampfprofil verfügbar.";
        return Task.CompletedTask;
    }

    private Task HandleWeaponSelected(string weaponId)
    {
        _selectedWeaponId = weaponId;
        return Task.CompletedTask;
    }

    private Task HandleActionSelected(string action)
    {
        _selectedAction = action;
        return Task.CompletedTask;
    }

    private Task HandleProbeSelected(string probe)
    {
        _selectedProbe = probe;
        return Task.CompletedTask;
    }

    private Task HandleAttributeSelected(string attribute)
    {
        _selectedAttribute = attribute;
        return Task.CompletedTask;
    }

    private Task HandleModifierChanged(int modifier)
    {
        _modifier = modifier;
        return Task.CompletedTask;
    }

    private Task HandleRollTextChanged(string text)
    {
        _rollText = text;
        return Task.CompletedTask;
    }

    private Task ResetAction()
    {
        _modifier = 0;
        _rollText = string.Empty;
        _notice = null;
        return Task.CompletedTask;
    }

    private Task HandleRollRequested()
    {
        _notice = "Kampfwürfe noch nicht angebunden.";
        return Task.CompletedTask;
    }

    private Task HandleFacingChanged(CombatFacing facing)
    {
        _facing = facing;
        return Task.CompletedTask;
    }

    private Task HandleZoneSelected(CombatWoundZone zone)
    {
        _selectedZone = zone;
        return Task.CompletedTask;
    }

    private Task HandleHistorySelected(RollHistoryEntryDto entry)
    {
        _notice = $"Historieneintrag {entry.Timestamp.ToLocalTime():HH:mm} ausgewählt.";
        return Task.CompletedTask;
    }

    private sealed record InitiativeEntry(string Name, int? Initiative);
}
