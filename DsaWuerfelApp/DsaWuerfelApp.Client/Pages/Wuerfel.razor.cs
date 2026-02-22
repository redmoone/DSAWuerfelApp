using Microsoft.AspNetCore.Components;

using System.Net.Http.Json;

using DsaWuerfelApp.Client.Components;
using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Pages;

public partial class Wuerfel : IDisposable
{
    [Inject] public HttpClient Http { get; set; } = null!;

    private readonly int[] _availableSides = [4, 6, 8, 10, 12, 20];
    private readonly List<int> _selectedDice = new();

    private int _modifier;
    private string? _error;
    private RollSetResult? _last;
    private Dice3D _dice3d = null!;
    private RollHistory _rollHistory = null!;
    private bool _isHiddenRoll;

    protected override void OnInitialized() => GameClient.OnRollResultReceived += HandleServerRoll;

    public void Dispose() => GameClient.OnRollResultReceived -= HandleServerRoll;

    private async void HandleServerRoll(RollResult result)
    {
        _last = new RollSetResult(
            result.Rolls.GroupBy(r => r.Sides).Select(g => new DiceGroup(g.Key, g.Count())).ToArray(),
            result.Modifier,
            result.Rolls.Select(r => new SingleRoll(r.Sides, r.Value)).ToArray(),
            result.TotalSum - result.Modifier,
            result.TotalSum
        );

        await InvokeAsync(async () =>
        {
            if (_dice3d != null)
            {
                await _dice3d.Roll(_last.Rolls.Select(r => r.Value).ToArray());
            }

            StateHasChanged();
        });
    }

    private async Task AddDie(int sides)
    {
        _selectedDice.Add(sides);
        await Update3DView();
    }

    private async Task Reset()
    {
        _selectedDice.Clear();
        _last = null;
        _modifier = 0;
        await Update3DView();
    }

    private void UpdateModifier(int newModifier) => _modifier = newModifier;

    private void UpdateHiddenRollStatus(bool isHidden) => _isHiddenRoll = isHidden;

    private async Task Update3DView() => await _dice3d.UpdateDice(_selectedDice);

    private void HandleDiceRemoved(int index)
    {
        if (index < 0 || index >= _selectedDice.Count) return;
        _selectedDice.RemoveAt(index);
        _ = Update3DView();
        StateHasChanged();
    }

    private async Task Roll()
    {
        if (_selectedDice.Count == 0) return;
        _error = null;

        try
        {
            var diceGroups = _selectedDice
                .GroupBy(sides => sides)
                .Select(g => new DiceGroup(g.Key, g.Count()))
                .ToList();
            if (GameClient.IsConnected && !string.IsNullOrEmpty(GameClient.CurrentSessionId))
            {
                await RollOnlineAsync(diceGroups);
            }
            else
            {
                await RollOfflineAsync(diceGroups);
            }
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private async Task RollOnlineAsync(List<DiceGroup> diceGroups)
    {
        var sharedGroups = diceGroups.Select(g => new DsaWuerfelApp.Shared.DiceGroup(g.Sides, g.Count)).ToList();
        await GameClient.RollDice(sharedGroups, _modifier, GameClient.CurrentSessionId!);
    }

    private async Task RollOfflineAsync(List<DiceGroup> diceGroups)
    {
        var req = new RollSetRequest(diceGroups, _modifier);
        var resp = await Http.PostAsJsonAsync("api/dice/rollset", req);

        if (!resp.IsSuccessStatusCode)
        {
            _error = "Fehler beim Würfeln";
            return;
        }

        _last = await resp.Content.ReadFromJsonAsync<RollSetResult>();

        if (_last != null)
        {
            await _dice3d.Roll(_last.Rolls.Select(r => r.Value).ToArray());

            _rollHistory.AddLocalRoll(new RollResult
            {
                PlayerName = "Du (Offline)",
                TotalSum = _last.Total,
                Modifier = _last.Modifier,
                Timestamp = DateTime.UtcNow,
                Rolls = _last.Rolls
                    .Select(r => new DsaWuerfelApp.Shared.SingleRoll { Sides = r.Sides, Value = r.Value }).ToList()
            });
        }
    }

    private async Task RollAttribute(string attributeName, int value)
    {
        await Reset();
        await AddDie(20);
        await Roll();
    }

    private async Task HandleProbenRoll((string Probe, int Modifier, bool IsHidden) args)
    {
        await Reset();
        _modifier = args.Modifier;
        _isHiddenRoll = args.IsHidden;

        await AddDie(20);
        await AddDie(20);
        await AddDie(20);
        await Roll();
    }

    public record DiceGroup(int Sides, int Count);

    public record SingleRoll(int Sides, int Value);

    public record RollSetRequest(List<DiceGroup> Dice, int Modifier);

    public record RollSetResult(DiceGroup[] Dice, int Modifier, SingleRoll[] Rolls, int Sum, int Total);
}