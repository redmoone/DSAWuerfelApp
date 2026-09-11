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
    private bool _attributeMode;
    private CombatArea _activeArea = CombatArea.Wurf;
    private CombatDrawer _drawer;
    private CombatResourceKind? _resourceKind;
    private int? _resourceDraft;
    private int? _woundDraft;
    private int? _initiativeDraft;
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
    private CombatWoundZone WoundDrawerZone => SelectedZone ?? CombatWoundZone.Torso;
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
        ? HasCombatContext && !_rollBusy && SelectedAttributes.Count > 0
        : HasCombatContext && !_rollBusy &&
          Actions.FirstOrDefault(action => action.Key == SelectedAction)?.IsAvailable == true;

    private bool CanConsumeReaction => IsSessionCombat &&
                                       OwnSessionParticipant?.ReactionAvailable == true &&
                                       CombatResult?.Snapshot.Action is CombatActionKind.WeaponParry or CombatActionKind.ShieldParry or CombatActionKind.Dodge;

    private string DrawerTitle => _drawer switch
    {
        CombatDrawer.Resource => $"{GetResourceLabel(_resourceKind ?? CombatResourceKind.LeP)} setzen",
        CombatDrawer.Wound => "Wundstand setzen",
        CombatDrawer.Initiative => "Initiative",
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
            _activeArea = CombatArea.Wurf;
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

    private Task ResetAction() => CombatState.ResetActionAsync();

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
            var result = await CombatCoordinator.RollAsync(BuildRollRequest(action));
            _notice = result.Snapshot.StatusLabel;
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
            _notice = AttributeResult is { Success: true }
                ? "Eigenschaftsprobe gelungen."
                : "Eigenschaftsprobe ausgewertet.";
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
        OpenWoundDrawer();
    }

    private void SetArea(CombatArea area) => _activeArea = area;

    private void OpenResourceDrawer(CombatResourceKind resource)
    {
        _resourceKind = resource;
        _resourceDraft = GetResourceValue(resource);
        _drawer = CombatDrawer.Resource;
    }

    private void OpenWoundDrawer()
    {
        var zone = WoundDrawerZone;
        _woundDraft = Wounds.TryGetValue(zone, out var wounds) ? wounds ?? 0 : 0;
        _drawer = CombatDrawer.Wound;
    }

    private void OpenInitiativeDrawer()
    {
        _initiativeParticipantId = OwnSessionParticipant?.Id;
        _initiativeHeroId = ActiveHero?.Id;
        _initiativeDraft = CurrentInitiative ?? SelectedSet?.Initiative;
        _drawer = CombatDrawer.Initiative;
    }

    private void OpenOrientationDrawer()
    {
        _orientationReliefDraft = Profile?.KriegskunstValue is { } kriegskunst
            ? Math.Max(0, kriegskunst / 2)
            : 0;
        _orientationUninterruptedDraft = true;
        _drawer = CombatDrawer.Orientation;
    }

    private void OpenParticipantInitiativeDrawer(CombatSessionParticipantDto participant)
    {
        _initiativeParticipantId = participant.Id;
        _initiativeHeroId = participant.HeroId;
        _initiativeDraft = participant.CurrentInitiative;
        _drawer = CombatDrawer.Initiative;
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
        _resourceKind = null;
    }

    private async Task ApplyResourceAsync()
    {
        if (_resourceKind is not { } resource)
        {
            return;
        }

        await CombatState.SetResourceAsync(resource, _resourceDraft);
        _notice = $"{GetResourceLabel(resource)} gespeichert.";
        CloseDrawer();
    }

    private async Task ApplyWoundAsync()
    {
        if (!Wounds.TryGetValue(WoundDrawerZone, out _) || !_woundDraft.HasValue)
        {
            _notice = "Bitte einen Wundstand von 0 bis 3 setzen.";
            return;
        }

        await CombatState.SetWoundAsync(WoundDrawerZone, _woundDraft.Value);
        _notice = $"{GetWoundLabel(WoundDrawerZone)} gespeichert.";
        CloseDrawer();
    }

    private async Task ApplyInitiativeAsync()
    {
        if (!_initiativeDraft.HasValue)
        {
            _notice = "Bitte einen konkreten INI-Wert setzen.";
            return;
        }

        if (IsSessionCombat)
        {
            var result = await CombatSessionState.SetInitiativeAsync(
                _initiativeDraft.Value,
                _initiativeParticipantId,
                _initiativeHeroId);
            _notice = result.Message;
            if (result.Applied)
            {
                if (_initiativeHeroId == ActiveHero?.Id)
                {
                    await CombatState.SetInitiativeAsync(_initiativeDraft);
                }

                CloseDrawer();
            }
            return;
        }

        if (await CombatState.SetInitiativeAsync(_initiativeDraft))
        {
            _notice = _initiativeDraft.HasValue ? $"INI {_initiativeDraft} gespeichert." : "Laufende Initiative entfernt.";
            CloseDrawer();
        }
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

    private async Task RollInitiativeAsync()
    {
        if (!HasCombatContext)
        {
            _notice = "Für den INI-Hilfswurf fehlt ein importiertes Kampfprofil.";
            return;
        }

        _rollBusy = true;
        try
        {
            if (IsSessionCombat)
            {
                var sessionResult = await CombatSessionState.RollInitiativeAsync(_initiativeParticipantId, _initiativeHeroId);
                _initiativeDraft = sessionResult.Snapshot.Participants
                    .FirstOrDefault(participant => participant.Id == _initiativeParticipantId)?.CurrentInitiative;
                _notice = sessionResult.Message;
                if (sessionResult.Applied)
                {
                    if (_initiativeHeroId == ActiveHero?.Id && _initiativeDraft.HasValue)
                    {
                        await CombatState.SetInitiativeAsync(_initiativeDraft);
                    }

                    CloseDrawer();
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
                Helper = new CombatHelperRollRequestDto { DiceCount = 1, DiceSides = 6, Purpose = "INI-Startwurf" }
            });
            var baseInitiative = SelectedSet?.Initiative ?? 0;
            _initiativeDraft = baseInitiative + result.Rolls.Sum(roll => roll.Value);
            _notice = $"INI-Hilfswurf: {_initiativeDraft} zum Anwenden bereit.";
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

    private Task HandleResourceDraftChanged(int? value)
    {
        _resourceDraft = value;
        return Task.CompletedTask;
    }

    private Task HandleWoundDraftChanged(int? value)
    {
        _woundDraft = value.HasValue ? Math.Clamp(value.Value, 0, 3) : null;
        return Task.CompletedTask;
    }

    private Task HandleInitiativeDraftChanged(int? value)
    {
        _initiativeDraft = value;
        return Task.CompletedTask;
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
        var result = await CombatSessionState.CompleteActionAsync(action.Id, action.ParticipantId);
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
            _orientationUninterruptedDraft);
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
        var result = await CombatSessionState.ResolveOrientationAsync(action.Id, action.ParticipantId);
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
        IsStarted = IsCombatStarted,
        CurrentLeP = CurrentLeP,
        CurrentAuP = CurrentAuP,
        Wounds = Wounds.ToDictionary(pair => pair.Key, pair => pair.Value)
    };

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

    private static int? GetResourceMinimum(CombatResourceKind resource) => resource == CombatResourceKind.LeP ? null : 0;

    private static string GetResourceLabel(CombatResourceKind resource) => resource switch
    {
        CombatResourceKind.LeP => "LeP",
        CombatResourceKind.AuP => "AuP",
        CombatResourceKind.AeP => "AeP",
        CombatResourceKind.KeP => "KE",
        _ => "Wert"
    };

    private static string GetResourceDescription(CombatResourceKind resource) => resource switch
    {
        CombatResourceKind.LeP => "Lebenspunkte bleiben auch unter 0 sichtbar und werden nicht automatisch geheilt.",
        CombatResourceKind.AuP => "Ausdauerpunkte dürfen nicht negativ gesetzt werden.",
        CombatResourceKind.AeP => "Astralenergie wird nur angezeigt, wenn ein positives Maximum importiert wurde.",
        CombatResourceKind.KeP => "Karmaenergie wird nur angezeigt, wenn ein positives Maximum importiert wurde.",
        _ => string.Empty
    };

    private string GetWoundLabel(CombatWoundZone zone) => zone switch
    {
        CombatWoundZone.Head => "Kopf",
        CombatWoundZone.Torso => "Torso",
        CombatWoundZone.Abdomen => "Bauch",
        CombatWoundZone.LeftArm => "Linker Arm",
        CombatWoundZone.RightArm => "Rechter Arm",
        CombatWoundZone.LeftLeg => "Linkes Bein",
        CombatWoundZone.RightLeg => "Rechtes Bein",
        _ => zone.ToString()
    };

    private string GetArmorFacingLabel()
    {
        var armorZone = WoundDrawerZone == CombatWoundZone.Torso
            ? Facing == CombatFacing.Back ? CombatArmorZone.Back : CombatArmorZone.Chest
            : WoundDrawerZone switch
            {
                CombatWoundZone.Head => CombatArmorZone.Head,
                CombatWoundZone.Abdomen => CombatArmorZone.Abdomen,
                CombatWoundZone.LeftArm => CombatArmorZone.LeftArm,
                CombatWoundZone.RightArm => CombatArmorZone.RightArm,
                CombatWoundZone.LeftLeg => CombatArmorZone.LeftLeg,
                CombatWoundZone.RightLeg => CombatArmorZone.RightLeg,
                _ => CombatArmorZone.Chest
            };
        return $"{(Facing == CombatFacing.Back ? "Rückseite" : "Vorderseite")} · RS {GetArmorValue(armorZone)?.ToString() ?? "—"}";
    }

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

    private static string FormatActionValue(string label, int? value) => value.HasValue ? $"{label} {value}" : $"{label} —";

    private static string GetDamageText(CombatWeaponDto? weapon) => weapon?.CalculatedDamage ?? weapon?.BaseDamage ?? "—";

    private enum CombatArea
    {
        Wurf,
        Initiative,
        Zonen
    }

    private enum CombatDrawer
    {
        None,
        Resource,
        Wound,
        Initiative,
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
