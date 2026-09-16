using System.Globalization;

using DsaWuerfelApp.Client.Components.Combat;
using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Pages;

public partial class Kampf : IDisposable
{
    private static readonly IReadOnlyDictionary<CombatWoundZone, int?> EmptySessionWounds =
        new Dictionary<CombatWoundZone, int?>();

    [Inject] public ActiveHeroState ActiveHeroState { get; set; } = null!;
    [Inject] public AuthState AuthState { get; set; } = null!;
    [Inject] public CombatCoordinator CombatCoordinator { get; set; } = null!;
    [Inject] public CombatState CombatState { get; set; } = null!;
    [Inject] public CombatSessionState CombatSessionState { get; set; } = null!;
    [Inject] public SessionState SessionState { get; set; } = null!;
    [Inject] public WuerfelState WuerfelState { get; set; } = null!;
    [Inject] public WuerfelFacade WuerfelFacade { get; set; } = null!;
    [Inject] public IWuerfelApiClient WuerfelApiClient { get; set; } = null!;

    private string? _notice;
    private string? _lastContextKey;
    private bool _rollBusy;
    private bool _valueMutationBusy;
    private CombatResourceKind? _resourceDrawerKind;
    private int? _resourceDraft;
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
    private CombatEnemyCatalogEntryDto[] _enemyCatalog = [];
    private bool _enemyCatalogLoading;
    private string? _selectedEnemyId;
    private string? _selectedEnemyVariantId;
    private string? _selectedEnemyAttackId;
    private string? _selectedEnemyWeaponOption;
    private string? _selectedEnemyArmorOption;
    private int? _selectedEnemyLeP;
    private string _announcementDraft = string.Empty;
    private CombatParticipantDrawerMode _participantDrawerMode;
    private RollHistoryEntryDto? _selectedHistoryEntry;
    private string? _selectedTargetParticipantId;
    private string? _selectedActingOpponentId;
    private string? _selectedOpponentTargetId;
    private string? _selectedOpponentAttackId;
    private string? _viewSessionId;
    private bool _combatViewInitialized;
    private bool _isMasterView;

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
    private CombatRuntimeStateDto? SessionRuntimeState =>
        IsSessionCombat ? OwnSessionParticipant?.RuntimeState : null;
    private int? CurrentLeP => IsSessionCombat ? SessionRuntimeState?.CurrentLeP : CombatState.CurrentLeP;
    private int? CurrentAuP => IsSessionCombat ? SessionRuntimeState?.CurrentAuP : CombatState.CurrentAuP;
    private int? CurrentAeP => CombatState.CurrentAeP;
    private int? CurrentKeP => CombatState.CurrentKeP;
    private CombatSessionSnapshotDto? SessionCombat => CombatSessionState.Current;
    private CombatSessionParticipantDto? OwnSessionParticipant => SessionCombat?.Participants
        .FirstOrDefault(participant => participant.Kind == CombatParticipantKind.Hero &&
                                       participant.HeroId == ActiveHero?.Id &&
                                       string.Equals(participant.OwnerUserId,
                                           AuthState.Current.User?.Id,
                                           StringComparison.Ordinal));
    private int? CurrentInitiative => IsSessionCombat
        ? OwnSessionParticipant?.CurrentInitiative
        : CombatState.CurrentInitiative;
    private int? InitiativeBase => IsSessionCombat
        ? OwnSessionParticipant?.InitiativeBase
        : SelectedSet?.Initiative;
    private IReadOnlyDictionary<CombatWoundZone, int?> Wounds =>
        IsSessionCombat ? SessionRuntimeState?.Wounds ?? EmptySessionWounds : CombatState.Wounds;
    private bool HasCombatContext => ActiveHero is not null && Profile is not null && SelectedSet is not null;
    private bool IsSessionCombat => !string.IsNullOrWhiteSpace(SessionState.ActiveSessionId);
    private bool IsCombatStarted => IsSessionCombat ? SessionCombat?.IsStarted == true : CombatState.IsStarted;
    private bool CanUndoCombat => IsSessionCombat ? CombatSessionState.CanUndo : CombatState.CanUndo;
    private bool IsSessionMaster => IsSessionCombat &&
                                    string.Equals(SessionState.ActiveSession?.MasterUserId,
                                        AuthState.Current.User?.Id, StringComparison.Ordinal);
    private bool HasOwnSessionHero => !IsSessionCombat || OwnSessionParticipant is not null;
    private bool CanSwitchCombatView => IsSessionCombat && IsSessionMaster && _combatViewInitialized;
    private bool IsMasterView => CanSwitchCombatView && _isMasterView;
    private bool NeedsOwnHeroPlaceholder => IsSessionCombat && !HasOwnSessionHero;
    private bool HasOpenAttackExchange => IsSessionCombat &&
                                          CombatAttackExchangeRules.IsOpen(SessionCombat?.ActiveExchange);
    private const string OpenExchangeNotice = "Der offene Angriffsaustausch muss zuerst abgeschlossen werden.";

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

    private const string ManualOpponentSelection = "__manual__";

    private CombatEnemyCatalogEntryDto? SelectedEnemyCatalogEntry =>
        _enemyCatalog.FirstOrDefault(enemy =>
            string.Equals(enemy.Id, _selectedEnemyId, StringComparison.Ordinal));

    private CombatEnemyCombatDto? SelectedEnemyCombat =>
        SelectedEnemyCatalogEntry is not { } enemy
            ? null
            : enemy.CombatVariants.FirstOrDefault(variant =>
                    string.Equals(variant.Id, _selectedEnemyVariantId, StringComparison.Ordinal))?.Combat
              ?? enemy.Combat;

    private CombatEnemyRangeDto? SelectedEnemyLePRange => SelectedEnemyCombat?.Resources.LeP.SourceRange;

    private bool IsManualOpponentSelection =>
        string.Equals(_selectedEnemyId, ManualOpponentSelection, StringComparison.Ordinal);

    private bool HasSelectedEnemyVariant =>
        SelectedEnemyCatalogEntry is not { CombatVariants.Length: > 0 } ||
        !string.IsNullOrWhiteSpace(_selectedEnemyVariantId);

    private bool HasSelectedEnemyAttack =>
        SelectedEnemyCatalogEntry is not { Attacks.Length: > 1 } ||
        !string.IsNullOrWhiteSpace(_selectedEnemyAttackId);

    private bool HasSelectedEnemyEquipment =>
        SelectedEnemyCatalogEntry?.Equipment is not { SelectionRequired: true } ||
        (!string.IsNullOrWhiteSpace(_selectedEnemyWeaponOption) &&
         !string.IsNullOrWhiteSpace(_selectedEnemyArmorOption));

    private bool HasSelectedEnemyLeP =>
        SelectedEnemyLePRange is null || _selectedEnemyLeP.HasValue;

    private bool CanApplyOpponentDrawer =>
        IsMasterView &&
        (IsManualOpponentSelection
            ? !string.IsNullOrWhiteSpace(_opponentNameDraft)
            : SelectedEnemyCatalogEntry is not null &&
              SelectedEnemyCombat is not null &&
              HasSelectedEnemyVariant &&
              HasSelectedEnemyAttack &&
              HasSelectedEnemyEquipment &&
              HasSelectedEnemyLeP);

    private string HeroDisplayName => ActiveHero?.Name ?? "Kein aktiver Held";

