using Microsoft.AspNetCore.Components;
using System.Net.Http.Json;
using DsaWuerfelApp.Client.Components;

namespace DsaWuerfelApp.Client.Pages;

public partial class Wuerfel
{
    [Inject] public HttpClient Http { get; set; } = null!;

    private readonly int[] _availableSides = [4, 6, 8, 10, 12, 20];
    private readonly Dictionary<int, int> _selected = new();

    private int _modifier = 0;
    private bool _showModifier = false;
    private string? _error;
    private RollSetResult? _last;
    private Dice3D _dice3d = null!;

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

    private async Task Update3DView()
    {
        var flatList = _selected
            .OrderBy(x => x.Key)
            .SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value))
            .ToList();

        await _dice3d.UpdateDice(flatList);
    }

    private static string GetShapeClass(int sides) => "shape-d" + sides;

    private async Task Roll()
    {
        if (_selected.Values.Sum() == 0) return;
        _error = null;

        try
        {
            var req = new RollSetRequest(
                _selected.Where(k => k.Value > 0).Select(k => new DiceGroup(k.Key, k.Value)).ToList(),
                _modifier
            );

            var animTask = _dice3d.Roll();

            var resp = await Http.PostAsJsonAsync("api/dice/rollset", req);

            if (!resp.IsSuccessStatusCode)
            {
                _error = "Fehler beim Würfeln";
                return;
            }

            _last = await resp.Content.ReadFromJsonAsync<RollSetResult>();
            await animTask;
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