using DsaWuerfelApp.Client.Components.Combat;
using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Pages;

public partial class Kampf : IDisposable
{
    [Inject] public ActiveHeroState ActiveHeroState { get; set; } = null!;
    [Inject] public CombatState CombatState { get; set; } = null!;
    [Inject] public SessionState SessionState { get; set; } = null!;
    [Inject] public WuerfelState WuerfelState { get; set; } = null!;
    [Inject] public WuerfelFacade WuerfelFacade { get; set; } = null!;

    private string? _notice;
    private string _hitLePLossText = "0";
    private int _hitWounds;
    private CombatWoundZone? _hitZone;
    private string _hitNote = string.Empty;
    private string _initiativeText = string.Empty;
    private bool _initiativeDraftDirty;
    private bool _hitCaptureOpen;
    private bool _hitBusy;
    private string? _lastContextKey;

    private Hero? ActiveHero => ActiveHeroState.CurrentHero;
    private CombatProfileDto? Profile => CombatState.Profile;
    private bool ProfileLoading => CombatState.IsProfileLoading;
    private string? ProfileError => CombatState.ProfileError;
    private IReadOnlyList<CombatSetVariantDto> Sets => Profile?.Sets ?? Array.Empty<CombatSetVariantDto>();
    private IReadOnlyList<CombatWeaponDto> Weapons => CombatState.Weapons;
    private IReadOnlyList<CombatAttributeDto> Attributes => Profile?.Attributes ?? Array.Empty<CombatAttributeDto>();
    private IReadOnlyList<ProbeSearchEntryDto> Maneuvers => Profile?.Specializations
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
    private IReadOnlyList<string> Effects => CombatState.Effects;
    private CombatSetVariantDto? SelectedSet => CombatState.SelectedSet;
    private CombatWeaponDto? SelectedWeapon => CombatState.SelectedWeapon;
    private CombatWeaponDto? RangedWeapon => Weapons.FirstOrDefault(weapon => weapon.Category == CombatWeaponCategory.Ranged);
    private CombatSpecializationDto? SelectedSpecialization => Profile?.Specializations.FirstOrDefault(specialization =>
        string.Equals(specialization.Identifier, CombatState.SelectedProbe, StringComparison.Ordinal) ||
        string.Equals(specialization.Name, CombatState.SelectedProbe, StringComparison.Ordinal));
    private int? CurrentLeP => CombatState.CurrentLeP;
    private int? CurrentAuP => CombatState.CurrentAuP;
    private int? CurrentInitiative => CombatState.CurrentInitiative;
    private IReadOnlyDictionary<CombatWoundZone, int?> Wounds => CombatState.Wounds;
    private bool HasCombatContext => ActiveHero is not null && Profile is not null;

    private string? SelectedSetId => CombatState.SelectedSetId;
    private string? SelectedWeaponId => CombatState.SelectedWeaponId;
    private string SelectedAction => CombatState.SelectedAction;
    private string SelectedProbe => CombatState.SelectedProbe;
    private string? SelectedAttribute => CombatState.SelectedAttribute;
    private int Modifier => CombatState.Modifier;
    private string RollText => CombatState.RollText;
    private CombatFacing Facing => CombatState.Facing;
    private CombatWoundZone? SelectedZone => CombatState.SelectedZone;

    private string HeroDisplayName => ActiveHero?.Name ?? "Kein aktiver Held";
    private string ProfileStatus => ActiveHero is null
        ? "Aktiven Helden wählen"
        : ProfileLoading
            ? "Kampfprofil wird geladen"
            : Profile is not null
                ? "Importierte Kampfwerte geladen"
                : "Kampfprofil fehlt";
    private string CombatInformationSummary => Profile is null
        ? ProfileLoading
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

