using DsaWuerfelApp.Client.Components.Combat;
using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Pages;

public partial class Kampf : IDisposable
{
    [Inject] public ActiveHeroState ActiveHeroState { get; set; } = null!;
    [Inject] public IHeroApiClient HeroApiClient { get; set; } = null!;
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
    private CombatProfileDto? _profile;
    private string? _profileError;
    private Guid? _profileHeroId;
    private bool _profileLoading;
    private long _profileLoadVersion;
    private CancellationTokenSource? _profileLoadCancellation;

    private Hero? ActiveHero => ActiveHeroState.CurrentHero;
    private CombatProfileDto? Profile => _profile;
    private IReadOnlyList<CombatSetVariantDto> Sets => _profile?.Sets ?? Array.Empty<CombatSetVariantDto>();
    private IReadOnlyList<CombatWeaponDto> Weapons => SelectedSet?.Weapons ?? Array.Empty<CombatWeaponDto>();
    private IReadOnlyList<CombatAttributeDto> Attributes => _profile?.Attributes ?? Array.Empty<CombatAttributeDto>();
    private IReadOnlyList<ProbeSearchEntryDto> Maneuvers => _profile?.Specializations
        .Select(specialization => new ProbeSearchEntryDto(
            specialization.IsLearned
                ? specialization.Name
                : $"{specialization.Name} (vergünstigt)",
            specialization.IsLearned ? specialization.Identifier ?? specialization.Name : null,
            specialization.IsLearned,
            specialization.IsLearned
                ? specialization.Categories
                    .Select(category => new ProbeSearchAlternativeDto(category, category))
                    .ToArray()
                : Array.Empty<ProbeSearchAlternativeDto>()))
        .ToArray() ?? Array.Empty<ProbeSearchEntryDto>();
    private IReadOnlyList<string> Effects => Array.Empty<string>();
    private CombatSetVariantDto? SelectedSet => Sets.FirstOrDefault(set => set.Id == _selectedSetId);
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
    private string ProfileStatus => ActiveHero is null
        ? "Aktiven Helden wählen"
        : _profileLoading
            ? "Kampfprofil wird geladen"
            : Profile is not null
                ? "Importierte Kampfwerte geladen"
                : "Kampfprofil fehlt";
    private string CombatInformationSummary => Profile is null
        ? _profileLoading
            ? "Die Kampfwerte werden aus dem gespeicherten Heldenimport gelesen."
            : "Wähle einen aktiven Helden, um Kampfwerte aus dem gespeicherten Import zu laden."
        : "Ausgewählte Kampfaktion und Quellwerte des Helden.";

    private IReadOnlyList<CombatActionPanel.ActionOption> Actions =>
    [
        new("attack", "Attacke", FormatActionValue("AT", SelectedWeapon?.Attack ?? SelectedSet?.Raufen?.Attack),
            SelectedWeapon?.Attack.HasValue == true || SelectedSet?.Raufen?.Attack.HasValue == true),
        new("parry", "Parade", FormatActionValue("PA", SelectedWeapon?.Parry ?? SelectedSet?.Raufen?.Parry),
            SelectedWeapon?.Parry.HasValue == true || SelectedSet?.Raufen?.Parry.HasValue == true),
        new("dodge", "Ausweichen", FormatActionValue("AW", SelectedSet?.Dodge), SelectedSet?.Dodge.HasValue == true),
        new("ranged", "Fernkampf", FormatActionValue("FK", RangedWeapon?.RangedValue), RangedWeapon?.RangedValue.HasValue == true)
    ];

