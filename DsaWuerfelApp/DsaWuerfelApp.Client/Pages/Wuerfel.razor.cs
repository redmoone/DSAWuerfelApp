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
        "Koerperbeherrschung (GE/GE/KO)",
        "Sinnesschaerfe (KL/IN/IN)",
        "Ueberreden (MU/IN/CH)",
        "Verbergen (MU/IN/GE)"
    ];

    private readonly int[] _availableSides = [4, 6, 8, 10, 12, 20];
    private readonly List<string> _selectedAttributes = [];
    private readonly List<int> _selectedDice = [];

    private Hero? _activeHero;
    private Dice3D _dice3d = null!;
    private string? _error;
    private string _forcedRollsText = string.Empty;
    private bool _isHiddenRoll;
    private RollSetResult? _last;
    private TalentProbeResult? _lastProbeEvaluation;
    private int _modifier;
    private RollHistory _rollHistory = null!;
    private string? _selectedProbe;
    private bool _showDebugForcedRolls;

    [Inject] public ActiveHeroState ActiveHeroState { get; set; } = null!;
    [Inject] public HttpClient Http { get; set; } = null!;

    private bool CanRoll => _selectedDice.Count > 0 || _selectedAttributes.Count > 0 ||
                            !string.IsNullOrWhiteSpace(_selectedProbe);

    private IReadOnlyDictionary<string, int> CurrentAttributeValues =>
        _activeHero?.Eigenschaften ?? DefaultAttributeValues;

    private IReadOnlyList<string> AvailableProben =>
        _activeHero is { Talente.Count: > 0 }
            ? BuildTalentProben(_activeHero)
            : DefaultProben;

    private string ProbePlaceholder =>
        _activeHero is null ? "Nach Proben suchen..." : $"Talente von {_activeHero.Name} durchsuchen...";

    private bool ShowDebugForcedRolls => _showDebugForcedRolls;

    public void Dispose()
    {
        GameClient.OnRollResultReceived -= HandleServerRoll;
        GameClient.OnTalentProbeResultReceived -= HandleTalentProbeResult;
        ActiveHeroState.Changed -= HandleActiveHeroChanged;
    }

    protected override void OnInitialized()
    {
        GameClient.OnRollResultReceived += HandleServerRoll;
        GameClient.OnTalentProbeResultReceived += HandleTalentProbeResult;
    }

    protected override async Task OnInitializedAsync()
    {
        ActiveHeroState.Changed += HandleActiveHeroChanged;
        _showDebugForcedRolls = await GetDebugModeAsync();
        await ActiveHeroState.EnsureLoadedAsync();
        _activeHero = ActiveHeroState.CurrentHero;
    }

    private async void HandleServerRoll(RollResult result)
    {
        _lastProbeEvaluation = null;
        _last = ToRollSetResult(result);

        await InvokeAsync(async () =>
        {
            if (_dice3d != null)
            {
                await _dice3d.Roll(_last.Rolls.Select(r => r.Value).ToArray());
            }

            StateHasChanged();
        });
    }

    private async void HandleTalentProbeResult(TalentProbeResult result)
    {
        await ApplyTalentProbeResultAsync(result, addLocalHistory: false);
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
        _lastProbeEvaluation = null;
        _modifier = 0;
        _forcedRollsText = string.Empty;
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
            await ExecuteTalentProbeAsync();
            return;
        }

        _lastProbeEvaluation = null;

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
            _error = "Fehler beim Wuerfeln";
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

    private async Task ExecuteTalentProbeAsync()
    {
        _error = null;
        var request = CreateTalentProbeRequest();
        if (request is null)
        {
            return;
        }

        _lastProbeEvaluation = null;
        _selectedDice.Clear();
        _selectedAttributes.Clear();
        _selectedDice.AddRange([20, 20, 20]);
        await Update3DView();

        try
        {
            if (GameClient.IsConnected && !string.IsNullOrEmpty(GameClient.CurrentSessionId))
            {
                request.SessionId = GameClient.CurrentSessionId!;
                await GameClient.RollTalentProbe(request);
            }
            else
            {
                await RollTalentProbeOfflineAsync(request);
            }
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private TalentProbeRequest? CreateTalentProbeRequest()
    {
        if (_activeHero is null)
        {
            _error = "Fuer eine Talentprobe muss ein aktiver Held gewaehlt sein.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(_selectedProbe))
        {
            _error = "Bitte zuerst ein Talent aus der Probensuche waehlen.";
            return null;
        }

        foreach (var talentEntry in _activeHero.Talente)
        {
            if (!string.Equals(BuildTalentProbeLabel(talentEntry.Key, talentEntry.Value), _selectedProbe,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var probeAttributes = ParseProbeAttributes(talentEntry.Value.Probe);
            if (probeAttributes.Length != 3)
            {
                _error = $"Fuer {talentEntry.Key} ist keine vollstaendige Talentprobe hinterlegt.";
                return null;
            }

            List<int>? forcedRolls = null;
            if (_showDebugForcedRolls)
            {
                forcedRolls = ParseForcedRolls();
                if (!string.IsNullOrWhiteSpace(_forcedRollsText) && forcedRolls is null)
                {
                    return null;
                }
            }

            return new TalentProbeRequest
            {
                TalentName = talentEntry.Key,
                TalentValue = talentEntry.Value.Wert,
                Probe = string.Join('/', probeAttributes),
                AttributeValues = CurrentAttributeValues.ToDictionary(entry => entry.Key, entry => entry.Value),
                ForcedRolls = forcedRolls,
                Modifier = _modifier
            };
        }

        _error = "Die gewaehlte Probe konnte nicht aufgeloest werden.";
        return null;
    }

    private async Task RollTalentProbeOfflineAsync(TalentProbeRequest request)
    {
        var response = await Http.PostAsJsonAsync("api/dice/talentprobe", request);
        if (!response.IsSuccessStatusCode)
        {
            _error = "Fehler bei der Talentprobe";
            return;
        }

        var result = await response.Content.ReadFromJsonAsync<TalentProbeResult>();
        if (result is null)
        {
            _error = "Die Talentprobe konnte nicht gelesen werden.";
            return;
        }

        result.PlayerName = "Du (Offline)";
        await ApplyTalentProbeResultAsync(result, addLocalHistory: true);
    }

    private async Task ApplyTalentProbeResultAsync(TalentProbeResult result, bool addLocalHistory)
    {
        _lastProbeEvaluation = result;
        _last = ToRollSetResult(result);

        if (_dice3d != null)
        {
            await _dice3d.Roll(result.Rolls.Select(roll => roll.Value).ToArray());
        }

        if (addLocalHistory)
        {
            _rollHistory.AddLocalRoll(new RollResult
            {
                PlayerName = result.PlayerName,
                Timestamp = result.Timestamp,
                Rolls = result.Rolls.Select(roll => new Shared.SingleRoll { Sides = roll.Sides, Value = roll.Value })
                    .ToList(),
                Modifier = 0,
                TotalSum = result.Rolls.Sum(roll => roll.Value)
            });
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task<bool> GetDebugModeAsync()
    {
#if DEBUG
        try
        {
            return await Http.GetFromJsonAsync<bool>("api/dice/debug-mode");
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
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

    private List<int>? ParseForcedRolls()
    {
        if (string.IsNullOrWhiteSpace(_forcedRollsText))
        {
            return null;
        }

        var parts = _forcedRollsText.Split([',', ';', '/', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            _error = "Testwuerfe muessen genau 3 Werte enthalten.";
            return null;
        }

        var rolls = new List<int>(capacity: 3);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var roll) || roll is < 1 or > 20)
            {
                _error = "Testwuerfe muessen Zahlen von 1 bis 20 sein.";
                return null;
            }

            rolls.Add(roll);
        }

        return rolls;
    }

    private void SetForcedRolls(string value)
    {
        _forcedRollsText = value;
    }

    private void ClearForcedRolls()
    {
        _forcedRollsText = string.Empty;
    }

    private static IReadOnlyList<string> BuildTalentProben(Hero hero)
    {
        return hero.Talente
            .OrderBy(entry => entry.Key)
            .Select(entry => BuildTalentProbeLabel(entry.Key, entry.Value))
            .ToList();
    }

    private static string BuildTalentProbeLabel(string talentName, TalentData talent)
    {
        return string.IsNullOrWhiteSpace(talent.Probe) ? talentName : $"{talentName} ({talent.Probe})";
    }

    private static string[] ParseProbeAttributes(string? probe)
    {
        if (string.IsNullOrWhiteSpace(probe))
        {
            return [];
        }

        return probe.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.ToUpperInvariant())
            .ToArray();
    }

    private static RollSetResult ToRollSetResult(RollResult result)
    {
        return new RollSetResult(
            result.Rolls.GroupBy(r => r.Sides).Select(g => new DiceGroup(g.Key, g.Count())).ToArray(),
            result.Modifier,
            result.Rolls.Select(r => new SingleRoll(r.Sides, r.Value)).ToArray(),
            result.TotalSum - result.Modifier,
            result.TotalSum);
    }

    private static RollSetResult ToRollSetResult(TalentProbeResult result)
    {
        var rolls = result.Rolls.Select(roll => new SingleRoll(roll.Sides, roll.Value)).ToArray();
        var sum = rolls.Sum(roll => roll.Value);

        return new RollSetResult(
            rolls.GroupBy(roll => roll.Sides).Select(group => new DiceGroup(group.Key, group.Count())).ToArray(),
            0,
            rolls,
            sum,
            sum);
    }

    private static string FormatModifier(int modifier)
    {
        return modifier > 0 ? $"+{modifier}" : modifier.ToString();
    }

    private static string GetProbeStatusText(TalentProbeResult result)
    {
        return result.Status switch
        {
            TalentProbeStatus.Patzer => "Patzer",
            TalentProbeStatus.GluecklicherWurf => "Gluecklicher Wurf",
            TalentProbeStatus.Bestanden => $"Bestanden um {result.Margin}",
            _ => $"Misslungen um {result.Margin}"
        };
    }

    private static string GetProbeEvaluationClass(TalentProbeResult result)
    {
        return result.Status switch
        {
            TalentProbeStatus.Patzer => "patzer",
            TalentProbeStatus.GluecklicherWurf => "glueck",
            TalentProbeStatus.Bestanden => "success",
            _ => "failure"
        };
    }

    private static string GetProbeRollChipClass(TalentProbeResult result, TalentProbeRollDetail detail)
    {
        return result.Status switch
        {
            TalentProbeStatus.Patzer => "patzer",
            TalentProbeStatus.GluecklicherWurf => "glueck",
            _ => detail.Success ? "success" : "failure"
        };
    }

    private void HandleActiveHeroChanged()
    {
        _activeHero = ActiveHeroState.CurrentHero;
        _lastProbeEvaluation = null;
        InvokeAsync(StateHasChanged);
    }

    public record DiceGroup(int Sides, int Count);

    public record SingleRoll(int Sides, int Value);

    public record RollSetRequest(List<DiceGroup> Dice, int Modifier);

    public record RollSetResult(DiceGroup[] Dice, int Modifier, SingleRoll[] Rolls, int Sum, int Total);
}