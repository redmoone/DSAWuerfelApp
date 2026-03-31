using DsaWuerfelApp.Client.Services;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Pages;

public partial class Wuerfel : IDisposable
{
    private readonly int[] _availableSides = [4, 6, 8, 10, 12, 20];

    [Inject] public WuerfelState State { get; set; } = null!;
    [Inject] public WuerfelFacade Facade { get; set; } = null!;

    private WuerfelViewState View => State.Current;

    public void Dispose()
    {
        State.Changed -= HandleStateChanged;
        Facade.Detach();
    }

    protected override async Task OnInitializedAsync()
    {
        State.Changed += HandleStateChanged;
        await Facade.AttachAsync();
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

    private Task ToggleProbeInfoAsync()
    {
        return Facade.ToggleProbeInfoAsync();
    }

    private Task HandleSelectedBadTraitChanged(string? selectedBadTraitName)
    {
        Facade.SetSelectedBadTrait(selectedBadTraitName);
        return Task.CompletedTask;
    }

    private Task ExecuteBadTraitRollAsync()
    {
        return Facade.ExecuteBadTraitRollAsync();
    }

    private Task HandleModifierChanged(int modifier)
    {
        Facade.SetModifier(modifier);
        return Task.CompletedTask;
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
}