using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Pages;

public partial class Wuerfel : IDisposable
{
    private readonly int[] _availableSides = [4, 6, 8, 10, 12, 20];
    private readonly HashSet<string> _selectedMasterTargetUserIds = new(StringComparer.Ordinal);

    [Inject] public WuerfelState State { get; set; } = null!;
    [Inject] public WuerfelFacade Facade { get; set; } = null!;
    [Inject] public SessionState SessionState { get; set; } = null!;
    [Inject] public AuthState AuthState { get; set; } = null!;

    private WuerfelViewState View => State.Current;

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

    public void Dispose()
    {
        State.Changed -= HandleStateChanged;
        AuthState.Changed -= HandleAuthChanged;
        SessionState.ActiveSessionChanged -= HandleActiveSessionChanged;
        Facade.Detach();
    }

    protected override async Task OnInitializedAsync()
    {
        State.Changed += HandleStateChanged;
        AuthState.Changed += HandleAuthChanged;
        SessionState.ActiveSessionChanged += HandleActiveSessionChanged;
        await Facade.AttachAsync();
        await ApplyMasterTargetSelectionAsync();
    }

    private Task AddDieAsync(int sides)
    {
        return Facade.AddDieAsync(sides);
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

        return Facade.SetMasterTargetsAsync(selectedTargets);
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
}