    private IReadOnlyList<CombatActionPanel.ActionOption> Actions =>
    [
        new(CombatActionIds.Attack, "Attacke", FormatActionValue("AT", SelectedWeapon?.Attack), HasWeaponAttack && CanUseActionBudget(CombatActionKind.MeleeAttack) && CanUseActionDuringOpenExchange(CombatActionKind.MeleeAttack)),
        new(CombatActionIds.Parry, "Waffenparade", FormatActionValue("PA", SelectedWeapon?.Parry), HasWeaponParry && CanUseActionBudget(CombatActionKind.WeaponParry) && CanUseActionDuringOpenExchange(CombatActionKind.WeaponParry)),
        new(CombatActionIds.ShieldParry, "Schildparade", FormatActionValue("PA", SelectedWeapon?.Parry), HasShieldParry && CanUseActionBudget(CombatActionKind.ShieldParry) && CanUseActionDuringOpenExchange(CombatActionKind.ShieldParry)),
        new(CombatActionIds.Dodge, "Ausweichen", FormatActionValue("AW", SelectedSet?.Dodge), SelectedSet?.Dodge.HasValue == true && CanUseActionBudget(CombatActionKind.Dodge) && CanUseActionDuringOpenExchange(CombatActionKind.Dodge)),
        new(CombatActionIds.Ranged, "Fernkampf", FormatActionValue("FK", SelectedWeapon?.RangedValue), HasRangedValue && CanUseActionBudget(CombatActionKind.RangedAttack) && CanUseActionDuringOpenExchange(CombatActionKind.RangedAttack)),
        new(CombatActionIds.Initiative, "Initiative", GetInitiativeDiceLabel(), CanRollInitiative)
    ];

    private bool HasWeaponAttack => SelectedWeapon is { Category: CombatWeaponCategory.Melee or CombatWeaponCategory.Unarmed } && SelectedWeapon.Attack.HasValue;
    private bool HasWeaponParry => SelectedWeapon is { Category: CombatWeaponCategory.Melee or CombatWeaponCategory.Unarmed } && SelectedWeapon.Parry.HasValue;
    private bool HasShieldParry => SelectedWeapon is { Category: CombatWeaponCategory.Shield } && SelectedWeapon.Parry.HasValue;
    private bool HasRangedValue => SelectedWeapon is { Category: CombatWeaponCategory.Ranged } && SelectedWeapon.RangedValue.HasValue;
    private bool HasDamage => SelectedWeapon is not null && !string.IsNullOrWhiteSpace(SelectedWeapon.CalculatedDamage ?? SelectedWeapon.BaseDamage);

    private int? EffectiveTarget => CombatRollRules.ResolveEffectiveTarget(GetActionBaseValue(), GetCurrentModifiers());

    private bool CanRoll => IsMasterOpponentMode
        ? CanRollOpponentAttack
        : IsAttributeMode
        ? HasCombatContext && !_rollBusy && !_valueMutationBusy && SelectedAttributes.Count > 0
        : SelectedAction == CombatActionIds.Initiative
            ? CanRollInitiative
        : HasCombatContext && !_rollBusy && !_valueMutationBusy &&
          (!IsSessionCombat || !IsAttackAction(GetActionKind()) || SelectedTargetParticipantId is not null) &&
          (GetActionKind() is not { } selectedKind ||
           CanUseActionDuringOpenExchange(selectedKind) &&
           (CanUseActionBudget(selectedKind) ||
            CanRollOpenSessionAttack(selectedKind))) &&
          (GetActionKind() is { } activeKind && CanRollOpenSessionAttack(activeKind) ||
           Actions.FirstOrDefault(action => action.Key == SelectedAction)?.IsAvailable == true);

    private bool CanRollInitiative => HasCombatContext &&
                                      SelectedSet?.Initiative.HasValue == true &&
                                      !_rollBusy &&
                                      !_valueMutationBusy &&
                                      !HasOpenAttackExchange;

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

            if (HasOpenAttackExchange)
            {
                return OpenExchangeNotice;
            }

            if (SelectedSet?.Initiative.HasValue != true)
            {
                return "Für das Set ist kein INI-Basiswert hinterlegt.";
            }

