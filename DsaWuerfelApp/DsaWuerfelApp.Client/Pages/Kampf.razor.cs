using System.Globalization;

using DsaWuerfelApp.Client.Components.Combat;
using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Pages;

public partial class Kampf : IDisposable
{
    [Inject] public ActiveHeroState ActiveHeroState { get; set; } = null!;
    [Inject] public AuthState AuthState { get; set; } = null!;
    [Inject] public CombatCoordinator CombatCoordinator { get; set; } = null!;
    [Inject] public CombatState CombatState { get; set; } = null!;
    [Inject] public CombatSessionState CombatSessionState { get; set; } = null!;
    [Inject] public SessionState SessionState { get; set; } = null!;
    [Inject] public WuerfelState WuerfelState { get; set; } = null!;
    [Inject] public WuerfelFacade WuerfelFacade { get; set; } = null!;

    private string? _notice;
    private string? _lastContextKey;
    private bool _rollBusy;
    private bool _valueMutationBusy;
    private bool _attributeMode;
    private CombatArea _activeArea = CombatArea.Kampf;
    private CombatDrawer _drawer;
    private int? _orientationReliefDraft;
    private bool _orientationUninterruptedDraft = true;
    private string? _initiativeParticipantId;
    private Guid? _initiativeHeroId;
    private string _opponentNameDraft = string.Empty;
    private string _opponentAffiliationDraft = "Gegner";
    private int? _opponentInitiativeBaseDraft;
    private int? _opponentInitiativeDraft;
    private string _announcementDraft = string.Empty;
    private CombatParticipantDrawerMode _participantDrawerMode;
    private RollHistoryEntryDto? _selectedHistoryEntry;

    private Hero? ActiveHero => ActiveHeroState.CurrentHero;
    private CombatProfileDto? Profile => CombatState.Profile;
    private bool ProfileLoading => CombatState.IsProfileLoading;
    private string? ProfileError => CombatState.ProfileError;
    private IReadOnlyList<CombatSetVariantDto> Sets => Profile?.Sets ?? Array.Empty<CombatSetVariantDto>();
    private IReadOnlyList<CombatWeaponDto> Weapons => CombatState.Weapons;
    private IReadOnlyList<CombatAttributeDto> Attributes => Profile?.Attributes ?? Array.Empty<CombatAttributeDto>();
    private IReadOnlyList<string> SelectedAttributes => CombatState.SelectedAttributes;
    private IReadOnlyList<ProbeSearchEntryDto> Maneuvers => Profile?.Specializations
        .Select(specialization => new ProbeSearchEntryDto(
            specialization.IsLearned ? specialization.Name : $"{specialization.Name} (vergünstigt)",
            specialization.IsLearned ? specialization.Identifier ?? specialization.Name : null,
            specialization.IsLearned,
            specialization.IsLearned
                ? specialization.Categories.Select(category => new ProbeSearchAlternativeDto(category, category)).ToArray()
                : Array.Empty<ProbeSearchAlternativeDto>()))
        .ToArray() ?? Array.Empty<ProbeSearchEntryDto>();
    private IReadOnlyList<string> Effects => CombatState.Effects;
    private CombatSetVariantDto? SelectedSet => CombatState.SelectedSet;
    private CombatWeaponDto? SelectedWeapon => CombatState.SelectedWeapon;
    private int? CurrentLeP => CombatState.CurrentLeP;
    private int? CurrentAuP => CombatState.CurrentAuP;
    private int? CurrentAeP => CombatState.CurrentAeP;
    private int? CurrentKeP => CombatState.CurrentKeP;
    private CombatSessionSnapshotDto? SessionCombat => CombatSessionState.Current;
    private CombatSessionParticipantDto? OwnSessionParticipant => SessionCombat?.Participants
        .FirstOrDefault(participant => participant.HeroId == ActiveHero?.Id);
    private int? CurrentInitiative => OwnSessionParticipant?.CurrentInitiative ?? CombatState.CurrentInitiative;
    private IReadOnlyDictionary<CombatWoundZone, int?> Wounds => CombatState.Wounds;
    private bool HasCombatContext => ActiveHero is not null && Profile is not null && SelectedSet is not null;
    private bool IsSessionCombat => !string.IsNullOrWhiteSpace(SessionState.ActiveSessionId);
    private bool IsCombatStarted => IsSessionCombat ? SessionCombat?.IsStarted == true : CombatState.IsStarted;
    private bool CanUndoCombat => IsSessionCombat ? CombatSessionState.CanUndo : CombatState.CanUndo;
    private bool IsSessionMaster => IsSessionCombat &&
                                    string.Equals(SessionState.ActiveSession?.MasterUserId,
                                        AuthState.Current.User?.Id, StringComparison.Ordinal);

