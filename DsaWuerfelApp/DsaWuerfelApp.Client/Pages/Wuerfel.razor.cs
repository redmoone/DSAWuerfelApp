using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Pages;

public partial class Wuerfel : IDisposable
{
    private readonly int[] _availableSides = [4, 6, 8, 10, 12, 20];
    private readonly HashSet<string> _selectedMasterTargetUserIds = new(StringComparer.Ordinal);

    [Inject] public ActiveHeroState ActiveHeroState { get; set; } = null!;
    [Inject] public CombatCoordinator CombatCoordinator { get; set; } = null!;
    [Inject] public CombatState CombatState { get; set; } = null!;
    [Inject] public CombatSessionState CombatSessionState { get; set; } = null!;
    [Inject] public WuerfelState State { get; set; } = null!;
    [Inject] public WuerfelFacade Facade { get; set; } = null!;
    [Inject] public SessionState SessionState { get; set; } = null!;
    [Inject] public AuthState AuthState { get; set; } = null!;

    private WuerfelViewState View => State.Current;

    private WuerfelArea VisibleArea => View.ActiveArea == WuerfelArea.None
        ? WuerfelArea.ProbeSearch
        : View.ActiveArea;

    private WuerfelCombatDrawer _combatDrawer;
    private CombatResourceKind? _combatResourceKind;
    private int? _combatResourceDraft;
    private int? _combatWoundDraft;
    private int? _combatInitiativeDraft;
    private bool _combatInitiativeBusy;
    private string? _combatNotice;

    private Hero? CombatHero => ActiveHeroState.CurrentHero;
    private CombatProfileDto? CombatProfile => CombatState.Profile;
    private bool CombatProfileLoading => CombatState.IsProfileLoading;
    private string? CombatProfileError => CombatState.ProfileError;
    private CombatSetVariantDto? CombatSelectedSet => CombatState.SelectedSet;
    private int? CombatCurrentLeP => CombatState.CurrentLeP;
    private int? CombatCurrentAuP => CombatState.CurrentAuP;
    private int? CombatCurrentAeP => CombatState.CurrentAeP;
    private int? CombatCurrentKeP => CombatState.CurrentKeP;
    private IReadOnlyDictionary<CombatWoundZone, int?> CombatWounds => CombatState.Wounds;
    private IReadOnlyList<string> CombatEffects => CombatState.Effects;
    private CombatFacing CombatFacing => CombatState.Facing;
    private CombatWoundZone? CombatSelectedZone => CombatState.SelectedZone;
    private CombatSessionSnapshotDto? CombatSession => CombatSessionState.Current;
    private CombatSessionParticipantDto? OwnCombatParticipant => CombatSession?.Participants
        .FirstOrDefault(participant => participant.HeroId == CombatHero?.Id);
    private int? CombatCurrentInitiative => OwnCombatParticipant?.CurrentInitiative ?? CombatState.CurrentInitiative;
    private bool CombatIsSession => !string.IsNullOrWhiteSpace(SessionState.ActiveSessionId);
    private bool CombatIsStarted => CombatIsSession ? CombatSession?.IsStarted == true : CombatState.IsStarted;
    private bool CombatCanUndo => CombatIsSession ? CombatSessionState.CanUndo : CombatState.CanUndo;
    private string CombatHeroDisplayName => CombatHero?.Name ?? "Kein aktiver Held";
    private CombatWoundZone CombatWoundDrawerZone => CombatSelectedZone ?? CombatWoundZone.Torso;
    private string CombatDrawerTitle => _combatDrawer switch
    {
        WuerfelCombatDrawer.Resource => $"{GetCombatResourceLabel(_combatResourceKind ?? CombatResourceKind.LeP)} setzen",
        WuerfelCombatDrawer.Wound => "Wundstand setzen",
        WuerfelCombatDrawer.Initiative => "Initiative",
        _ => "Details"
    };

