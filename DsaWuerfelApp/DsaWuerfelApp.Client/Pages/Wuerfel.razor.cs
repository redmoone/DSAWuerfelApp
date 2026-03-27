using System.Net.Http.Json;

using DsaWuerfelApp.Client.Components;
using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Pages;

public partial class Wuerfel : IDisposable
{
    private static readonly IReadOnlyDictionary<string, int> DefaultAttributeValues = new Dictionary<string, int>
    {
        ["MU"] = 14,
        ["KL"] = 13,
        ["IN"] = 15,
        ["CH"] = 12,
        ["FF"] = 15,
        ["GE"] = 15,
        ["KO"] = 14,
        ["KK"] = 13
    };

    private static readonly IReadOnlyList<string> DefaultProben =
    [
        "Klettern (MU/GE/KK)",
        "Körperbeherrschung (GE/GE/KO)",
        "Sinnesschärfe (KL/IN/IN)",
        "Überreden (MU/IN/CH)",
        "Verbergen (MU/IN/GE)"
    ];

    private readonly int[] _availableSides = [4, 6, 8, 10, 12, 20];
    private readonly List<string> _selectedAttributes = [];
    private readonly List<int> _selectedDice = [];

    private Hero? _activeHero;
    private Dice3D _dice3d = null!;
    private string? _error;
    private bool _isHiddenRoll;
    private RollSetResult? _last;
    private int _modifier;
    private RollHistory _rollHistory = null!;
    private string? _selectedProbe;

    [Inject] public ActiveHeroState ActiveHeroState { get; set; } = null!;
    [Inject] public HttpClient Http { get; set; } = null!;

    private bool CanRoll => _selectedDice.Count > 0 || _selectedAttributes.Count > 0 ||
                            !string.IsNullOrWhiteSpace(_selectedProbe);

    private IReadOnlyDictionary<string, int> CurrentAttributeValues =>
        _activeHero?.Eigenschaften ?? DefaultAttributeValues;

    private IReadOnlyList<string> AvailableProben =>
        _activeHero is { Talente.Count: > 0 }
            ? _activeHero.Talente.Keys.OrderBy(name => name).ToList()
            : DefaultProben;

    private string ProbePlaceholder =>
        _activeHero is null ? "Nach Proben suchen..." : $"Talente von {_activeHero.Name} durchsuchen...";

    public void Dispose()
    {
        GameClient.OnRollResultReceived -= HandleServerRoll;
        ActiveHeroState.Changed -= HandleActiveHeroChanged;
    }

    protected override void OnInitialized()
    {
        GameClient.OnRollResultReceived += HandleServerRoll;
    }

    protected override async Task OnInitializedAsync()
    {
        ActiveHeroState.Changed += HandleActiveHeroChanged;
        await ActiveHeroState.EnsureLoadedAsync();
        _activeHero = ActiveHeroState.CurrentHero;
    }

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
        _selectedAttributes.Clear();
        _selectedProbe = null;
        _last = null;
        _modifier = 0;
        await Update3DView();
    }

    private void ToggleHiddenRoll() => _isHiddenRoll = !_isHiddenRoll;

    private async Task Update3DView() => await _dice3d.UpdateDice(_selectedDice);

    private void HandleDiceRemoved(int index)
    {
        if (index < 0 || index >= _selectedDice.Count) return;
        _selectedDice.RemoveAt(index);

        if (_selectedDice.Count < _selectedAttributes.Count)
        {
            _selectedAttributes.RemoveAt(_selectedAttributes.Count - 1);
        }

        _ = Update3DView();
        StateHasChanged();
    }

    private async Task ExecuteGlobalRoll()
    {
        if (!string.IsNullOrWhiteSpace(_selectedProbe))
        {
            _selectedDice.Clear();
            _selectedAttributes.Clear();
            _selectedDice.AddRange([20, 20, 20]);
            await Update3DView();
            await Roll();
            return;
        }

        if (_selectedDice.Count == 0 && _selectedAttributes.Count > 0)
        {
            for (int i = 0; i < _selectedAttributes.Count; i++)
            {
                _selectedDice.Add(20);
            }

            await Update3DView();
        }

        await Roll();
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
        var sharedGroups = diceGroups.Select(g => new Shared.DiceGroup(g.Sides, g.Count)).ToList();
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
                    .Select(r => new Shared.SingleRoll { Sides = r.Sides, Value = r.Value }).ToList()
            });
        }
    }

    private async Task AddAttribute(string shortName)
    {
        if (GetAttributeCount(shortName) >= 3)
        {
            _selectedAttributes.RemoveAll(a => a == shortName);

            for (int i = 0; i < 3; i++)
            {
                var d20Index = _selectedDice.IndexOf(20);
                if (d20Index != -1)
                {
                    _selectedDice.RemoveAt(d20Index);
                }
            }

            await Update3DView();
            return;
        }

        if (_selectedAttributes.Count < 3)
        {
            if (_selectedAttributes.Count == 3)
            {
                _selectedAttributes.RemoveAt(0);
            }
            else
            {
                _selectedDice.Add(20);
            }

            _selectedAttributes.Add(shortName);
            await Update3DView();
        }
        else if (_selectedAttributes.Count == 3)
        {
            _selectedAttributes.RemoveAt(0);
            _selectedAttributes.Add(shortName);
        }
    }

    private async Task RemoveAttribute(string shortName)
    {
        if (GetAttributeCount(shortName) == 0)
        {
            _selectedAttributes.Clear();
            _selectedDice.RemoveAll(d => d == 20);

            _selectedAttributes.Add(shortName);
            _selectedAttributes.Add(shortName);
            _selectedAttributes.Add(shortName);

            _selectedDice.Add(20);
            _selectedDice.Add(20);
            _selectedDice.Add(20);

            await Update3DView();
            return;
        }

        var index = _selectedAttributes.LastIndexOf(shortName);
        if (index != -1)
        {
            _selectedAttributes.RemoveAt(index);
            var d20Index = _selectedDice.LastIndexOf(20);
            if (d20Index != -1)
            {
                _selectedDice.RemoveAt(d20Index);
            }

            await Update3DView();
        }
    }

    private int GetAttributeCount(string shortName) => _selectedAttributes.Count(a => a == shortName);

    private void HandleActiveHeroChanged()
    {
        _activeHero = ActiveHeroState.CurrentHero;
        InvokeAsync(StateHasChanged);
    }

    public record DiceGroup(int Sides, int Count);

    public record SingleRoll(int Sides, int Value);

    public record RollSetRequest(List<DiceGroup> Dice, int Modifier);

    public record RollSetResult(DiceGroup[] Dice, int Modifier, SingleRoll[] Rolls, int Sum, int Total);
}