    private string SelectionSummary
    {
        get
        {
            var action = Actions.FirstOrDefault(item => item.Key == SelectedAction);
            var actionText = action?.Label ?? "Keine Aktion";
            return SelectedWeapon is null
                ? actionText
                : $"{SelectedWeapon.Name} · {actionText} · {GetWeaponPrimaryValues(SelectedWeapon)}";
        }
    }

    private IReadOnlyList<InitiativeEntry> InitiativeEntries =>
        SessionState.ActiveSession?.Players
            .Select(player => new InitiativeEntry(player.Name, null))
            .Prepend(new InitiativeEntry(HeroDisplayName, CurrentInitiative))
            .ToArray() ??
        (ActiveHero is null ? Array.Empty<InitiativeEntry>() : [new InitiativeEntry(HeroDisplayName, CurrentInitiative)]);

    private int? CurrentHitWounds => _hitZone.HasValue && Wounds.TryGetValue(_hitZone.Value, out var wounds)
        ? wounds
        : null;

    private int? HitPreviewLeP
    {
        get
        {
            if (!CurrentLeP.HasValue || !int.TryParse(_hitLePLossText, out var loss))
            {
                return CurrentLeP;
            }

            return Math.Max(0, CurrentLeP.Value - Math.Max(0, loss));
        }
    }

    private bool CanApplyHit => CombatState.IsStarted &&
                                 CurrentLeP.HasValue &&
                                 _hitZone.HasValue &&
                                 int.TryParse(_hitLePLossText, out var loss) &&
                                 loss >= 0;

    protected override async Task OnInitializedAsync()
    {
        CombatState.Changed += HandleCombatStateChanged;
        SessionState.ActiveSessionChanged += HandleStateChanged;
        WuerfelState.Changed += HandleStateChanged;
        await CombatState.EnsureLoadedAsync();
        _lastContextKey = CombatState.ContextKey;
        SyncInitiativeText();
        await WuerfelFacade.AttachAsync();
    }

    public void Dispose()
    {
        CombatState.Changed -= HandleCombatStateChanged;
        SessionState.ActiveSessionChanged -= HandleStateChanged;
        WuerfelState.Changed -= HandleStateChanged;
        WuerfelFacade.Detach();
    }

    private void HandleStateChanged() => _ = InvokeAsync(StateHasChanged);

    private void HandleCombatStateChanged()
    {
        if (!string.Equals(_lastContextKey, CombatState.ContextKey, StringComparison.Ordinal))
        {
            _lastContextKey = CombatState.ContextKey;
            CloseHitCapture();
            _notice = null;
            _initiativeDraftDirty = false;
        }

        if (!_initiativeDraftDirty)
        {
            SyncInitiativeText();
        }

        _ = InvokeAsync(StateHasChanged);
    }

    private Task HandleSetSelected(string setId) => CombatState.SetSelectedSetAsync(setId);

    private Task HandleWeaponSelected(string weaponId) => CombatState.SetSelectedWeaponAsync(weaponId);

    private Task HandleActionSelected(string action) => CombatState.SetSelectedActionAsync(action);

    private Task HandleProbeSelected(string probe) => CombatState.SetSelectedProbeAsync(probe);

    private Task HandleAttributeSelected(string attribute) => CombatState.SetSelectedAttributeAsync(attribute);

    private Task HandleModifierChanged(int modifier) => CombatState.SetModifierAsync(modifier);

    private Task HandleRollTextChanged(string text) => CombatState.SetRollTextAsync(text);

    private Task ResetAction() => CombatState.ResetActionAsync();

    private Task HandleRollRequested()
    {
        _notice = "Kampfwürfe noch nicht angebunden.";
        return Task.CompletedTask;
    }

    private Task HandleFacingChanged(CombatFacing facing) => CombatState.SetFacingAsync(facing);

    private Task HandleZoneSelected(CombatWoundZone zone) => CombatState.SetSelectedZoneAsync(zone);

    private Task HandleHistorySelected(RollHistoryEntryDto entry)
    {
        _notice = $"Historieneintrag {entry.Timestamp.ToLocalTime():HH:mm} ausgewählt.";
        return Task.CompletedTask;
    }