    private SessionPlayerDto? CurrentSessionPlayer
    {
        get
        {
            var currentUserId = NormalizeUserId(AuthState.Current.User?.Id);
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                return null;
            }

            return SessionState.ActiveSession?.Players.FirstOrDefault(player =>
                string.Equals(NormalizeUserId(player.UserId), currentUserId, StringComparison.Ordinal));
        }
    }

    private bool IsSessionMaster
    {
        get
        {
            var currentUserId = NormalizeUserId(AuthState.Current.User?.Id);
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                return false;
            }

            if (CurrentSessionPlayer is { IsMaster: true })
            {
                return true;
            }

            return string.Equals(
                NormalizeUserId(SessionState.ActiveSession?.MasterUserId),
                currentUserId,
                StringComparison.Ordinal);
        }
    }

    private IReadOnlyList<SessionPlayerDto> AvailableMasterTargets =>
        SessionState.ActiveSession?.Players
            .Where(player => !player.IsMaster && player.ActiveHeroId.HasValue)
            .OrderBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<SessionPlayerDto>();

    private bool IsAllMasterTargetsSelected =>
        AvailableMasterTargets.Count > 0 &&
        AvailableMasterTargets.All(target => _selectedMasterTargetUserIds.Contains(target.UserId));

    private string MasterTargetSummary => _selectedMasterTargetUserIds.Count switch
    {
        0 => "Einzelwurf",
        1 => "1 Ziel",
        var count => $"{count} Ziele"
    };

    public void Dispose()
    {
        ActiveHeroState.Changed -= HandleCombatContextChanged;
        CombatState.Changed -= HandleCombatContextChanged;
        CombatSessionState.Changed -= HandleCombatContextChanged;
        State.Changed -= HandleStateChanged;
        AuthState.Changed -= HandleAuthChanged;
        SessionState.ActiveSessionChanged -= HandleActiveSessionChanged;
        Facade.Detach();
    }

    protected override async Task OnInitializedAsync()
    {
        ActiveHeroState.Changed += HandleCombatContextChanged;
        CombatState.Changed += HandleCombatContextChanged;
        CombatSessionState.Changed += HandleCombatContextChanged;
        State.Changed += HandleStateChanged;
        AuthState.Changed += HandleAuthChanged;
        SessionState.ActiveSessionChanged += HandleActiveSessionChanged;
        CombatCoordinator.Attach();
        await CombatState.EnsureLoadedAsync();
        await CombatSessionState.EnsureLoadedAsync();
        await Facade.AttachAsync();
        await ApplyMasterTargetSelectionAsync();
    }

    private Task AddDieAsync(int sides)
    {
        return Facade.AddDieAsync(sides);
    }

    private Task ActivateAreaAsync(WuerfelArea area)
    {
        return Facade.ActivateAreaAsync(area);
    }

    private Task HandleDiceRemovedAsync(int index)
    {
        return Facade.RemoveDieAsync(index);
    }

    private Task AddAttributeAsync(string shortName)
    {
        return Facade.AddAttributeAsync(shortName);
    }

    private Task RemoveAttributeAsync(string shortName)
    {
        return Facade.RemoveAttributeAsync(shortName);
    }

    private Task RemoveAttributeAtAsync(int index)
    {
        return Facade.RemoveAttributeAtAsync(index);
    }

    private Task HandleSelectedProbeChangedAsync(string selectedProbeValue)
    {
        return Facade.SetSelectedProbeAsync(selectedProbeValue);
    }

    private Task HandleSpellOptionToggleAsync(string spellOptionValue)
    {
        return Facade.ToggleSpellOptionAsync(spellOptionValue);
    }

    private Task ToggleProbeInfoAsync()
    {
        return Facade.ToggleProbeInfoDetailsAsync();
    }

    private Task HandleHistoryEntrySelected(RollHistoryEntryDto entry)
    {
        State.OpenHistoryEntry(entry);
        return Task.CompletedTask;
    }

    private Task CloseDetailsAsync()
    {
        State.CloseDetails();
        return Task.CompletedTask;
    }

    private Task HandleSelectedBadTraitChanged(string? selectedBadTraitName)
    {
        return Facade.SetSelectedBadTraitAsync(selectedBadTraitName);
    }

    private Task ExecuteBadTraitRollAsync()
    {
        return Facade.ExecuteBadTraitRollAsync();
    }

    private Task HandleModifierChanged(int modifier)
    {
        return Facade.SetModifierAsync(modifier);
    }

    private Task HandleRollTextChanged(string rollText)
    {
        Facade.SetRollText(rollText);
        return Task.CompletedTask;
    }

    private Task ToggleHiddenRoll()
    {
        Facade.ToggleHiddenRoll();
        return Task.CompletedTask;
    }

    private Task ResetAsync()
    {
        return Facade.ResetAsync();
    }

    private Task ExecuteRollAsync()
    {
        return Facade.ExecuteCurrentRollAsync();
    }

    private bool IsMasterTargetSelected(string userId)
    {
        return _selectedMasterTargetUserIds.Contains(userId);
    }

    private Task ToggleMasterTargetAsync(string userId)
    {
        if (!_selectedMasterTargetUserIds.Add(userId))
        {
            _selectedMasterTargetUserIds.Remove(userId);
        }

        return ApplyMasterTargetSelectionAsync();
    }

    private Task SelectAllMasterTargetsAsync()
    {
        _selectedMasterTargetUserIds.Clear();
        foreach (var target in AvailableMasterTargets)
        {
            _selectedMasterTargetUserIds.Add(target.UserId);
        }

        return ApplyMasterTargetSelectionAsync();
    }

    private Task ClearMasterTargetsAsync()
    {
        _selectedMasterTargetUserIds.Clear();
        return ApplyMasterTargetSelectionAsync();
    }

    private Task HandleForcedRollsChanged(string forcedRollsText)
    {
        Facade.SetForcedRollsText(forcedRollsText);
        return Task.CompletedTask;
    }

    private Task HandleForcedRollPreset(string forcedRollsText)
    {
        Facade.SetForcedRollPreset(forcedRollsText);
        return Task.CompletedTask;
    }

    private Task ClearForcedRolls()
    {
        Facade.ClearForcedRolls();
        return Task.CompletedTask;
    }

    private void HandleCombatContextChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private void OpenCombatResourceDrawer(CombatResourceKind resource)
    {
        _combatResourceKind = resource;
        _combatResourceDraft = GetCombatResourceValue(resource);
        _combatDrawer = WuerfelCombatDrawer.Resource;
    }

    private void OpenCombatWoundDrawer()
    {
        var zone = CombatWoundDrawerZone;
        _combatWoundDraft = CombatWounds.TryGetValue(zone, out var wounds) ? wounds ?? 0 : 0;
        _combatDrawer = WuerfelCombatDrawer.Wound;
    }

    private void OpenCombatInitiativeDrawer()
    {
        var runtimeModifier = CombatProfile is null || CombatSelectedSet is null
            ? 0
            : CombatRuntimeModifierRules.ResolveInitiative(
                CombatProfile,
                CombatSelectedSet,
                BuildCombatRuntimeState()).Modifier;
        _combatInitiativeDraft = CombatCurrentInitiative ?? (CombatSelectedSet?.Initiative + runtimeModifier);
        _combatDrawer = WuerfelCombatDrawer.Initiative;
    }

    private void CloseCombatDrawer()
    {
        _combatDrawer = WuerfelCombatDrawer.None;
        _combatResourceKind = null;
    }

    private async Task ApplyCombatResourceAsync()
    {
        if (_combatResourceKind is not { } resource || CombatProfile is null)
        {
            return;
        }

        await CombatState.SetResourceAsync(resource, _combatResourceDraft);
        var sessionResult = await SyncCombatSessionRuntimeStateAsync();
        _combatNotice = sessionResult?.Stale == true
            ? sessionResult.Message
            : $"{GetCombatResourceLabel(resource)} gespeichert.";
        CloseCombatDrawer();
    }

    private async Task ApplyCombatWoundAsync()
    {
        if (CombatProfile is null || !_combatWoundDraft.HasValue)
        {
            _combatNotice = "Bitte einen Wundstand von 0 bis 3 setzen.";
            return;
        }

        await CombatState.SetWoundAsync(CombatWoundDrawerZone, _combatWoundDraft.Value);
        var sessionResult = await SyncCombatSessionRuntimeStateAsync();
        _combatNotice = sessionResult?.Stale == true
            ? sessionResult.Message
            : $"{GetCombatWoundLabel(CombatWoundDrawerZone)} gespeichert.";
        CloseCombatDrawer();
    }

    private async Task ApplyCombatInitiativeAsync()
    {
        if (!_combatInitiativeDraft.HasValue)
        {
            _combatNotice = "Bitte einen konkreten INI-Wert setzen.";
            return;
        }

        if (CombatIsSession)
        {
            var participant = OwnCombatParticipant;
            var result = await CombatSessionState.SetInitiativeAsync(
                _combatInitiativeDraft.Value,
                participant?.Id,
                CombatHero?.Id,
                BuildCombatRuntimeState());
            _combatNotice = result.Message;
            if (result.Applied)
            {
                if (participant?.HeroId == CombatHero?.Id)
                {
                    await CombatState.SetInitiativeAsync(_combatInitiativeDraft);
                }

                CloseCombatDrawer();
            }

            return;
        }

        if (await CombatState.SetInitiativeAsync(_combatInitiativeDraft))
        {
            _combatNotice = $"INI {_combatInitiativeDraft} gespeichert.";
            CloseCombatDrawer();
        }
    }

    private async Task RollCombatInitiativeAsync()
    {
        if (CombatProfile is null || CombatSelectedSet is null)
        {
            _combatNotice = "Für den INI-Hilfswurf fehlt ein importiertes Kampfprofil.";
            return;
        }

        _combatInitiativeBusy = true;
        try
        {
            if (CombatIsSession)
            {
                var participant = OwnCombatParticipant;
                var result = await CombatSessionState.RollInitiativeAsync(
                    participant?.Id,
                    CombatHero?.Id,
                    BuildCombatRuntimeState());
                var updatedParticipant = result.Snapshot.Participants.FirstOrDefault(current =>
                    participant is not null
                        ? current.Id == participant.Id
                        : current.HeroId == CombatHero?.Id);
                _combatInitiativeDraft = updatedParticipant?.CurrentInitiative;
                _combatNotice = result.Message;
                if (result.Applied && _combatInitiativeDraft.HasValue && updatedParticipant?.HeroId == CombatHero?.Id)
                {
                    await CombatState.SetInitiativeAsync(_combatInitiativeDraft);
                    CloseCombatDrawer();
                }

                return;
            }

            var rollResult = await CombatCoordinator.RollAsync(new CombatRollRequestDto
            {
                RequestId = Guid.NewGuid(),
                SessionId = SessionState.ActiveSessionId,
                HeroId = CombatHero?.Id,
                SetId = CombatSelectedSet.Id,
                Action = CombatActionKind.InitiativeHelper,
                RuntimeState = BuildCombatRuntimeState(),
                Helper = new CombatHelperRollRequestDto
                {
                    DiceCount = 1,
                    DiceSides = 6,
                    Purpose = "INI-Startwurf"
                }
            });
            var baseInitiative = CombatSelectedSet.Initiative ?? 0;
            var runtime = CombatRuntimeModifierRules.ResolveInitiative(
                CombatProfile,
                CombatSelectedSet,
                BuildCombatRuntimeState());
            _combatInitiativeDraft = baseInitiative + rollResult.Rolls.Sum(roll => roll.Value) + runtime.Modifier;
            _combatNotice = runtime.Modifier == 0
                ? $"INI-Hilfswurf: {_combatInitiativeDraft} zum Anwenden bereit."
                : $"INI-Hilfswurf: {_combatInitiativeDraft} zum Anwenden bereit ({runtime.Modifier} laufend).";
        }
        catch (Exception exception)
        {
            _combatNotice = exception.Message;
        }
        finally
        {
            _combatInitiativeBusy = false;
        }
    }

    private Task HandleCombatResourceDraftChanged(int? value)
    {
        _combatResourceDraft = value;
        return Task.CompletedTask;
    }

    private Task HandleCombatWoundDraftChanged(int? value)
    {
        _combatWoundDraft = value.HasValue ? Math.Clamp(value.Value, 0, 3) : null;
        return Task.CompletedTask;
    }

    private Task HandleCombatInitiativeDraftChanged(int? value)
    {
        _combatInitiativeDraft = value;
        return Task.CompletedTask;
    }

    private Task HandleCombatFacingChanged(CombatFacing facing) => CombatState.SetFacingAsync(facing);

    private async Task HandleCombatZoneSelected(CombatWoundZone zone)
    {
        await CombatState.SetSelectedZoneAsync(zone);
        _combatWoundDraft = CombatWounds.TryGetValue(zone, out var wounds) ? wounds ?? 0 : 0;
        _combatDrawer = WuerfelCombatDrawer.Wound;
    }

    private async Task UndoCombatChangeAsync()
    {
        if (CombatIsSession)
        {
            var result = await CombatSessionState.UndoAsync();
            _combatNotice = result.Message;
            return;
        }

        _combatNotice = await CombatState.UndoLastChangeAsync()
            ? "Letzte Änderung wurde rückgängig gemacht."
            : "Keine Änderung zum Rückgängigmachen vorhanden.";
    }

    private CombatRuntimeStateDto BuildCombatRuntimeState() => new()
    {
        IsStarted = CombatIsStarted,
        CurrentLeP = CombatCurrentLeP,
        CurrentAuP = CombatCurrentAuP,
        Wounds = CombatWounds.ToDictionary(pair => pair.Key, pair => pair.Value)
    };

    private async Task<CombatSessionMutationResultDto?> SyncCombatSessionRuntimeStateAsync()
    {
        if (!CombatIsSession || OwnCombatParticipant is not { } participant || CombatHero is null)
        {
            return null;
        }

        return await CombatSessionState.SyncRuntimeStateAsync(
            BuildCombatRuntimeState(),
            participant.Id,
            CombatHero.Id);
    }

    private int? GetCombatResourceValue(CombatResourceKind resource) => resource switch
    {
        CombatResourceKind.LeP => CombatCurrentLeP,
        CombatResourceKind.AuP => CombatCurrentAuP,
        CombatResourceKind.AeP => CombatCurrentAeP,
        CombatResourceKind.KeP => CombatCurrentKeP,
        _ => null
    };

    private int? GetCombatResourceMaximum(CombatResourceKind resource) => resource switch
    {
        CombatResourceKind.LeP => CombatProfile?.Resources.LeP,
        CombatResourceKind.AuP => CombatProfile?.Resources.AuP,
        CombatResourceKind.AeP => CombatProfile?.Resources.AeP,
        CombatResourceKind.KeP => CombatProfile?.Resources.KeP,
        _ => null
    };

    private static int? GetCombatResourceMinimum(CombatResourceKind resource) =>
        resource == CombatResourceKind.LeP ? null : 0;

    private static string GetCombatResourceLabel(CombatResourceKind resource) => resource switch
    {
        CombatResourceKind.LeP => "LeP",
        CombatResourceKind.AuP => "AuP",
        CombatResourceKind.AeP => "AeP",
        CombatResourceKind.KeP => "KE",
        _ => "Wert"
    };

    private static string GetCombatResourceDescription(CombatResourceKind resource) => resource switch
    {
        CombatResourceKind.LeP => "Lebenspunkte bleiben auch unter 0 sichtbar und werden nicht automatisch geheilt.",
        CombatResourceKind.AuP => "Ausdauerpunkte dürfen nicht negativ gesetzt werden.",
        CombatResourceKind.AeP => "Astralenergie wird nur angezeigt, wenn ein positives Maximum importiert wurde.",
        CombatResourceKind.KeP => "Karmaenergie wird nur angezeigt, wenn ein positives Maximum importiert wurde.",
        _ => string.Empty
    };

    private static string GetCombatWoundLabel(CombatWoundZone zone) => zone switch
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

    private string GetCombatArmorFacingLabel()
    {
        var armorZone = CombatWoundDrawerZone == CombatWoundZone.Torso
            ? CombatFacing == CombatFacing.Back ? CombatArmorZone.Back : CombatArmorZone.Chest
            : CombatWoundDrawerZone switch
            {
                CombatWoundZone.Head => CombatArmorZone.Head,
                CombatWoundZone.Abdomen => CombatArmorZone.Abdomen,
                CombatWoundZone.LeftArm => CombatArmorZone.LeftArm,
                CombatWoundZone.RightArm => CombatArmorZone.RightArm,
                CombatWoundZone.LeftLeg => CombatArmorZone.LeftLeg,
                CombatWoundZone.RightLeg => CombatArmorZone.RightLeg,
                _ => CombatArmorZone.Chest
            };
        return $"{(CombatFacing == CombatFacing.Back ? "Rückseite" : "Vorderseite")} · RS {GetCombatArmorValue(armorZone)?.ToString() ?? "—"}";
    }

    private int? GetCombatArmorValue(CombatArmorZone zone)
    {
        var armor = CombatSelectedSet?.ArmorZones;
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

    private void HandleStateChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private void HandleAuthChanged()
    {
        _ = InvokeAsync(async () =>
        {
            await ApplyMasterTargetSelectionAsync();
            StateHasChanged();
        });
    }

    private void HandleActiveSessionChanged()
    {
        _ = InvokeAsync(async () =>
        {
            await ApplyMasterTargetSelectionAsync();
            StateHasChanged();
        });
    }

    private Task ApplyMasterTargetSelectionAsync()
    {
        if (!IsSessionMaster)
        {
            _selectedMasterTargetUserIds.Clear();
            return Facade.SetMasterTargetsAsync(Array.Empty<SessionPlayerDto>());
        }

        _selectedMasterTargetUserIds.IntersectWith(AvailableMasterTargets.Select(target => target.UserId));

        var selectedTargets = AvailableMasterTargets
            .Where(target => _selectedMasterTargetUserIds.Contains(target.UserId))
            .ToArray();

        return Facade.SetMasterTargetsAsync(selectedTargets, true);
    }

    private static string? NormalizeUserId(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var trimmedUserId = userId.Trim();
        return Guid.TryParse(trimmedUserId, out var parsedGuid)
            ? parsedGuid.ToString("N")
            : trimmedUserId;
    }

    private enum WuerfelCombatDrawer
    {
        None,
        Resource,
        Wound,
        Initiative
    }
}