    private CombatWeaponDto? SelectedWeapon => Weapons.FirstOrDefault(weapon => weapon.Id == _selectedWeaponId);
    private CombatWeaponDto? RangedWeapon => Weapons.FirstOrDefault(weapon => weapon.Category == CombatWeaponCategory.Ranged);
    private CombatSpecializationDto? SelectedSpecialization => Profile?.Specializations.FirstOrDefault(specialization =>
        string.Equals(specialization.Identifier, _selectedProbe, StringComparison.Ordinal) ||
        string.Equals(specialization.Name, _selectedProbe, StringComparison.Ordinal));

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
        ActiveHeroState.Changed += HandleActiveHeroChanged;
        SessionState.ActiveSessionChanged += HandleStateChanged;
        WuerfelState.Changed += HandleStateChanged;
        await ActiveHeroState.EnsureLoadedAsync();
        await LoadCombatProfileAsync(ActiveHeroState.CurrentHero);
        await WuerfelFacade.AttachAsync();
    }

    public void Dispose()
    {
        ActiveHeroState.Changed -= HandleStateChanged;
        ActiveHeroState.Changed -= HandleActiveHeroChanged;
        SessionState.ActiveSessionChanged -= HandleStateChanged;
        WuerfelState.Changed -= HandleStateChanged;
        _profileLoadCancellation?.Cancel();
        _profileLoadCancellation?.Dispose();
        WuerfelFacade.Detach();
    }

    private void HandleStateChanged() => _ = InvokeAsync(StateHasChanged);

    private void HandleActiveHeroChanged()
    {
        _ = InvokeAsync(async () =>
        {
            await LoadCombatProfileAsync(ActiveHeroState.CurrentHero);
            StateHasChanged();
        });
    }

    private Task HandleSetSelected(string setId)
    {
        _selectedSetId = setId;
        _selectedWeaponId = PreferredWeaponId(Sets.FirstOrDefault(set => set.Id == setId));
        _notice = null;
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

    private async Task LoadCombatProfileAsync(Hero? hero)
    {
        if (hero?.Id == _profileHeroId && (_profileLoading || _profile is not null))
        {
            return;
        }

        var version = ++_profileLoadVersion;
        _profileLoadCancellation?.Cancel();
        _profileLoadCancellation?.Dispose();
        _profileLoadCancellation = new CancellationTokenSource();
        _profileHeroId = hero?.Id;
        _profile = null;
        _profileError = null;
        _profileLoading = hero is not null;
        _selectedSetId = null;
        _selectedWeaponId = null;

        if (hero is null)
        {
            _profileLoading = false;
            return;
        }

        try
        {
            var profile = await HeroApiClient.GetCombatProfileAsync(hero.Id, _profileLoadCancellation.Token);
            if (version != _profileLoadVersion || ActiveHero?.Id != hero.Id)
            {
                return;
            }

            _profile = profile;
            var selectedSet = PreferredSet(profile.Sets);
            _selectedSetId = selectedSet?.Id;
            _selectedWeaponId = PreferredWeaponId(selectedSet);
        }
        catch (OperationCanceledException) when (_profileLoadCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (HttpRequestException exception)
        {
            if (version == _profileLoadVersion && ActiveHero?.Id == hero.Id)
            {
                _profileError = exception.Message.Trim('"');
            }
        }
        finally
        {
            if (version == _profileLoadVersion)
            {
                _profileLoading = false;
            }
        }
    }

    private static CombatSetVariantDto? PreferredSet(IReadOnlyList<CombatSetVariantDto> sets)
    {
        return sets
            .Where(set => set.IsInUse)
            .OrderByDescending(set => set.IsDefault)
            .ThenBy(set => set.Number)
            .ThenBy(set => set.ArmorModel)
            .FirstOrDefault()
            ?? sets.OrderBy(set => set.Number).ThenBy(set => set.ArmorModel).FirstOrDefault();
    }

    private static string? PreferredWeaponId(CombatSetVariantDto? set)
    {
        return set?.Weapons
            .OrderByDescending(weapon => weapon.IsAvailable == true)
            .ThenBy(weapon => weapon.Category)
            .ThenBy(weapon => weapon.Number)
            .Select(weapon => weapon.Id)
            .FirstOrDefault();
    }

    private static string FormatActionValue(string label, int? value) =>
        value.HasValue ? $"{label} {value.Value}" : $"{label} nicht verfügbar";

    private static string GetWeaponCategoryLabel(CombatWeaponCategory category) => category switch
    {
        CombatWeaponCategory.Melee => "Nahkampf",
        CombatWeaponCategory.Ranged => "Fernkampf",
        CombatWeaponCategory.Shield => "Abwehr",
        _ => "Waffenlos"
    };

    private static string GetWeaponPrimaryValues(CombatWeaponDto weapon) => weapon.Category switch
    {
        CombatWeaponCategory.Ranged => FormatActionValue("FK", weapon.RangedValue),
        CombatWeaponCategory.Shield => FormatActionValue("PA", weapon.Parry),
        _ => $"{FormatActionValue("AT", weapon.Attack)} · {FormatActionValue("PA", weapon.Parry)}"
    };

    private static string GetWeaponDamage(CombatWeaponDto weapon)
    {
        var baseDamage = weapon.BaseDamage ?? "TP nicht verfügbar";
        var calculatedDamage = weapon.CalculatedDamage is null ? null : $"inkl. {weapon.CalculatedDamage}";
        return calculatedDamage is null ? baseDamage : $"{baseDamage} · {calculatedDamage}";
    }

    private sealed record InitiativeEntry(string Name, int? Initiative);
}
