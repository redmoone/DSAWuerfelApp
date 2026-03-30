using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class DiceViewport
{
    private Dice3D _dice3d = null!;
    private long _lastPreviewVersion = -1;
    private long _lastResultVersion = -1;

    [Parameter] public IReadOnlyList<int> SelectedDice { get; set; } = Array.Empty<int>();
    [Parameter] public IReadOnlyList<int> ResultDiceSides { get; set; } = Array.Empty<int>();
    [Parameter] public IReadOnlyList<int> ResultDiceValues { get; set; } = Array.Empty<int>();
    [Parameter] public long PreviewVersion { get; set; }
    [Parameter] public long ResultVersion { get; set; }
    [Parameter] public EventCallback<int> OnDiceRemoved { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _lastPreviewVersion = PreviewVersion;
            _lastResultVersion = ResultVersion;

            if (SelectedDice.Count > 0)
            {
                await _dice3d.UpdateDice(SelectedDice);
            }

            return;
        }

        if (ResultVersion != _lastResultVersion)
        {
            _lastResultVersion = ResultVersion;

            if (ResultDiceSides.Count > 0)
            {
                await _dice3d.UpdateDice(ResultDiceSides);
            }

            if (ResultDiceValues.Count > 0)
            {
                await _dice3d.Roll(ResultDiceValues.ToArray());
            }

            return;
        }

        if (PreviewVersion != _lastPreviewVersion)
        {
            _lastPreviewVersion = PreviewVersion;
            await _dice3d.UpdateDice(SelectedDice);
        }
    }
}