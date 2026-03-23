using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class DiceControls
{
    [Parameter] public int[] AvailableSides { get; set; } = [];
    [Parameter] public EventCallback<int> OnAddDie { get; set; }

    private static string GetShapeClass(int sides) => "shape-d" + sides;
}