    private async Task StartCombatAsync()
    {
        if (await CombatState.StartCombatAsync())
        {
            _notice = "Kampfzustand gestartet: volle importierte Ressourcen, keine Wunden.";
        }
    }

    private async Task UndoLastChangeAsync()
    {
        _notice = await CombatState.UndoLastChangeAsync()
            ? "Letzte Änderung wurde rückgängig gemacht."
            : "Keine Änderung zum Rückgängigmachen vorhanden.";
    }

    private void OpenHitCapture()
    {
        if (Profile is null)
        {
            _notice = "Treffererfassung ist erst mit einem importierten Kampfprofil verfügbar.";
            return;
        }

        if (!CombatState.IsStarted)
        {
            _notice = "Starte zuerst den laufenden Kampfzustand mit vollen Ressourcen.";
            return;
        }

        _hitZone = SelectedZone ?? CombatWoundZone.Torso;
        _hitWounds = CurrentHitWounds ?? 0;
        _hitLePLossText = "0";
        _hitNote = string.Empty;
        _hitCaptureOpen = true;
        _notice = null;
    }

    private void CloseHitCapture()
    {
        _hitCaptureOpen = false;
        _hitBusy = false;
        _hitZone = null;
        _hitNote = string.Empty;
        _hitLePLossText = "0";
        _hitWounds = 0;
    }

    private Task HandleHitZoneChanged(CombatWoundZone? zone)
    {
        _hitZone = zone;
        _hitWounds = zone.HasValue && Wounds.TryGetValue(zone.Value, out var wounds) && wounds.HasValue
            ? wounds.Value
            : 0;
        return Task.CompletedTask;
    }

    private Task HandleHitLePLossChanged(string value)
    {
        _hitLePLossText = value;
        return Task.CompletedTask;
    }

    private Task HandleHitWoundsChanged(int value)
    {
        _hitWounds = Math.Clamp(value, 0, 3);
        return Task.CompletedTask;
    }

    private Task HandleHitNoteChanged(string value)
    {
        _hitNote = value;
        return Task.CompletedTask;
    }

    private async Task ApplyHitAsync()
    {
        if (!CanApplyHit || !_hitZone.HasValue || !int.TryParse(_hitLePLossText, out var loss))
        {
            _notice = "Bitte einen gültigen LeP-Verlust und eine Trefferzone angeben.";
            return;
        }

        _hitBusy = true;
        try
        {
            if (await CombatState.ApplyHitAsync(_hitZone.Value, loss, _hitWounds, _hitNote))
            {
                CloseHitCapture();
                _notice = "Treffer wurde manuell erfasst.";
            }
            else
            {
                _notice = "Der laufende Kampfzustand ist noch nicht verfügbar.";
            }
        }
        finally
        {
            _hitBusy = false;
        }
    }

    private Task HandleInitiativeTextChanged(string value)
    {
        _initiativeDraftDirty = true;
        _initiativeText = value;
        return Task.CompletedTask;
    }

    private async Task ApplyInitiativeAsync()
    {
        if (string.IsNullOrWhiteSpace(_initiativeText))
        {
            if (await CombatState.SetInitiativeAsync(null))
            {
                _initiativeDraftDirty = false;
                _notice = "Laufende Initiative entfernt.";
            }

            return;
        }

        if (!int.TryParse(_initiativeText, out var initiative))
        {
            _notice = "Initiative muss eine ganze Zahl sein.";
            return;
        }

        if (await CombatState.SetInitiativeAsync(initiative))
        {
            _initiativeDraftDirty = false;
            _notice = "Laufende Initiative gespeichert.";
        }
        else
        {
            _notice = "Starte zuerst den laufenden Kampfzustand.";
        }
    }

    private void SyncInitiativeText() => _initiativeText = CurrentInitiative?.ToString() ?? string.Empty;

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
