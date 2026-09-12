using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class WuerfelActionBar
{
    [Parameter] public string RollText { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> RollTextChanged { get; set; }
    [Parameter] public int Modifier { get; set; }
    [Parameter] public int? DisplayModifier { get; set; }
    [Parameter] public EventCallback<int> ModifierChanged { get; set; }
    [Parameter] public bool IsHiddenRoll { get; set; }
    [Parameter] public EventCallback ToggleHiddenRollRequested { get; set; }
    [Parameter] public EventCallback ResetRequested { get; set; }
    [Parameter] public EventCallback RollRequested { get; set; }
    [Parameter] public bool CanRoll { get; set; }
    [Parameter] public bool IsBusy { get; set; }
    [Parameter] public string RollButtonText { get; set; } = "Würfeln";
    [Parameter] public string? RollButtonAriaLabel { get; set; }
    [Parameter] public bool AllowDirectModifierInput { get; set; }
    [Parameter] public bool ShowRollText { get; set; } = true;
    [Parameter] public bool ShowTitle { get; set; } = true;
    [Parameter] public bool ShowHiddenRollHint { get; set; } = true;
    [Parameter] public string? ResetButtonText { get; set; }

    private int CurrentDisplayModifier => DisplayModifier ?? Modifier;

    private string EffectiveRollButtonAriaLabel => string.IsNullOrWhiteSpace(RollButtonAriaLabel)
        ? RollButtonText
        : RollButtonAriaLabel;
}