    private string? SelectedSetId => CombatState.SelectedSetId;
    private string? SelectedWeaponId => CombatState.SelectedWeaponId;
    private string SelectedAction => CombatState.SelectedAction;
    private string SelectedProbe => CombatState.SelectedProbe;
    private int Modifier => CombatState.Modifier;
    private string RollText => CombatState.RollText;
    private CombatFacing Facing => CombatState.Facing;
    private CombatWoundZone? SelectedZone => CombatState.SelectedZone;
    private IReadOnlyList<RollHistoryEntryDto> History => WuerfelState.Current.History;
    private CombatRollResultDto? CombatResult => WuerfelState.Current.LastCombatRoll;
    private AttributeRollResultDto? AttributeResult => WuerfelState.Current.LastAttributeRoll;
    private IReadOnlyList<int> ResultDiceSides => IsAttributeMode
        ? WuerfelState.Current.AnimatedDiceSides
        : CombatResult?.Rolls.Select(roll => roll.Sides).ToArray() ?? Array.Empty<int>();
    private IReadOnlyList<int> ResultDiceValues => IsAttributeMode
        ? WuerfelState.Current.AnimatedDiceValues
        : CombatResult?.Rolls.Select(roll => roll.Value).ToArray() ?? Array.Empty<int>();
    private long ResultVersion => WuerfelState.Current.ResultVersion;
    private CombatArea ActiveArea => _activeArea;
    private bool IsAttributeMode => _attributeMode;
    private bool HasAttention => Profile?.HasAttention == true;

    private string HeroDisplayName => ActiveHero?.Name ?? "Kein aktiver Held";
    private string ProfileStatus => ActiveHero is null
        ? "Aktiven Helden wählen"
        : ProfileLoading
            ? "Kampfprofil wird geladen"
            : Profile is not null
                ? "Importierte Kampfwerte geladen"
                : "Kampfprofil fehlt";

    private IReadOnlyList<CombatActionPanel.ActionOption> Actions =>
    [
        new("attack", "Attacke", FormatActionValue("AT", SelectedWeapon?.Attack), HasWeaponAttack),
        new("parry", "Waffenparade", FormatActionValue("PA", SelectedWeapon?.Parry), HasWeaponParry),
        new("shield-parry", "Schildparade", FormatActionValue("PA", SelectedWeapon?.Parry), HasShieldParry),
        new("dodge", "Ausweichen", FormatActionValue("AW", SelectedSet?.Dodge), SelectedSet?.Dodge.HasValue == true),
        new("ranged", "Fernkampf", FormatActionValue("FK", SelectedWeapon?.RangedValue), HasRangedValue),
        new("damage", "Trefferpunkte", $"TP {GetDamageText(SelectedWeapon)}", HasDamage),
        new("zone", "Trefferzone", "W20", HasCombatContext),
        new("wound-helper", "Wund-Hilfswurf", "W6", HasCombatContext),
        new("fumble-helper", "Patzer-Hilfswurf", "W20", HasCombatContext)
    ];

    private bool HasWeaponAttack => SelectedWeapon is { Category: CombatWeaponCategory.Melee or CombatWeaponCategory.Unarmed } && SelectedWeapon.Attack.HasValue;
    private bool HasWeaponParry => SelectedWeapon is { Category: CombatWeaponCategory.Melee or CombatWeaponCategory.Unarmed } && SelectedWeapon.Parry.HasValue;
    private bool HasShieldParry => SelectedWeapon is { Category: CombatWeaponCategory.Shield } && SelectedWeapon.Parry.HasValue;
    private bool HasRangedValue => SelectedWeapon is { Category: CombatWeaponCategory.Ranged } && SelectedWeapon.RangedValue.HasValue;
    private bool HasDamage => SelectedWeapon is not null && !string.IsNullOrWhiteSpace(SelectedWeapon.CalculatedDamage ?? SelectedWeapon.BaseDamage);

    private int? EffectiveTarget => CombatRollRules.ResolveEffectiveTarget(GetActionBaseValue(), GetCurrentModifiers());

    private string TargetSource
    {
        get
        {
            var source = GetActionKind() switch
            {
                CombatActionKind.Damage => "TP werden separat aus der importierten Formel berechnet.",
                CombatActionKind.MeleeAttack or CombatActionKind.WeaponParry or CombatActionKind.ShieldParry or CombatActionKind.Dodge or CombatActionKind.RangedAttack => "Importierter Zielwert",
                _ => "Hilfswurf ohne Zielwert"
            };
            var automatic = GetAutomaticModifiers();
            return automatic.Count == 0
                ? source
                : $"{source}; automatisch {string.Join(", ", automatic.Select(FormatModifier))}";
        }
    }

    private bool CanRoll => IsAttributeMode
        ? HasCombatContext && !_rollBusy && !_valueMutationBusy && SelectedAttributes.Count > 0
        : HasCombatContext && !_rollBusy && !_valueMutationBusy &&
          Actions.FirstOrDefault(action => action.Key == SelectedAction)?.IsAvailable == true;

    private bool CanRollInitiative => HasCombatContext &&
                                      SelectedSet?.Initiative.HasValue == true &&
                                      !_rollBusy &&
                                      !_valueMutationBusy;

