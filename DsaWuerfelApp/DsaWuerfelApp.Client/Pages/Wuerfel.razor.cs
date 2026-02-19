using Microsoft.AspNetCore.Components;
using System.Net.Http.Json;
using DsaWuerfelApp.Client.Components;
using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Pages;

public partial class Wuerfel
{
    [Inject] 
    public HttpClient Http { get; set; } = null!;

    private readonly int[] _availableSides = [4, 6, 8, 10, 12, 20];
    private readonly Dictionary<int, int> _selected = new();

    private int _modifier = 0;
    private bool _isHiddenRoll = false; // Neu: Status für verdeckte Würfe
    private string? _error;
    private RollSetResult? _last;
    private Dice3D _dice3d = null!;
    private RollHistory _rollHistory = null!;

    private async Task AddDie(int sides)
    {
        _selected.TryAdd(sides, 0);
        _selected[sides]++;
        await Update3DView();
    }

    private async Task Reset()
    {
        _selected.Clear();
        _last = null;
        _modifier = 0;
        await Update3DView();
    }

    private void UpdateModifier(int newModifier)
    {
        _modifier = newModifier;
    }
    
    private void UpdateHiddenRollStatus(bool isHidden)
    {
        _isHiddenRoll = isHidden;
    }

    private async Task Update3DView()
    {
        var flatList = _selected
            .OrderBy(x => x.Key)
            .SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value))
            .ToList();

        await _dice3d.UpdateDice(flatList);
    }

    private void HandleDiceRemoved(int index)
    {
        var flatList = _selected
            .OrderBy(x => x.Key)
            .SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value))
            .ToList();

        if (index >= 0 && index < flatList.Count)
        {
            int sides = flatList[index];
            if (_selected.TryGetValue(sides, out int count) && count > 0)
            {
                _selected[sides]--;
                
                if (_selected[sides] == 0)
                {
                    _selected.Remove(sides);
                }
                
                _ = Update3DView();
                StateHasChanged();
            }
        }
    }

    private async Task Roll()
    {
        if (_selected.Values.Sum() == 0)
        {
            return;
        }
        
        _error = null;

        try
        {
            // Wenn verbunden UND NICHT verdeckt -> an alle senden
            if (GameClient.IsConnected && !string.IsNullOrEmpty(GameClient.CurrentSessionId) && !_isHiddenRoll)
            {
                var diceGroups = _selected.Where(k => k.Value > 0)
                                          .Select(k => new DsaWuerfelApp.Shared.DiceGroup(k.Key, k.Value))
                                          .ToList();
                
                await GameClient.RollDice(diceGroups, _modifier, GameClient.CurrentSessionId);
            }
            else
            {
                // Lokaler Wurf (Offline ODER Verdeckt)
                var req = new RollSetRequest(
                    _selected.Where(k => k.Value > 0).Select(k => new DiceGroup(k.Key, k.Value)).ToList(),
                    _modifier
                );

                var resp = await Http.PostAsJsonAsync("api/dice/rollset", req);

                if (!resp.IsSuccessStatusCode)
                {
                    _error = "Fehler beim Würfeln";
                    return;
                }

                _last = await resp.Content.ReadFromJsonAsync<RollSetResult>();

                if (_last != null)
                {
                    var resultsArray = _last.Rolls.Select(r => r.Value).ToArray();
                    await _dice3d.Roll(resultsArray);
                    
                    // Markierung im lokalen Log, damit der Spieler weiß, dass es verdeckt war
                    string logName = _isHiddenRoll ? "Du (Verdeckter Wurf)" : "Du (Offline)";
                    
                    _rollHistory.AddLocalRoll(new RollResult 
                    { 
                        PlayerName = logName, 
                        TotalSum = _last.Total, 
                        Modifier = _last.Modifier,
                        Timestamp = DateTime.UtcNow,
                        Rolls = _last.Rolls.Select(r => new DsaWuerfelApp.Shared.SingleRoll { Sides = r.Sides, Value = r.Value }).ToList()
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    public record DiceGroup(int Sides, int Count);
    public record SingleRoll(int Sides, int Value);
    public record RollSetRequest(List<DiceGroup> Dice, int Modifier);
    public record RollSetResult(DiceGroup[] Dice, int Modifier, SingleRoll[] Rolls, int Sum, int Total);
}