            return _rollBusy ? "Wurf läuft." : null;
        }
    }

    private bool CanUseActionBudget(CombatActionKind action)
    {
        if (!IsSessionCombat)
        {
            return true;
        }

        var budget = OwnSessionParticipant?.ActionBudget;
        return CombatActionBudgetRules.RequiresNormalAction(action)
            ? budget?.HasNormalAction == true || budget?.HeldActionId is not null
            : CombatActionBudgetRules.RequiresReaction(action)
                ? budget?.HasReaction == true
                : true;
    }

    private bool CanUseActionDuringOpenExchange(CombatActionKind action)
    {
        if (!HasOpenAttackExchange || SessionCombat?.ActiveExchange is not { } exchange)
        {
            return true;
        }

        var ownParticipantId = OwnSessionParticipant?.Id;
        if (string.IsNullOrWhiteSpace(ownParticipantId))
        {
            return false;
        }

        if (action is CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack)
        {
            return exchange.Status == CombatExchangeStatus.Declared &&
                   exchange.AttackKind == action &&
                   string.Equals(exchange.AttackerParticipantId, ownParticipantId, StringComparison.Ordinal);
        }

        if (action is CombatActionKind.WeaponParry or CombatActionKind.ShieldParry or CombatActionKind.Dodge)
        {
            return exchange.Status == CombatExchangeStatus.DefenseOpen &&
                   string.Equals(exchange.TargetParticipantId, ownParticipantId, StringComparison.Ordinal) &&
                   (exchange.AllowedDefenseActions ?? []).Contains(action);
        }

        var isHitFollowUp = action is CombatActionKind.Damage or CombatActionKind.HitZone;
        return isHitFollowUp &&
               exchange.Status is (CombatExchangeStatus.Hit or CombatExchangeStatus.DamageOpen) &&
               string.Equals(exchange.AttackerParticipantId, ownParticipantId, StringComparison.Ordinal);
    }

    private string DrawerTitle => _drawer switch
    {
        CombatDrawer.Resource => $"{GetResourceLabel(_resourceDrawerKind ?? CombatResourceKind.LeP)} setzen",
        CombatDrawer.Orientation => "Orientieren",
        CombatDrawer.Participant => _participantDrawerMode == CombatParticipantDrawerMode.Opponent
            ? "Gegner hinzufügen"
            : "Rundenansage",
        _ => "Details"
    };

    private IReadOnlyList<CombatSessionParticipantDto> InitiativeParticipants =>
        SessionCombat?.Participants
            .OrderByDescending(participant => participant.CurrentInitiative.HasValue)
            .ThenByDescending(participant => participant.CurrentInitiative ?? int.MinValue)
            .ThenByDescending(participant => participant.InitiativeBase ?? int.MinValue)
            .ThenBy(participant => participant.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<CombatSessionParticipantDto>();

    private IReadOnlyList<CombatSessionParticipantDto> TargetParticipants =>
        SessionCombat?.Participants
            .Where(participant => participant.Id != OwnSessionParticipant?.Id)
            .Where(CombatTargetRules.HasTargetProfile)
            .OrderBy(participant => participant.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<CombatSessionParticipantDto>();

    private bool IsMasterOpponentMode => IsMasterView && !HasOwnSessionHero;

    private IReadOnlyList<CombatSessionParticipantDto> OpponentParticipants =>
        SessionCombat?.Participants
            .Where(participant => participant.Kind == CombatParticipantKind.Opponent)
            .Where(participant => participant.OpponentProfile?.CatalogProfile?.CombatReady == true)
            .Where(participant => participant.CurrentInitiative.HasValue)
            .OrderByDescending(IsCurrentParticipant)
            .ThenByDescending(participant => participant.CurrentInitiative ?? int.MinValue)
            .ThenBy(participant => participant.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<CombatSessionParticipantDto>();

    private string? SelectedOpponentParticipantId =>
        OpponentParticipants.Any(participant => participant.Id == _selectedActingOpponentId)
            ? _selectedActingOpponentId
            : OpponentParticipants.FirstOrDefault()?.Id;

    private CombatSessionParticipantDto? SelectedOpponentParticipant =>
        OpponentParticipants.FirstOrDefault(participant => participant.Id == SelectedOpponentParticipantId);

    private IReadOnlyList<CombatSessionParticipantDto> OpponentTargetParticipants =>
        SessionCombat?.Participants
            .Where(participant => participant.Id != SelectedOpponentParticipant?.Id)
            .Where(CombatTargetRules.HasTargetProfile)
            .OrderBy(participant => participant.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<CombatSessionParticipantDto>();

    private string? SelectedOpponentTargetId =>
        OpponentTargetParticipants.Any(participant => participant.Id == _selectedOpponentTargetId)
            ? _selectedOpponentTargetId
            : OpponentTargetParticipants.FirstOrDefault()?.Id;

    private IReadOnlyList<CombatEnemyAttackDto> OpponentAttacks =>
        SelectedOpponentParticipant?.OpponentProfile?.CatalogProfile?.Attacks
            ?? Array.Empty<CombatEnemyAttackDto>();

    private string? SelectedOpponentAttackId =>
        OpponentAttacks.Any(attack => attack.Id == _selectedOpponentAttackId)
            ? _selectedOpponentAttackId
            : SelectedOpponentParticipant?.OpponentProfile?.CatalogProfile?.AttackId
              ?? OpponentAttacks.FirstOrDefault()?.Id;

    private CombatEnemyAttackDto? SelectedOpponentAttack =>
        OpponentAttacks.FirstOrDefault(attack => attack.Id == SelectedOpponentAttackId);

    private CombatSessionActionDto? SelectedOpponentAction =>
        SelectedOpponentParticipant is not { } participant
            ? null
            : GetParticipantActions(participant.Id)
                .FirstOrDefault(action => !action.IsReaction &&
                                          action.State is CombatActionEntryState.Open or CombatActionEntryState.Held);

    private CombatActionKind? SelectedOpponentActionKind => SelectedOpponentAttack?.Category.Contains(
        "ranged", StringComparison.OrdinalIgnoreCase) == true
        ? CombatActionKind.RangedAttack
        : SelectedOpponentAttack is null ? null : CombatActionKind.MeleeAttack;

    private bool CanRollOpponentAttack =>
        IsMasterOpponentMode &&
        !_rollBusy &&
        !_valueMutationBusy &&
        !HasOpenAttackExchange &&
        SelectedOpponentParticipant is not null &&
        SelectedOpponentTargetId is not null &&
        SelectedOpponentAttack is not null &&
        SelectedOpponentAction is { } action &&
        (action.State == CombatActionEntryState.Held ||
         SessionCombat?.CurrentActionIds.Contains(action.Id, StringComparer.Ordinal) == true);

    private string? SelectedTargetParticipantId =>
        TargetParticipants.Any(participant => participant.Id == _selectedTargetParticipantId)
            ? _selectedTargetParticipantId
            : null;

    private bool CanSelectCombatTarget =>
        IsSessionCombat && HasOwnSessionHero &&
        (IsMasterView || CanManageOwnSessionParticipant) &&
        IsAttackAction(GetActionKind()) &&
        !HasOpenAttackExchange;

    private bool IsTargetSelectable(CombatSessionParticipantDto participant) =>
        CanSelectCombatTarget && TargetParticipants.Any(target => target.Id == participant.Id);

    private string? ActiveExchangeAttackerName => SessionCombat?.ActiveExchange is { } exchange
        ? SessionCombat.Participants.FirstOrDefault(participant => participant.Id == exchange.AttackerParticipantId)?.Name
        : null;

    private string? ActiveExchangeTargetName => SessionCombat?.ActiveExchange is { } exchange
        ? SessionCombat.Participants.FirstOrDefault(participant => participant.Id == exchange.TargetParticipantId)?.Name
        : null;

    private bool CanRespondToExchange =>
        IsSessionCombat &&
        SessionCombat?.ActiveExchange is { Status: CombatExchangeStatus.DefenseOpen } exchange &&
        OwnSessionParticipant?.Id == exchange.TargetParticipantId &&
        !_rollBusy &&
        !_valueMutationBusy;

    private bool CanResolveActiveExchange =>
        IsMasterView &&
        SessionCombat?.ActiveExchange is { Status: CombatExchangeStatus.DefenseOpen } exchange &&
        exchange.AllowedDefenseActions.Length > 0 &&
        SessionCombat?.Participants.FirstOrDefault(participant =>
            string.Equals(participant.Id, exchange.TargetParticipantId, StringComparison.Ordinal)) is
        { Kind: CombatParticipantKind.Opponent } &&
        !_rollBusy &&
        !_valueMutationBusy;

    private bool CanHoldAction =>
        IsSessionCombat &&
        OwnSessionParticipant is { } participant &&
        !_rollBusy &&
        !_valueMutationBusy &&
        !HasOpenAttackExchange &&
        GetParticipantActions(participant.Id).Any(action =>
            action.Round == SessionCombat?.Round &&
            !action.IsReaction &&
            !action.IsAdditional &&
            action.State == CombatActionEntryState.Open);

    private bool CanShowOrientation =>
        IsSessionCombat &&
        CanManageOwnSessionParticipant;

    private bool OrientationDisabled =>
        !CanShowOrientation ||
        OwnSessionParticipant?.CurrentInitiative is null ||
        OpenOrientationAction is not null ||
        _rollBusy ||
        _valueMutationBusy ||
        HasOpenAttackExchange;

    private CombatSessionActionDto? OpenOrientationAction =>
        IsSessionCombat && OwnSessionParticipant is { } participant
            ? GetParticipantActions(participant.Id)
                .FirstOrDefault(action => IsOrientationAction(action) &&
                                          action.State == CombatActionEntryState.Open)
            : null;

    private CombatSessionActionDto? CurrentOrientationAction =>
        IsSessionCombat && OwnSessionParticipant is { } participant
            ? GetParticipantActions(participant.Id)
                .FirstOrDefault(action => IsOrientationAction(action))
            : null;

    private IReadOnlyList<CombatSessionActionDto> CurrentSessionActions =>
        SessionCombat?.Actions
            .Where(action => action.Round == (SessionCombat?.Round ?? 1) && action.State != CombatActionEntryState.Completed)
            .ToArray() ?? Array.Empty<CombatSessionActionDto>();

    private IReadOnlySet<string> NextInitiativeParticipantIds
    {
        get
        {
            var snapshot = SessionCombat;
            if (snapshot is null || HasOpenAttackExchange || !snapshot.CurrentActionIds.Any())
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
        EnsureCombatViewState();
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

    private void HandleStateChanged()
    {
        EnsureCombatViewState();
        _ = InvokeAsync(StateHasChanged);
    }

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

    private void EnsureCombatViewState()
    {
        var sessionId = SessionState.ActiveSessionId;
        if (!string.Equals(_viewSessionId, sessionId, StringComparison.Ordinal))
        {
            _viewSessionId = sessionId;
            _combatViewInitialized = false;
            _isMasterView = false;
        }

        if (!IsSessionCombat)
        {
            _isMasterView = false;
            _combatViewInitialized = true;
            return;
        }

        if (_combatViewInitialized)
        {
            if (!IsSessionMaster)
            {
                _isMasterView = false;
            }

            return;
        }

        if (SessionState.ActiveSession is null || SessionCombat is null)
        {
            return;
        }

        _isMasterView = IsSessionMaster && !HasOwnSessionHero;
        _combatViewInitialized = true;
    }

    private void ToggleCombatView()
    {
        if (!CanSwitchCombatView)
        {
            return;
        }

        _isMasterView = !_isMasterView;
        _notice = null;
    }

    private async Task HandleSetSelected(string setId)
    {
        await CombatState.SetSelectedSetAsync(setId);
        if (SelectedWeapon?.Category == CombatWeaponCategory.Ranged)
        {
            await CombatState.SetSelectedActionAsync(CombatActionIds.Ranged);
        }
        else if (SelectedWeapon?.Category == CombatWeaponCategory.Shield)
        {
            await CombatState.SetSelectedActionAsync(CombatActionIds.ShieldParry);
        }
        else if (SelectedAction is CombatActionIds.Ranged or CombatActionIds.ShieldParry)
        {
            await CombatState.SetSelectedActionAsync(CombatActionIds.Attack);
        }

        await SyncSessionRuntimeStateAsync(setId);
    }

    private async Task HandleWeaponSelected(string weaponId)
    {
        await CombatState.SetSelectedWeaponAsync(weaponId);
        if (SelectedWeapon?.Category == CombatWeaponCategory.Ranged)
        {
            await CombatState.SetSelectedActionAsync(CombatActionIds.Ranged);
        }
        else if (SelectedWeapon?.Category == CombatWeaponCategory.Shield)
        {
            await CombatState.SetSelectedActionAsync(CombatActionIds.ShieldParry);
        }
        else if (SelectedAction is CombatActionIds.Ranged or CombatActionIds.ShieldParry)
        {
            await CombatState.SetSelectedActionAsync(CombatActionIds.Attack);
        }
    }

    private async Task HandleActionSelected(string action)
    {
        var actionKind = GetActionKind(action);
        if (HasOpenAttackExchange &&
            (action == CombatActionIds.Initiative || actionKind is not { } kind || !CanUseActionDuringOpenExchange(kind)))
        {
            _notice = OpenExchangeNotice;
            return;
        }

        await CombatState.SetSelectedActionAsync(action);
    }

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
        if (IsMasterOpponentMode)
        {
            await HandleOpponentAttackRequested();
            return;
        }

        if (IsAttributeMode)
        {
            await HandleAttributeRollRequested();
            return;
        }

        if (SelectedAction == CombatActionIds.Initiative)
        {
            await RollInitiativeFromStatusAsync();
            return;
        }

        if (!CanRoll || GetActionKind() is not { } action)
        {
            _notice = "Bitte zuerst ein Kampfset, eine passende Waffe und eine verfügbare Aktion wählen.";
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
        if (IsSessionCombat &&
            CombatActionBudgetRules.RequiresReaction(action) &&
            SessionCombat?.ActiveExchange is { } exchange &&
            OwnSessionParticipant is { } ownTarget &&
            string.Equals(exchange.TargetParticipantId, ownTarget.Id, StringComparison.Ordinal))
        {
            return BuildDefenseRollRequest(exchange, ownTarget, action);
        }

        var modifiers = action == CombatActionKind.Damage || Modifier == 0
            ? Array.Empty<CombatModifierDto>()
            : [new CombatModifierDto("Situativ", Modifier, "Kampfseite")];
        return new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = SessionState.ActiveSessionId,
            ParticipantId = IsSessionCombat ? OwnSessionParticipant?.Id : null,
            TargetParticipantId = action is CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack
                ? SelectedTargetParticipantId
                : action is CombatActionKind.WeaponParry or CombatActionKind.ShieldParry or CombatActionKind.Dodge
                    ? SessionCombat?.ActiveExchange?.AttackerParticipantId
                    : null,
            ActionId = IsSessionCombat
                ? IsAttackAction(action)
                    ? GetSessionAttackAction(action)?.Id
                    : SessionCombat?.ActiveExchange?.ActionId
                : null,
            ExpectedRevision = IsSessionCombat ? SessionCombat?.Revision : null,
            HeroId = ActiveHero?.Id,
            SetId = SelectedSet?.Id,
            ExchangeId = IsSessionCombat && action is
                (CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack or
                 CombatActionKind.WeaponParry or CombatActionKind.ShieldParry or CombatActionKind.Dodge or
                 CombatActionKind.HitZone or CombatActionKind.Damage)
                ? SessionCombat?.ActiveExchange?.ExchangeId
                : null,
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
            ResolvedZone = action == CombatActionKind.Damage
                ? SessionCombat?.ActiveExchange?.Zone ??
                  (CombatResult?.Snapshot.Action == CombatActionKind.HitZone
                      ? CombatResult.Snapshot.Zone
                      : null)
                : null,
            Zone = action == CombatActionKind.HitZone
                ? new CombatZoneRollRequestDto(Facing, CombatArmorZone.LeftArm, CombatArmorZone.RightArm)
                : null,
            Facing = Facing,
            Helper = action switch
            {
                CombatActionKind.WoundHelper => new CombatHelperRollRequestDto { DiceCount = 1, DiceSides = 6, Purpose = "Wund-Hilfswurf" },
                CombatActionKind.FumbleHelper => new CombatHelperRollRequestDto { DiceCount = 1, DiceSides = 20, Purpose = "Patzer-Hilfswurf" },
                _ => null
            },
            Note = string.IsNullOrWhiteSpace(RollText) ? null : RollText.Trim()
        };
    }

    private CombatRollRequestDto BuildDefenseRollRequest(
        CombatAttackExchangeDto exchange,
        CombatSessionParticipantDto target,
        CombatActionKind action)
    {
        var targetIsOpponent = target.Kind == CombatParticipantKind.Opponent;
        var weapon = !targetIsOpponent && action is
            (CombatActionKind.WeaponParry or CombatActionKind.ShieldParry)
            ? SelectedWeapon
            : null;

        return new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = SessionState.ActiveSessionId,
            ParticipantId = target.Id,
            TargetParticipantId = exchange.AttackerParticipantId,
            ActionId = exchange.ActionId,
            ExpectedRevision = SessionCombat?.Revision,
            HeroId = targetIsOpponent ? null : target.HeroId,
            SetId = targetIsOpponent ? null : target.InitiativeSetId ?? SelectedSet?.Id,
            ExchangeId = exchange.ExchangeId,
            Action = action,
            WeaponId = weapon?.Id,
            WeaponName = weapon?.Name,
            RuntimeState = target.RuntimeState,
            Modifiers = Modifier == 0
                ? []
                : [new CombatModifierDto("Situativ", Modifier, "Kampfseite")],
            Options = new CombatRuleOptionsDto(SpecialResultsEnabled: true, LowLePEnabled: true),
            Facing = exchange.Facing,
            Note = string.IsNullOrWhiteSpace(RollText) ? null : RollText.Trim()
        };
    }

    private bool CanRollOpenSessionAttack(CombatActionKind action)
    {
        if (!IsSessionCombat || action is not (CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack))
        {
            return false;
        }

        return SessionCombat?.ActiveExchange is { Status: CombatExchangeStatus.Declared } exchange &&
               exchange.AttackerParticipantId == OwnSessionParticipant?.Id &&
               exchange.AttackKind == action;
    }

    private CombatSessionActionDto? GetSessionAttackAction(CombatActionKind action)
    {
        if (!IsSessionCombat || OwnSessionParticipant is not { } participant)
        {
            return null;
        }

        if (SessionCombat?.ActiveExchange is { Status: CombatExchangeStatus.Declared } exchange &&
            exchange.AttackerParticipantId == participant.Id &&
            exchange.AttackKind == action)
        {
            return GetParticipantActions(participant.Id)
                .FirstOrDefault(current => current.Id == exchange.ActionId);
        }

        return GetParticipantActions(participant.Id).FirstOrDefault(current =>
            !current.IsReaction &&
            !IsOrientationAction(current) &&
            current.State is (CombatActionEntryState.Open or CombatActionEntryState.Held) &&
            (!current.IsAdditional || current.ActionKind is null || current.ActionKind == action));
    }

    private Task HandleTargetSelected(string participantId)
    {
        if (!HasOpenAttackExchange && TargetParticipants.Any(participant => participant.Id == participantId))
        {
            _selectedTargetParticipantId = participantId;
        }

        return Task.CompletedTask;
    }

    private Task HandleOpponentParticipantSelected(string participantId)
    {
        if (OpponentParticipants.Any(participant => participant.Id == participantId))
        {
            _selectedActingOpponentId = participantId;
            _selectedOpponentAttackId = null;
            _selectedOpponentTargetId = null;
        }

        return Task.CompletedTask;
    }

    private Task HandleOpponentTargetSelected(string participantId)
    {
        if (OpponentTargetParticipants.Any(participant => participant.Id == participantId))
        {
            _selectedOpponentTargetId = participantId;
        }

        return Task.CompletedTask;
    }

    private Task HandleOpponentAttackSelected(string attackId)
    {
        if (OpponentAttacks.Any(attack => attack.Id == attackId))
        {
            _selectedOpponentAttackId = attackId;
        }

        return Task.CompletedTask;
    }

    private async Task HandleOpponentAttackRequested()
    {
        if (!CanRollOpponentAttack ||
            SelectedOpponentParticipant is not { } attacker ||
            SelectedOpponentTargetId is not { } targetId ||
            SelectedOpponentAttack is not { } attack ||
            SelectedOpponentAction is not { } action ||
            SelectedOpponentActionKind is not { } actionKind)
        {
            _notice = "Bitte einen handlungsfähigen Gegner, ein Ziel und einen konkreten Angriff wählen.";
            return;
        }

        _rollBusy = true;
        _notice = null;
        try
        {
            var request = new CombatRollRequestDto
            {
                RequestId = Guid.NewGuid(),
                SessionId = SessionCombat?.SessionId,
                ParticipantId = attacker.Id,
                TargetParticipantId = targetId,
                ActionId = action.Id,
                ExpectedRevision = SessionCombat?.Revision,
                Action = actionKind,
                WeaponId = attack.Id,
                WeaponName = attack.Name,
                RuntimeState = attacker.RuntimeState,
                Modifiers = Modifier == 0
                    ? []
                    : [new CombatModifierDto("Situativ", Modifier, "Kampfseite")],
                Options = new CombatRuleOptionsDto(SpecialResultsEnabled: true, LowLePEnabled: true),
                Facing = Facing,
                Note = string.IsNullOrWhiteSpace(RollText) ? null : RollText.Trim()
            };
            await CombatCoordinator.RollAsync(request);
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

    private async Task HandleExchangeActionAsync(CombatActionKind action)
    {
        if (SessionCombat?.ActiveExchange is not { } exchange)
        {
            return;
        }

        if (action is not (CombatActionKind.WeaponParry or CombatActionKind.ShieldParry or CombatActionKind.Dodge))
        {
            return;
        }

        if (!(exchange.AllowedDefenseActions ?? []).Contains(action))
        {
            return;
        }

        var target = SessionCombat.Participants.FirstOrDefault(participant =>
            string.Equals(participant.Id, exchange.TargetParticipantId, StringComparison.Ordinal));
        if (target is null ||
            (target.Kind == CombatParticipantKind.Opponent
                ? !CanResolveActiveExchange
                : !CanRespondToExchange))
        {
            return;
        }

        var request = BuildDefenseRollRequest(exchange, target, action);

        _rollBusy = true;
        _notice = null;
        try
        {
            await CombatCoordinator.RollAsync(request);
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
            var result = IsSessionCombat
                ? await SyncOwnSessionRuntimeEditAsync(runtime => change.Resource switch
                {
                    CombatResourceKind.LeP => runtime with { CurrentLeP = change.Value },
                    CombatResourceKind.AuP => runtime with { CurrentAuP = change.Value },
                    _ => runtime
                })
                : null;
            if (!IsSessionCombat)
            {
                await CombatState.SetResourceAsync(change.Resource, change.Value);
            }

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
            var value = Math.Clamp(change.Value.Value, 0, 3);
            var result = IsSessionCombat
                ? await SyncOwnSessionRuntimeEditAsync(runtime => runtime with
                {
                    Wounds = WithWound(runtime.Wounds, change.Zone, value)
                })
                : null;
            if (!IsSessionCombat)
            {
                await CombatState.SetWoundAsync(change.Zone, value);
            }

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
        if (HasOpenAttackExchange)
        {
            _notice = OpenExchangeNotice;
            return;
        }

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
                var snapshot = SessionCombat;
                var participant = OwnSessionParticipant;
                if (snapshot is null || participant is null)
                {
                    _notice = "Der aktuelle Session-Kampfstand ist noch nicht geladen.";
                    return;
                }

                var result = await CombatSessionState.SetInitiativeAsync(
                    initiative.Value,
                    participant.Id,
                    participant.HeroId,
                    participant.RuntimeState,
                    participant.InitiativeSetId,
                    expectedRevision: snapshot.Revision);
                if (result.Stale || !result.Applied)
                {
                    _notice = result.Message;
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
                var snapshot = SessionCombat;
                var participant = OwnSessionParticipant;
                if (snapshot is null || participant is null)
                {
                    _notice = "Der aktuelle Session-Kampfstand ist noch nicht geladen.";
                    return;
                }

                var sessionResult = await CombatSessionState.RollInitiativeAsync(
                    participant.Id,
                    participant.HeroId,
                    participant.RuntimeState,
                    participant.InitiativeSetId,
                    Modifier,
                    expectedRevision: snapshot.Revision);
                var updatedParticipant = sessionResult.Snapshot.Participants
                    .FirstOrDefault(current => current.HeroId == ActiveHero?.Id);
                if (updatedParticipant?.CurrentInitiative.HasValue == true)
                {
                    await CombatState.SetSelectedActionAsync(GetDefaultCombatAction());
                }

                if (sessionResult.Stale || !sessionResult.Applied)
                {
                    _notice = sessionResult.Message;
                }

                return;
            }

            var baseInitiative = SelectedSet?.Initiative ?? 0;
            var runtime = CombatRuntimeModifierRules.ResolveInitiative(Profile, SelectedSet, BuildRuntimeState());
            var initiativeModifiers = new List<CombatModifierDto>();
            if (Modifier != 0)
            {
                initiativeModifiers.Add(new CombatModifierDto("Situativ", Modifier, "Kampfseite"));
            }

            if (runtime.Modifier != 0)
            {
                initiativeModifiers.Add(new CombatModifierDto("Automatisch", runtime.Modifier, "Kampf"));
            }

            var result = await CombatCoordinator.RollAsync(new CombatRollRequestDto
            {
                RequestId = Guid.NewGuid(),
                SessionId = SessionState.ActiveSessionId,
                HeroId = ActiveHero?.Id,
                SetId = SelectedSet?.Id,
                Action = CombatActionKind.InitiativeHelper,
                RuntimeState = BuildRuntimeState(),
                BaseValue = baseInitiative,
                Modifiers = initiativeModifiers.ToArray(),
                Helper = new CombatHelperRollRequestDto
                {
                    DiceCount = Profile?.HasKlingentaenzer == true ? 2 : 1,
                    DiceSides = 6,
                    Purpose = "INI-Startwurf"
                }
            });
            var total = baseInitiative + result.Rolls.Sum(roll => roll.Value) + runtime.Modifier + Modifier;
            await CombatState.SetInitiativeAsync(total);
            await CombatState.SetSelectedActionAsync(GetDefaultCombatAction());
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

    private async Task HandleParticipantInitiativeChanged(
        CombatInitiativeOverview.ParticipantInitiativeChange change)
    {
        var participant = change.Participant;
        var initiative = change.Initiative;
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
            var snapshot = SessionCombat;
            var currentParticipant = snapshot?.Participants.FirstOrDefault(item =>
                string.Equals(item.Id, participant.Id, StringComparison.Ordinal));
            if (snapshot is null || currentParticipant is null || !CanEditParticipantInitiative(currentParticipant))
            {
                _notice = "Die Initiative dieses Teilnehmers kann nicht geändert werden.";
                return;
            }

            var result = await CombatSessionState.SetInitiativeAsync(
                initiative.Value,
                currentParticipant.Id,
                currentParticipant.HeroId,
                currentParticipant.RuntimeState,
                currentParticipant.InitiativeSetId,
                expectedRevision: snapshot.Revision);
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
        if (!IsSessionCombat || HasOpenAttackExchange || participant.HeroId == ActiveHero?.Id)
        {
            return false;
        }

        return IsMasterView || string.Equals(
            participant.OwnerUserId,
            AuthState.Current.User?.Id,
            StringComparison.Ordinal);
    }

    private bool CanRollParticipantInitiative(CombatSessionParticipantDto participant) =>
        IsMasterView &&
        !HasOpenAttackExchange &&
        participant.Kind == CombatParticipantKind.Opponent &&
        !participant.CurrentInitiative.HasValue &&
        participant.InitiativeBase.HasValue;

    private async Task RollParticipantInitiativeAsync(CombatSessionParticipantDto participant)
    {
        if (!CanRollParticipantInitiative(participant) || _rollBusy)
        {
            return;
        }

        _rollBusy = true;
        _notice = null;
        try
        {
            var result = await CombatSessionState.RollInitiativeAsync(
                participant.Id,
                runtimeState: participant.RuntimeState,
                initiativeCorrection: participant.InitiativeCorrection);
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
            _rollBusy = false;
        }
    }

    private bool CanRemoveOpponent(CombatSessionParticipantDto participant) =>
        IsMasterView && !HasOpenAttackExchange && participant.Kind == CombatParticipantKind.Opponent;

    private bool CanManageParticipantControls(CombatSessionParticipantDto participant)
    {
        if (!IsSessionCombat)
        {
            return false;
        }

        if (IsMasterView)
        {
            return true;
        }

        return participant.Kind == CombatParticipantKind.Hero &&
               participant.HeroId == ActiveHero?.Id &&
               string.Equals(participant.OwnerUserId,
                   AuthState.Current.User?.Id,
                   StringComparison.Ordinal);
    }

    private bool CanManageOwnSessionParticipant => OwnSessionParticipant is not null &&
                                                   CanManageParticipantControls(OwnSessionParticipant);

    private async Task RemoveOpponentAsync(CombatSessionParticipantDto participant)
    {
        if (!CanRemoveOpponent(participant))
        {
            return;
        }

        try
        {
            var result = await CombatSessionState.RemoveOpponentAsync(participant.Id);
            _notice = result.Message;
        }
        catch (Exception exception)
        {
            _notice = exception.Message;
        }
    }

    private string? GetParticipantStatus(CombatSessionParticipantDto participant)
    {
        if (participant.Kind != CombatParticipantKind.Opponent ||
            participant.OpponentProfile is not { } opponent)
        {
            return null;
        }

        var catalog = opponent.CatalogProfile;
        var runtime = participant.RuntimeState;
        var status = new List<string>
        {
            FormatOpponentLeP(opponent, runtime),
            FormatOpponentAuP(catalog, runtime),
            $"Wunden {CountWounds(runtime)}",
            "Schmerz —",
            $"RS {opponent.ArmorRating?.ToString() ?? catalog?.Armor.TotalRs?.ToString() ?? "—"}"
        };

        if (catalog is { CombatReady: false })
        {
            status.Add("noch nicht kampfbereit");
        }

        return string.Join(" · ", status);
    }

    private static string FormatOpponentLeP(
        CombatOpponentProfileDto opponent,
        CombatRuntimeStateDto? runtime)
    {
        var current = runtime?.CurrentLeP ?? opponent.LeP;
        var catalog = opponent.CatalogProfile;
        if (current.HasValue && catalog?.LePSourceMin is { } min && catalog.LePSourceMax is { } max)
        {
            return $"LeP {current} ({min}–{max})";
        }

        return current.HasValue && opponent.LeP.HasValue
            ? $"LeP {current}/{opponent.LeP}"
            : "LeP —";
    }

    private static string FormatOpponentAuP(
        CombatEnemyResolvedProfileDto? catalog,
        CombatRuntimeStateDto? runtime)
    {
        var current = runtime?.CurrentAuP ?? catalog?.AuP;
        return current.HasValue && catalog?.AuP.HasValue == true
            ? $"AuP {current}/{catalog.AuP}"
            : "AuP —";
    }

    private static int CountWounds(CombatRuntimeStateDto? runtime) =>
        runtime?.Wounds.Values.Count(value => value.GetValueOrDefault() > 0) ?? 0;

    private void OpenResourceDrawer(CombatResourceKind resource)
    {
        _resourceDrawerKind = resource;
        _resourceDraft = GetResourceValue(resource);
        _drawer = CombatDrawer.Resource;
    }

    private Task HandleResourceDraftChanged(int? value)
    {
        _resourceDraft = value;
        return Task.CompletedTask;
    }

    private async Task ApplyResourceDrawerAsync()
    {
        if (_resourceDrawerKind is not { } resource)
        {
            return;
        }

        await HandleResourceValueChanged(new CombatStatusPanel.ResourceValueChange(resource, _resourceDraft));
        if (!_valueMutationBusy)
        {
            CloseDrawer();
        }
    }

    private void OpenOrientationDrawer()
    {
        if (HasOpenAttackExchange || (IsSessionCombat && !CanManageOwnSessionParticipant))
        {
            return;
        }

        _orientationReliefDraft = Profile?.KriegskunstValue is { } kriegskunst
            ? Math.Max(0, kriegskunst / 2)
            : 0;
        _orientationUninterruptedDraft = true;
        _drawer = CombatDrawer.Orientation;
    }

    private async Task OpenOpponentDrawer()
    {
        if (!IsMasterView || HasOpenAttackExchange)
        {
            return;
        }

        _participantDrawerMode = CombatParticipantDrawerMode.Opponent;
        _opponentNameDraft = string.Empty;
        _opponentAffiliationDraft = "Gegner";
        _opponentInitiativeBaseDraft = null;
        _opponentInitiativeDraft = null;
        _selectedEnemyId = null;
        _selectedEnemyVariantId = null;
        _selectedEnemyAttackId = null;
        _selectedEnemyWeaponOption = null;
        _selectedEnemyArmorOption = null;
        _selectedEnemyLeP = null;
        _drawer = CombatDrawer.Participant;

        if (_enemyCatalog.Length > 0)
        {
            return;
        }

        _enemyCatalogLoading = true;
        _notice = null;
        try
        {
            _enemyCatalog = await WuerfelApiClient.GetCombatEnemyCatalogAsync();
        }
        catch (Exception exception)
        {
            _notice = exception.Message;
        }
        finally
        {
            _enemyCatalogLoading = false;
        }
    }

    private void OpenAnnouncementDrawer()
    {
        if (!IsMasterView)
        {
            return;
        }

        _participantDrawerMode = CombatParticipantDrawerMode.Announcement;
        _initiativeParticipantId = OwnSessionParticipant?.Id;
        _initiativeHeroId = ActiveHero?.Id;
        _announcementDraft = OwnSessionParticipant?.Announcement ?? string.Empty;
        _drawer = CombatDrawer.Participant;
    }

    private void CloseDrawer()
    {
        _drawer = CombatDrawer.None;
        _resourceDrawerKind = null;
    }

    private async Task InitializeCombatStateAsync()
    {
        if (IsSessionCombat)
        {
            if (Profile is null || SessionCombat is null || OwnSessionParticipant is null)
            {
                _notice = "Der aktuelle Session-Kampfstand ist noch nicht geladen.";
                return;
            }

            var initializationResult = await SyncOwnSessionRuntimeEditAsync(runtime => runtime with
            {
                IsStarted = true,
                CurrentLeP = runtime.CurrentLeP ?? Profile.Resources.LeP,
                CurrentAuP = runtime.CurrentAuP ?? Profile.Resources.AuP,
                Wounds = runtime.Wounds.Count == 0
                    ? Enum.GetValues<CombatWoundZone>().ToDictionary(zone => zone, _ => (int?)0)
                    : runtime.Wounds
            });
            _notice = initializationResult?.Stale == true
                ? initializationResult.Message
                : "Laufende Kampfwerte mit den Maximalwerten initialisiert.";
            return;
        }

        if (!await CombatState.StartCombatAsync())
        {
            _notice = "Für die Initialisierung fehlen die Kampfdaten.";
            return;
        }

        var sessionResult = await SyncSessionRuntimeStateAsync(runtimeState: BuildLocalRuntimeState());
        _notice = sessionResult?.Stale == true
            ? sessionResult.Message
            : "Laufende Kampfwerte mit den Maximalwerten initialisiert.";
    }

    private async Task ApplyParticipantDrawerAsync()
    {
        if (!IsMasterView)
        {
            return;
        }

        CombatSessionMutationResultDto result;
        if (_participantDrawerMode == CombatParticipantDrawerMode.Opponent)
        {
            if (!CanApplyOpponentDrawer)
            {
                _notice = "Bitte das Gegnerprofil und alle erforderlichen Auswahlwerte festlegen.";
                return;
            }

            result = IsManualOpponentSelection
                ? await CombatSessionState.AddOpponentAsync(
                    _opponentNameDraft,
                    _opponentInitiativeBaseDraft,
                    _opponentInitiativeDraft,
                    _opponentAffiliationDraft)
                : await CombatSessionState.AddCatalogEnemyAsync(
                    new CombatEnemySelectionDto
                    {
                        EnemyId = _selectedEnemyId!,
                        VariantId = _selectedEnemyVariantId,
                        AttackId = _selectedEnemyAttackId,
                        WeaponOption = _selectedEnemyWeaponOption,
                        ArmorOption = _selectedEnemyArmorOption,
                        LeP = _selectedEnemyLeP
                    },
                    affiliation: _opponentAffiliationDraft);
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

    private Task HandleEnemySelectionChanged(ChangeEventArgs args)
    {
        _selectedEnemyId = args.Value?.ToString();
        _selectedEnemyVariantId = null;
        _selectedEnemyAttackId = null;
        _selectedEnemyWeaponOption = null;
        _selectedEnemyArmorOption = null;
        _selectedEnemyLeP = null;
        return Task.CompletedTask;
    }

    private Task HandleEnemyVariantChanged(ChangeEventArgs args)
    {
        _selectedEnemyVariantId = args.Value?.ToString();
        _selectedEnemyLeP = null;
        return Task.CompletedTask;
    }

    private Task HandleEnemyAttackChanged(ChangeEventArgs args)
    {
        _selectedEnemyAttackId = args.Value?.ToString();
        return Task.CompletedTask;
    }

    private Task HandleEnemyWeaponChanged(ChangeEventArgs args)
    {
        _selectedEnemyWeaponOption = args.Value?.ToString();
        return Task.CompletedTask;
    }

    private Task HandleEnemyArmorChanged(ChangeEventArgs args)
    {
        _selectedEnemyArmorOption = args.Value?.ToString();
        return Task.CompletedTask;
    }

    private Task HandleEnemyLePChanged(int? value)
    {
        _selectedEnemyLeP = value;
        return Task.CompletedTask;
    }

    private string GetSelectedEnemyInitiativeLabel()
    {
        var combat = SelectedEnemyCombat;
        return combat?.Initiative.Notation is { Length: > 0 } notation
            ? $"INI {notation}"
            : "INI —";
    }

    private string GetSelectedEnemyLePLabel()
    {
        var resource = SelectedEnemyCombat?.Resources.LeP;
        if (resource?.SourceRange is { Min: { } min, Max: { } max })
        {
            return $"LeP {min}–{max}";
        }

        if (resource?.Initial is { } initial)
        {
            return $"LeP {initial}";
        }

        return resource?.Maximum is { } maximum
            ? $"LeP {maximum}"
            : "LeP —";
    }

    private string GetSelectedEnemyDefenseLabel()
    {
        var defense = SelectedEnemyCombat?.Defense;
        if (defense is null)
        {
            return "Abwehr —";
        }

        var value = defense.ParryValue?.ToString() ?? "—";
        return string.Equals(defense.Mode, "masterfulEvasion", StringComparison.OrdinalIgnoreCase)
            ? $"AW {value}"
            : $"PA {value}";
    }

    private string GetSelectedEnemyReadinessLabel()
    {
        var enemy = SelectedEnemyCatalogEntry;
        if (enemy is null)
        {
            return string.Empty;
        }

        if (enemy.CombatVariants.Length > 0 && string.IsNullOrWhiteSpace(_selectedEnemyVariantId))
        {
            return "Erfahrungsvariante auswählen.";
        }

        if (enemy.Attacks.Length > 1 && string.IsNullOrWhiteSpace(_selectedEnemyAttackId))
        {
            return "Konkreten Angriff auswählen.";
        }

        if (enemy.Equipment?.SelectionRequired == true &&
            (string.IsNullOrWhiteSpace(_selectedEnemyWeaponOption) ||
             string.IsNullOrWhiteSpace(_selectedEnemyArmorOption)))
        {
            return "Waffe und Rüstung auswählen.";
        }

        if (SelectedEnemyLePRange is not null && !_selectedEnemyLeP.HasValue)
        {
            return "LeP braucht eine Meisterentscheidung.";
        }

        if (enemy.CombatReady)
        {
            return "Kampfbereit nach Katalogwerten.";
        }

        return enemy.Readiness switch
        {
            "sourceDependent" => "LeP braucht eine Meisterentscheidung.",
            "requiresEquipmentSelection" => "Erfahrungsstufe, Waffe und Rüstung auswählen; TP/RS bleiben bei fehlenden Katalogwerten offen.",
            _ => "Für den Kampfeinsatz sind noch Auswahlwerte erforderlich."
        };
    }

    private string GetSelectedEnemySourceLabel()
    {
        var sourceRefs = SelectedEnemyCatalogEntry?.SourceRefs ?? [];
        return sourceRefs.Length == 0
            ? "Quelle: nicht angegeben"
            : $"Quelle: {string.Join(", ", sourceRefs.Select(FormatSourceReference))}";
    }

    private static string GetEnemyAttackLabel(CombatEnemyAttackDto attack)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(attack.DistanceClass))
        {
            details.Add(attack.DistanceClass!);
        }

        if (!string.IsNullOrWhiteSpace(attack.Damage?.Notation))
        {
            details.Add($"TP {attack.Damage.Notation}");
        }

        return details.Count == 0
            ? attack.Name
            : $"{attack.Name} · {string.Join(" · ", details)}";
    }

    private static string FormatSourceReference(CombatEnemySourceReferenceDto source)
    {
        var page = source.PrintedPage?.ToString() ??
                   (source.PrintedPages.Length > 0 ? string.Join(", ", source.PrintedPages) : null);
        return string.IsNullOrWhiteSpace(page) ? source.SourceId : $"{source.SourceId} S. {page}";
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
        if (HasOpenAttackExchange)
        {
            _notice = OpenExchangeNotice;
            return;
        }

        var participant = SessionCombat?.Participants.FirstOrDefault(item => item.Id == action.ParticipantId);
        if (participant is null || !CanManageParticipantControls(participant))
        {
            return;
        }

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
        if (HasOpenAttackExchange)
        {
            _notice = OpenExchangeNotice;
            return;
        }

        if (!CanManageParticipantControls(participant))
        {
            return;
        }

        var result = await CombatSessionState.ConsumeReactionAsync(participant.Id, participant.HeroId);
        _notice = result.Message;
    }

    private async Task HoldCurrentActionAsync()
    {
        if (!CanHoldAction || OwnSessionParticipant is not { } participant)
        {
            return;
        }

        var action = GetParticipantActions(participant.Id).FirstOrDefault(current =>
            current.Round == SessionCombat?.Round &&
            !current.IsReaction &&
            !current.IsAdditional &&
            current.State == CombatActionEntryState.Open);
        if (action is null)
        {
            _notice = "Es gibt keine offene normale Handlung zum Abwarten.";
            return;
        }

        await HoldActionAsync(action);
    }

    private async Task HoldActionAsync(CombatSessionActionDto action)
    {
        if (HasOpenAttackExchange)
        {
            _notice = OpenExchangeNotice;
            return;
        }

        var participant = SessionCombat?.Participants.FirstOrDefault(item => item.Id == action.ParticipantId);
        if (participant is null || !CanManageParticipantControls(participant))
        {
            return;
        }

        var result = await CombatSessionState.HoldActionAsync(action.Id, action.ParticipantId);
        _notice = result.Message;
    }

    private async Task ExecuteHeldActionAsync(CombatSessionActionDto action)
    {
        if (HasOpenAttackExchange)
        {
            _notice = OpenExchangeNotice;
            return;
        }

        var participant = SessionCombat?.Participants.FirstOrDefault(item => item.Id == action.ParticipantId);
        if (participant is null || !CanManageParticipantControls(participant))
        {
            return;
        }

        var result = await CombatSessionState.ExecuteHeldActionAsync(action.Id, action.ParticipantId);
        _notice = result.Message;
    }

    private async Task NewRoundAsync()
    {
        if (!IsMasterView)
        {
            return;
        }

        if (HasOpenAttackExchange)
        {
            _notice = OpenExchangeNotice;
            return;
        }

        var result = await CombatSessionState.NewRoundAsync();
        _notice = result.Message;
    }

    private async Task OrientAsync(bool hasAttention)
    {
        if (HasOpenAttackExchange)
        {
            _notice = OpenExchangeNotice;
            return;
        }

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
        if (!CanManageOwnSessionParticipant || OwnSessionParticipant?.CurrentInitiative is null)
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
        var participant = SessionCombat?.Participants.FirstOrDefault(item => item.Id == action.ParticipantId);
        if (HasOpenAttackExchange)
        {
            _notice = OpenExchangeNotice;
            return;
        }

        if (participant is null || !CanManageParticipantControls(participant))
        {
            return;
        }

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
        if (HasOpenAttackExchange &&
            string.Equals(GetPendingExchangeParticipantId(), participant.Id, StringComparison.Ordinal))
        {
            return true;
        }

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

        if (HasOpenAttackExchange)
        {
            return participant.CurrentInitiative.HasValue ? "Wartet" : "INI fehlt";
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

    private string? GetPendingExchangeParticipantId() => SessionCombat?.ActiveExchange is not { } exchange
        ? null
        : exchange.Status == CombatExchangeStatus.DefenseOpen
            ? exchange.TargetParticipantId
            : exchange.AttackerParticipantId;

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

    private static bool IsAttackAction(CombatActionKind? action) => action is
        CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack;

    private IReadOnlyList<CombatSessionActionDto> GetParticipantActions(string participantId)
    {
        return SessionCombat?.Actions
            .Where(action => action.ParticipantId == participantId && action.Round == SessionCombat.Round)
            .OrderBy(action => action.State == CombatActionEntryState.Completed)
            .ThenByDescending(action => action.PhaseInitiative ?? int.MinValue)
            .ToArray() ?? Array.Empty<CombatSessionActionDto>();
    }

    private static CombatActionKind? GetActionKind(string? action) => CombatActionIds.ToKind(action);

    private string GetDefaultCombatAction() => SelectedWeapon?.Category == CombatWeaponCategory.Ranged
        ? CombatActionIds.Ranged
        : CombatActionIds.Attack;

    private CombatActionKind? GetActionKind() => GetActionKind(SelectedAction);

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

    private CombatRuntimeStateDto? BuildRuntimeState() =>
        IsSessionCombat ? SessionRuntimeState : BuildLocalRuntimeState();

    private CombatRuntimeStateDto BuildLocalRuntimeState() => new()
    {
        IsStarted = CombatState.IsStarted,
        CurrentLeP = CombatState.CurrentLeP,
        CurrentAuP = CombatState.CurrentAuP,
        Wounds = CombatState.Wounds.ToDictionary(pair => pair.Key, pair => pair.Value)
    };

    private async Task<CombatSessionMutationResultDto?> SyncOwnSessionRuntimeEditAsync(
        Func<CombatRuntimeStateDto, CombatRuntimeStateDto> edit)
    {
        var snapshot = SessionCombat;
        var participant = OwnSessionParticipant;
        if (!IsSessionCombat || snapshot is null || participant is null || ActiveHero is null)
        {
            return null;
        }

        var current = participant.RuntimeState is { } runtime
            ? runtime with
            {
                Wounds = (runtime.Wounds ?? new Dictionary<CombatWoundZone, int?>())
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
            }
            : new CombatRuntimeStateDto { IsStarted = snapshot.IsStarted };
        var next = edit(current);
        return await CombatSessionState.SyncRuntimeStateAsync(
            next,
            participant.Id,
            participant.HeroId ?? ActiveHero.Id,
            participant.InitiativeSetId,
            expectedRevision: snapshot.Revision);
    }

    private static Dictionary<CombatWoundZone, int?> WithWound(
        IReadOnlyDictionary<CombatWoundZone, int?>? wounds,
        CombatWoundZone zone,
        int value)
    {
        var next = wounds?.ToDictionary(pair => pair.Key, pair => pair.Value)
                   ?? new Dictionary<CombatWoundZone, int?>();
        next[zone] = value;
        return next;
    }

    private async Task<CombatSessionMutationResultDto?> SyncSessionRuntimeStateAsync(
        string? setId = null,
        CombatRuntimeStateDto? runtimeState = null)
    {
        if (!IsSessionCombat || OwnSessionParticipant is not { } participant || ActiveHero is null)
        {
            return null;
        }

        var state = runtimeState ?? BuildRuntimeState();
        if (state is null)
        {
            return null;
        }

        return await CombatSessionState.SyncRuntimeStateAsync(
            state,
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

    private static string GetResourceLabel(CombatResourceKind resource) => resource switch
    {
        CombatResourceKind.LeP => "LeP",
        CombatResourceKind.AuP => "AuP",
        CombatResourceKind.AeP => "AeP",
        CombatResourceKind.KeP => "KE",
        _ => resource.ToString()
    };

    private static int? GetResourceMinimum(CombatResourceKind resource) => resource == CombatResourceKind.LeP ? null : 0;

    private static string GetDamageText(CombatWeaponDto? weapon) => weapon?.CalculatedDamage ?? weapon?.BaseDamage ?? "—";

    private enum CombatArea
    {
        Kampf,
        Zonen,
        Eigenschaften
    }

    private enum CombatDrawer
    {
        None,
        Resource,
        Orientation,
        Participant
    }

    private enum CombatParticipantDrawerMode
    {
        Opponent,
        Announcement
    }

}