    private string? InitiativeDisabledReason
    {
        get
        {
            if (ProfileLoading)
            {
                return "Kampfprofil wird geladen.";
            }

            if (!HasCombatContext)
            {
                return "Zuerst einen aktiven Helden und ein Kampfset laden.";
            }

            if (SelectedSet?.Initiative.HasValue != true)
            {
                return "Für das Set ist kein INI-Basiswert importiert.";
            }

            return _rollBusy ? "Wurf läuft." : null;
        }
    }

    private bool CanConsumeReaction => IsSessionCombat &&
                                       OwnSessionParticipant?.ReactionAvailable == true &&
                                       CombatResult?.Snapshot.Action is CombatActionKind.WeaponParry or CombatActionKind.ShieldParry or CombatActionKind.Dodge;

    private string DrawerTitle => _drawer switch
    {
        CombatDrawer.Orientation => "Orientieren",
        CombatDrawer.Participant => _participantDrawerMode == CombatParticipantDrawerMode.Opponent
            ? "Gegner hinzufügen"
            : "Rundenansage",
        CombatDrawer.History => "Vollständige Würfelhistorie",
        _ => "Details"
    };

    private IReadOnlyList<CombatSessionParticipantDto> InitiativeParticipants =>
        SessionCombat?.Participants
            .OrderByDescending(participant => participant.CurrentInitiative.HasValue)
            .ThenByDescending(participant => participant.CurrentInitiative ?? int.MinValue)
            .ThenByDescending(participant => participant.InitiativeBase ?? int.MinValue)
            .ThenBy(participant => participant.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<CombatSessionParticipantDto>();

    private IReadOnlyList<CombatSessionActionDto> CurrentSessionActions =>
        SessionCombat?.Actions
            .Where(action => action.Round == (SessionCombat?.Round ?? 1) && action.State != CombatActionEntryState.Completed)
            .ToArray() ?? Array.Empty<CombatSessionActionDto>();

    private IReadOnlySet<string> NextInitiativeParticipantIds
    {
        get
        {
            var snapshot = SessionCombat;
            if (snapshot is null || !snapshot.CurrentActionIds.Any())
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var currentActionIds = snapshot.CurrentActionIds.ToHashSet(StringComparer.Ordinal);
            var nextActions = snapshot.Actions
                .Where(action => action.Round == snapshot.Round &&
                                 !action.IsReaction &&
                                 action.State == CombatActionEntryState.Open &&
                                 !currentActionIds.Contains(action.Id))
                .ToArray();
            if (nextActions.Length == 0)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var nextInitiative = nextActions.Max(action => GetActionInitiative(action, snapshot));
            return nextActions
                .Where(action => GetActionInitiative(action, snapshot) == nextInitiative)
                .Select(action => action.ParticipantId)
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    protected override async Task OnInitializedAsync()
    {
        CombatState.Changed += HandleCombatStateChanged;
        CombatSessionState.Changed += HandleStateChanged;
        SessionState.ActiveSessionChanged += HandleStateChanged;
        WuerfelState.Changed += HandleStateChanged;
        CombatCoordinator.Attach();
        await CombatState.EnsureLoadedAsync();
        await CombatSessionState.EnsureLoadedAsync();
        _lastContextKey = CombatState.ContextKey;
        await WuerfelFacade.AttachAsync();
    }

    public void Dispose()
    {
        CombatState.Changed -= HandleCombatStateChanged;
        CombatSessionState.Changed -= HandleStateChanged;
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
            CloseDrawer();
            _selectedHistoryEntry = null;
            _notice = null;
            _activeArea = CombatArea.Kampf;
        }

        _ = InvokeAsync(StateHasChanged);
    }

    private async Task HandleSetSelected(string setId)
    {
        await CombatState.SetSelectedSetAsync(setId);
        if (SelectedWeapon?.Category == CombatWeaponCategory.Ranged)
        {
            await CombatState.SetSelectedActionAsync("ranged");
        }
        else if (SelectedWeapon?.Category == CombatWeaponCategory.Shield)
        {
            await CombatState.SetSelectedActionAsync("shield-parry");
        }
        else if (SelectedAction is "ranged" or "shield-parry")
        {
            await CombatState.SetSelectedActionAsync("attack");
        }

        await SyncSessionRuntimeStateAsync(setId);
    }

    private async Task HandleWeaponSelected(string weaponId)
    {
        await CombatState.SetSelectedWeaponAsync(weaponId);
        if (SelectedWeapon?.Category == CombatWeaponCategory.Ranged)
        {
            await CombatState.SetSelectedActionAsync("ranged");
        }
        else if (SelectedWeapon?.Category == CombatWeaponCategory.Shield)
        {
            await CombatState.SetSelectedActionAsync("shield-parry");
        }
        else if (SelectedAction is "ranged" or "shield-parry")
        {
            await CombatState.SetSelectedActionAsync("attack");
        }
    }

    private Task HandleActionSelected(string action) => CombatState.SetSelectedActionAsync(action);

    private Task HandleAttributeModeChanged(bool attributeMode)
    {
        _attributeMode = attributeMode;
        return Task.CompletedTask;
    }

    private Task HandleProbeSelected(string probe) => CombatState.SetSelectedProbeAsync(probe);

    private Task HandleAttributeSelected(string attribute) => CombatState.AddSelectedAttributeAsync(attribute);

    private Task HandleAttributeRemoved(int index) => CombatState.RemoveSelectedAttributeAtAsync(index);

    private Task HandleModifierChanged(int modifier) => CombatState.SetModifierAsync(modifier);

    private Task HandleRollTextChanged(string text) => CombatState.SetRollTextAsync(text);

    private Task ResetAction() => CombatState.SetModifierAsync(0);

    private async Task HandleRollRequested()
    {
        if (IsAttributeMode)
        {
            await HandleAttributeRollRequested();
            return;
        }

        if (!CanRoll || GetActionKind() is not { } action)
        {
            _notice = "Bitte zuerst ein importiertes Set, eine passende Waffe und eine verfügbare Aktion wählen.";
            return;
        }

        _rollBusy = true;
        _notice = null;
        try
        {
            await CombatCoordinator.RollAsync(BuildRollRequest(action));
        }
        catch (Exception exception)
        {
            _notice = exception.Message;
        }
        finally
        {
            _rollBusy = false;
        }
    }

    private async Task HandleAttributeRollRequested()
    {
        if (!CanRoll)
        {
            _notice = "Bitte mindestens eine Eigenschaft auswählen.";
            return;
        }

        _rollBusy = true;
        _notice = null;
        try
        {
            await WuerfelFacade.RollAttributesAsync(SelectedAttributes, Modifier, ActiveHero?.Id);
        }
        catch (Exception exception)
        {
            _notice = exception.Message;
        }
        finally
        {
            _rollBusy = false;
        }
    }

    private CombatRollRequestDto BuildRollRequest(CombatActionKind action)
    {
        var modifiers = action == CombatActionKind.Damage || Modifier == 0
            ? Array.Empty<CombatModifierDto>()
            : [new CombatModifierDto("Situativ", Modifier, "Kampfseite")];
        return new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = SessionState.ActiveSessionId,
            HeroId = ActiveHero?.Id,
            SetId = SelectedSet?.Id,
            Action = action,
            WeaponId = action is CombatActionKind.Dodge or CombatActionKind.HitZone or CombatActionKind.InitiativeHelper or CombatActionKind.WoundHelper or CombatActionKind.FumbleHelper
                ? null
                : SelectedWeapon?.Id,
            RuntimeState = BuildRuntimeState(),
            Modifiers = modifiers,
            Options = new CombatRuleOptionsDto(SpecialResultsEnabled: true, LowLePEnabled: true),
            Damage = action == CombatActionKind.Damage
                ? new CombatDamageRollRequestDto { IsCritical = IsLastCriticalAttack() }
                : null,
            DamageModifier = action == CombatActionKind.Damage ? Modifier : 0,
            Zone = action == CombatActionKind.HitZone
                ? new CombatZoneRollRequestDto(Facing, CombatArmorZone.LeftArm, CombatArmorZone.RightArm)
                : null,
            Helper = action switch
            {
                CombatActionKind.WoundHelper => new CombatHelperRollRequestDto { DiceCount = 1, DiceSides = 6, Purpose = "Wund-Hilfswurf" },
                CombatActionKind.FumbleHelper => new CombatHelperRollRequestDto { DiceCount = 1, DiceSides = 20, Purpose = "Patzer-Hilfswurf" },
                _ => null
            },
            Note = string.IsNullOrWhiteSpace(RollText) ? null : RollText.Trim()
        };
    }

    private Task HandleFacingChanged(CombatFacing facing) => CombatState.SetFacingAsync(facing);

    private async Task HandleZoneSelected(CombatWoundZone zone)
    {
        await CombatState.SetSelectedZoneAsync(zone);
    }

    private void SetArea(CombatArea area)
    {
        _activeArea = area;
        if (area is CombatArea.Kampf or CombatArea.Eigenschaften)
        {
            _attributeMode = area == CombatArea.Eigenschaften;
        }
    }

    private async Task HandleResourceValueChanged(CombatStatusPanel.ResourceValueChange change)
    {
        if (_valueMutationBusy || GetResourceValue(change.Resource) == change.Value)
        {
            return;
        }

        _valueMutationBusy = true;
        _notice = null;
        try
        {
            await CombatState.SetResourceAsync(change.Resource, change.Value);
            var result = await SyncSessionRuntimeStateAsync();
            if (result?.Stale == true)
            {
                _notice = result.Message;
            }
        }
        catch (Exception exception)
        {
            _notice = exception.Message;
        }
        finally
        {
            _valueMutationBusy = false;
        }
    }

    private async Task HandleRelativeResourceChanged(CombatStatusPanel.ResourceAdjustment change)
    {
        var current = GetResourceValue(change.Resource);
        if (!current.HasValue || change.Amount <= 0)
        {
            _notice = "Für die relative Änderung muss zuerst ein aktueller Wert erfasst sein.";
            return;
        }

        var next = change.Increase
            ? current.Value + change.Amount
            : current.Value - change.Amount;
        if (GetResourceMaximum(change.Resource) is { } maximum)
        {
            next = Math.Min(maximum, next);
        }

        await HandleResourceValueChanged(new CombatStatusPanel.ResourceValueChange(change.Resource, next));
    }

    private async Task HandleWoundChanged(CombatBodyPanel.WoundChange change)
    {
        if (!change.Value.HasValue || _valueMutationBusy)
        {
            if (!change.Value.HasValue)
            {
                _notice = "Bitte einen Wundstand von 0 bis 3 setzen.";
            }

            return;
        }

        _valueMutationBusy = true;
        _notice = null;
        try
        {
            await CombatState.SetWoundAsync(change.Zone, Math.Clamp(change.Value.Value, 0, 3));
            var result = await SyncSessionRuntimeStateAsync();
            if (result?.Stale == true)
            {
                _notice = result.Message;
            }
        }
        catch (Exception exception)
        {
            _notice = exception.Message;
        }
        finally
        {
            _valueMutationBusy = false;
        }
    }

    private async Task HandleInitiativeChanged(int? initiative)
    {
        if (!initiative.HasValue)
        {
            _notice = IsSessionCombat
                ? "In einer Session kann die eigene INI nicht leer sein."
                : "Bitte einen konkreten INI-Wert setzen.";
            return;
        }

        if (_valueMutationBusy)
        {
            return;
        }

        _valueMutationBusy = true;
        _notice = null;
        try
        {
            if (IsSessionCombat)
            {
                var result = await CombatSessionState.SetInitiativeAsync(
                    initiative.Value,
                    OwnSessionParticipant?.Id,
                    ActiveHero?.Id,
                    BuildRuntimeState(),
                    SelectedSet?.Id);
                if (result.Stale || !result.Applied)
                {
                    _notice = result.Message;
                }
                else
                {
                    await CombatState.SetInitiativeAsync(initiative);
                }
            }
            else
            {
                await CombatState.SetInitiativeAsync(initiative);
            }
        }
        catch (Exception exception)
        {
            _notice = exception.Message;
        }
        finally
        {
            _valueMutationBusy = false;
        }
    }

    private async Task RollInitiativeFromStatusAsync()
    {
        if (!CanRollInitiative)
        {
            _notice = InitiativeDisabledReason ?? "Der INI-Wurf ist derzeit nicht verfügbar.";
            return;
        }

        _rollBusy = true;
        _notice = null;
        try
        {
            if (IsSessionCombat)
            {
                var sessionResult = await CombatSessionState.RollInitiativeAsync(
                    OwnSessionParticipant?.Id,
                    ActiveHero?.Id,
                    BuildRuntimeState(),
                    SelectedSet?.Id);
                var initiative = sessionResult.Snapshot.Participants
                    .FirstOrDefault(participant => participant.HeroId == ActiveHero?.Id)?.CurrentInitiative;
                if (initiative.HasValue)
                {
                    await CombatState.SetInitiativeAsync(initiative);
                }

                if (sessionResult.Stale || !sessionResult.Applied)
                {
                    _notice = sessionResult.Message;
                }

                return;
            }

            var result = await CombatCoordinator.RollAsync(new CombatRollRequestDto
            {
                RequestId = Guid.NewGuid(),
                SessionId = SessionState.ActiveSessionId,
                HeroId = ActiveHero?.Id,
                SetId = SelectedSet?.Id,
                Action = CombatActionKind.InitiativeHelper,
                RuntimeState = BuildRuntimeState(),
                Helper = new CombatHelperRollRequestDto { DiceCount = 1, DiceSides = 6, Purpose = "INI-Startwurf" }
            });
            var baseInitiative = SelectedSet?.Initiative ?? 0;
            var runtime = CombatRuntimeModifierRules.ResolveInitiative(Profile, SelectedSet, BuildRuntimeState());
            await CombatState.SetInitiativeAsync(baseInitiative + result.Rolls.Sum(roll => roll.Value) + runtime.Modifier);
        }
        catch (Exception exception)
        {
            _notice = exception.Message;
        }
        finally
        {
            _rollBusy = false;
        }
    }

    private async Task HandleParticipantInitiativeChanged(CombatSessionParticipantDto participant, int? initiative)
    {
        if (!initiative.HasValue)
        {
            _notice = "Bitte einen konkreten INI-Wert setzen.";
            return;
        }

        if (!CanEditParticipantInitiative(participant) || _valueMutationBusy)
        {
            return;
        }

        _valueMutationBusy = true;
        _notice = null;
        try
        {
            var result = await CombatSessionState.SetInitiativeAsync(
                initiative.Value,
                participant.Id,
                participant.HeroId,
                BuildRuntimeState(),
                participant.InitiativeSetId ?? SelectedSet?.Id);
            if (result.Stale || !result.Applied)
            {
                _notice = result.Message;
            }
        }
        catch (Exception exception)
        {
            _notice = exception.Message;
        }
        finally
        {
            _valueMutationBusy = false;
        }
    }

    private bool CanEditParticipantInitiative(CombatSessionParticipantDto participant)
    {
        if (!IsSessionCombat || participant.HeroId == ActiveHero?.Id)
        {
            return false;
        }

        return IsSessionMaster || string.Equals(
            participant.OwnerUserId,
            AuthState.Current.User?.Id,
            StringComparison.Ordinal);
    }

    private void OpenOrientationDrawer()
    {
        _orientationReliefDraft = Profile?.KriegskunstValue is { } kriegskunst
            ? Math.Max(0, kriegskunst / 2)
            : 0;
        _orientationUninterruptedDraft = true;
        _drawer = CombatDrawer.Orientation;
    }

    private void OpenOpponentDrawer()
    {
        _participantDrawerMode = CombatParticipantDrawerMode.Opponent;
        _opponentNameDraft = string.Empty;
        _opponentAffiliationDraft = "Gegner";
        _opponentInitiativeBaseDraft = null;
        _opponentInitiativeDraft = null;
        _drawer = CombatDrawer.Participant;
    }

    private void OpenAnnouncementDrawer()
    {
        _participantDrawerMode = CombatParticipantDrawerMode.Announcement;
        _initiativeParticipantId = OwnSessionParticipant?.Id;
        _initiativeHeroId = ActiveHero?.Id;
        _announcementDraft = OwnSessionParticipant?.Announcement ?? string.Empty;
        _drawer = CombatDrawer.Participant;
    }

    private void OpenHistoryDrawer() => _drawer = CombatDrawer.History;

    private void CloseDrawer()
    {
        _drawer = CombatDrawer.None;
    }

    private async Task InitializeCombatStateAsync()
    {
        if (!await CombatState.StartCombatAsync())
        {
            _notice = "Für die Initialisierung fehlt ein importiertes Kampfprofil.";
            return;
        }

        var sessionResult = await SyncSessionRuntimeStateAsync();
        _notice = sessionResult?.Stale == true
            ? sessionResult.Message
            : "Laufende Kampfwerte mit den importierten Maximalwerten initialisiert.";
    }

    private async Task ApplyParticipantDrawerAsync()
    {
        CombatSessionMutationResultDto result;
        if (_participantDrawerMode == CombatParticipantDrawerMode.Opponent)
        {
            result = await CombatSessionState.AddOpponentAsync(
                _opponentNameDraft,
                _opponentInitiativeBaseDraft,
                _opponentInitiativeDraft,
                _opponentAffiliationDraft);
        }
        else
        {
            result = await CombatSessionState.SetAnnouncementAsync(
                _announcementDraft,
                _initiativeParticipantId,
                _initiativeHeroId);
        }

        _notice = result.Message;
        if (result.Applied)
        {
            CloseDrawer();
        }
    }

    private Task HandleOpponentNameChanged(string value)
    {
        _opponentNameDraft = value ?? string.Empty;
        return Task.CompletedTask;
    }

    private Task HandleOpponentAffiliationChanged(string value)
    {
        _opponentAffiliationDraft = value ?? string.Empty;
        return Task.CompletedTask;
    }

    private Task HandleOpponentInitiativeBaseChanged(int? value)
    {
        _opponentInitiativeBaseDraft = value;
        return Task.CompletedTask;
    }

    private Task HandleOpponentInitiativeChanged(int? value)
    {
        _opponentInitiativeDraft = value;
        return Task.CompletedTask;
    }

    private Task HandleAnnouncementChanged(string value)
    {
        _announcementDraft = value ?? string.Empty;
        return Task.CompletedTask;
    }

    private void HandleHistorySelected(RollHistoryEntryDto entry)
    {
        _drawer = CombatDrawer.None;
        _selectedHistoryEntry = entry;
    }

    private void OpenLastResultDetails()
    {
        if (CombatResult is not null || AttributeResult is not null)
        {
            _selectedHistoryEntry = CombatResult?.HistoryEntry ?? AttributeResult?.HistoryEntry;
        }
    }

    private Task CloseHistoryDetails()
    {
        _selectedHistoryEntry = null;
        return Task.CompletedTask;
    }

    private async Task UndoLastChangeAsync()
    {
        if (IsSessionCombat)
        {
            var result = await CombatSessionState.UndoAsync();
            _notice = result.Message;
            return;
        }

        _notice = await CombatState.UndoLastChangeAsync()
            ? "Letzte Änderung wurde rückgängig gemacht."
            : "Keine Änderung zum Rückgängigmachen vorhanden.";
    }

    private async Task CompleteActionAsync(CombatSessionActionDto action)
    {
        var result = await CombatSessionState.CompleteActionAsync(
            action.Id,
            action.ParticipantId,
            ActiveHero?.Id,
            BuildRuntimeState(),
            SelectedSet?.Id);
        _notice = result.Message;
    }

    private async Task ConsumeReactionAsync(CombatSessionParticipantDto participant)
    {
        var result = await CombatSessionState.ConsumeReactionAsync(participant.Id, participant.HeroId);
        _notice = result.Message;
    }

    private Task ConsumeReactionFromResultAsync()
    {
        return OwnSessionParticipant is { } participant
            ? ConsumeReactionAsync(participant)
            : Task.CompletedTask;
    }

    private async Task HoldActionAsync(CombatSessionActionDto action)
    {
        var result = await CombatSessionState.HoldActionAsync(action.Id, action.ParticipantId);
        _notice = result.Message;
    }

    private async Task ExecuteHeldActionAsync(CombatSessionActionDto action)
    {
        var result = await CombatSessionState.ExecuteHeldActionAsync(action.Id, action.ParticipantId);
        _notice = result.Message;
    }

    private async Task NewRoundAsync()
    {
        var result = await CombatSessionState.NewRoundAsync();
        _notice = result.Message;
    }

    private async Task OrientAsync(bool hasAttention)
    {
        var result = await CombatSessionState.OrientAsync(
            hasAttention,
            OwnSessionParticipant?.Id,
            ActiveHero?.Id,
            hasAttention ? null : _orientationReliefDraft,
            _orientationUninterruptedDraft,
            BuildRuntimeState(),
            SelectedSet?.Id);
        _notice = result.Message;
    }

    private async Task ApplyOrientationAsync()
    {
        if (OwnSessionParticipant?.CurrentInitiative is null)
        {
            _notice = "Orientieren ist erst nach dem ersten Initiativewurf verfügbar.";
            return;
        }

        await OrientAsync(HasAttention);
        if (_notice is not null && SessionCombat?.Actions.Any(action =>
                action.Round == SessionCombat.Round &&
                IsOrientationAction(action) &&
                action.ParticipantId == OwnSessionParticipant.Id &&
                action.State == CombatActionEntryState.Open) == true)
        {
            CloseDrawer();
        }
    }

    private async Task ResolveOrientationAsync(CombatSessionActionDto action)
    {
        var result = await CombatSessionState.ResolveOrientationAsync(
            action.Id,
            action.ParticipantId,
            ActiveHero?.Id,
            BuildRuntimeState(),
            SelectedSet?.Id);
        _notice = result.Message;
    }

    private static bool IsOrientationAction(CombatSessionActionDto action) =>
        string.Equals(action.Label, "Orientieren", StringComparison.OrdinalIgnoreCase);

    private Task HandleOrientationReliefChanged(int? value)
    {
        _orientationReliefDraft = value.HasValue ? Math.Clamp(value.Value, 0, 20) : 0;
        return Task.CompletedTask;
    }

    private Task HandleOrientationUninterruptedChanged(ChangeEventArgs args)
    {
        _orientationUninterruptedDraft = args.Value is bool value
            ? value
            : bool.TryParse(args.Value?.ToString(), out var parsed) && parsed;
        return Task.CompletedTask;
    }

    private bool IsCurrentParticipant(CombatSessionParticipantDto participant)
    {
        return SessionCombat?.CurrentActionIds.Any(actionId =>
            SessionCombat.Actions.FirstOrDefault(action => action.Id == actionId)?.ParticipantId == participant.Id) == true;
    }

    private string GetParticipantTurnLabel(CombatSessionParticipantDto participant)
    {
        if (IsCurrentParticipant(participant))
        {
            return "Jetzt";
        }

        if (NextInitiativeParticipantIds.Contains(participant.Id))
        {
            return "Als Nächstes";
        }

        var actions = GetParticipantActions(participant.Id);
        if (actions.Any(action => action.State == CombatActionEntryState.Held))
        {
            return "Wartet";
        }

        if (actions.Any(action => action.State == CombatActionEntryState.Completed))
        {
            return "Bereits gehandelt";
        }

        return participant.CurrentInitiative.HasValue ? "Weitere offen" : "INI fehlt";
    }

    private string GetParticipantTurnClass(CombatSessionParticipantDto participant) => GetParticipantTurnLabel(participant) switch
    {
        "Jetzt" => "turn-now",
        "Als Nächstes" => "turn-next",
        "Wartet" => "turn-held",
        "Bereits gehandelt" => "turn-completed",
        "INI fehlt" => "turn-unknown",
        _ => "turn-open"
    };

    private static int GetActionInitiative(
        CombatSessionActionDto action,
        CombatSessionSnapshotDto snapshot)
    {
        return snapshot.Participants.FirstOrDefault(participant => participant.Id == action.ParticipantId)?.CurrentInitiative
               ?? action.PhaseInitiative
               ?? int.MinValue;
    }

    private IReadOnlyList<CombatSessionActionDto> GetParticipantActions(string participantId)
    {
        return SessionCombat?.Actions
            .Where(action => action.ParticipantId == participantId && action.Round == SessionCombat.Round)
            .OrderBy(action => action.State == CombatActionEntryState.Completed)
            .ThenByDescending(action => action.PhaseInitiative ?? int.MinValue)
            .ToArray() ?? Array.Empty<CombatSessionActionDto>();
    }

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

    private int? GetActionBaseValue() => GetActionKind() switch
    {
        CombatActionKind.MeleeAttack => SelectedWeapon?.Attack,
        CombatActionKind.WeaponParry or CombatActionKind.ShieldParry => SelectedWeapon?.Parry,
        CombatActionKind.Dodge => SelectedSet?.Dodge,
        CombatActionKind.RangedAttack => SelectedWeapon?.RangedValue,
        _ => null
    };

    private IReadOnlyList<CombatModifierDto> GetCurrentModifiers()
    {
        var automatic = GetAutomaticModifiers();
        if (Modifier == 0)
        {
            return automatic;
        }

        return automatic
            .Append(new CombatModifierDto("Situativ", Modifier, "Kampfseite"))
            .ToArray();
    }

    private IReadOnlyList<CombatModifierDto> GetAutomaticModifiers()
    {
        if (Profile is null || SelectedSet is null || GetActionKind() is not { } action)
        {
            return Array.Empty<CombatModifierDto>();
        }

        return CombatRuntimeModifierRules.Resolve(
                Profile,
                SelectedSet,
                BuildRuntimeState(),
                action,
                new CombatRuleOptionsDto(SpecialResultsEnabled: true, LowLePEnabled: true))
            .Modifiers;
    }

    private CombatRuntimeStateDto BuildRuntimeState() => new()
    {
        IsStarted = CombatState.IsStarted,
        CurrentLeP = CurrentLeP,
        CurrentAuP = CurrentAuP,
        Wounds = Wounds.ToDictionary(pair => pair.Key, pair => pair.Value)
    };

    private async Task<CombatSessionMutationResultDto?> SyncSessionRuntimeStateAsync(string? setId = null)
    {
        if (!IsSessionCombat || OwnSessionParticipant is not { } participant || ActiveHero is null)
        {
            return null;
        }

        return await CombatSessionState.SyncRuntimeStateAsync(
            BuildRuntimeState(),
            participant.Id,
            ActiveHero.Id,
            setId ?? SelectedSet?.Id);
    }

    private static string FormatSigned(int value) => value > 0 ? $"+{value}" : value.ToString(CultureInfo.InvariantCulture);

    private CombatRuntimeInitiativeResult GetRuntimeInitiative() =>
        CombatRuntimeModifierRules.ResolveInitiative(Profile, SelectedSet, BuildRuntimeState());

    private bool HasRuntimeInitiativeNotes() => GetRuntimeInitiative().RuleNotes.Length > 0;

    private string GetRuntimeInitiativeSummary()
    {
        var result = GetRuntimeInitiative();
        var modifier = $"Automatische INI {FormatSigned(result.Modifier)}";
        return result.RuleNotes.Length == 0
            ? modifier
            : $"{modifier} · {string.Join(" · ", result.RuleNotes)}";
    }

    private string GetInitiativeDiceLabel() => Profile?.HasKlingentaenzer == true ? "2W6" : "1W6";

    private static bool HasRuntimeInitiativeState(CombatSessionParticipantDto participant) =>
        participant.InitiativeRuntimeModifier != 0 || participant.InitiativeRuntimeNotes.Length > 0;

    private static string FormatRuntimeNotes(IReadOnlyList<string> notes) =>
        notes.Count == 0 ? "Keine automatische INI-Notiz" : string.Join(" · ", notes);

    private static string FormatModifier(CombatModifierDto modifier)
    {
        var value = modifier.Value > 0 ? $"+{modifier.Value}" : modifier.Value.ToString(CultureInfo.InvariantCulture);
        return $"{modifier.Label} {value}";
    }

    private bool IsLastCriticalAttack()
    {
        var result = CombatResult;
        return result is not null &&
               result.Snapshot.Action is CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack &&
               result.Snapshot.Outcome == CombatOutcome.Critical &&
               string.Equals(result.Snapshot.WeaponName, SelectedWeapon?.Name, StringComparison.Ordinal);
    }

    private int? GetResourceValue(CombatResourceKind resource) => resource switch
    {
        CombatResourceKind.LeP => CurrentLeP,
        CombatResourceKind.AuP => CurrentAuP,
        CombatResourceKind.AeP => CurrentAeP,
        CombatResourceKind.KeP => CurrentKeP,
        _ => null
    };

    private int? GetResourceMaximum(CombatResourceKind resource) => resource switch
    {
        CombatResourceKind.LeP => Profile?.Resources.LeP,
        CombatResourceKind.AuP => Profile?.Resources.AuP,
        CombatResourceKind.AeP => Profile?.Resources.AeP,
        CombatResourceKind.KeP => Profile?.Resources.KeP,
        _ => null
    };

    private static string FormatActionValue(string label, int? value) => value.HasValue ? $"{label} {value}" : $"{label} —";

    private static string GetDamageText(CombatWeaponDto? weapon) => weapon?.CalculatedDamage ?? weapon?.BaseDamage ?? "—";

    private enum CombatArea
    {
        Kampf,
        Zonen,
        Runde,
        Eigenschaften
    }

    private enum CombatDrawer
    {
        None,
        Orientation,
        Participant,
        History
    }

    private enum CombatParticipantDrawerMode
    {
        Opponent,
        Announcement
    }

}
