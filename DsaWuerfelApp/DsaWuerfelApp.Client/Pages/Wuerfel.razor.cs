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

    private static readonly IReadOnlyList<ProbeSearchEntry> DefaultProben =
    [
        BuildSelectableProbeSearchEntry("Klettern (MU/GE/KK)"),
        BuildSelectableProbeSearchEntry("Koerperbeherrschung (GE/GE/KO)"),
        BuildSelectableProbeSearchEntry("Sinnesschaerfe (KL/IN/IN)"),
        BuildSelectableProbeSearchEntry("Ueberreden (MU/IN/CH)"),
        BuildSelectableProbeSearchEntry("Verbergen (MU/IN/GE)")
    ];

    private readonly int[] _availableSides = [4, 6, 8, 10, 12, 20];
    private readonly List<string> _selectedAttributes = [];
    private readonly List<int> _selectedDice = [];

    private Hero? _activeHero;
    private RollArea _activeRollArea;
    private Dice3D _dice3d = null!;
    private string? _error;
    private string _forcedRollsText = string.Empty;
    private bool _isHiddenRoll;
    private RollSetResult? _last;
    private AttributeProbeRequirement? _lastAttributeProbeRequirement;
    private TalentProbeResult? _lastProbeEvaluation;
    private int _modifier;
    private AttributeProbeContext? _pendingAttributeProbeContext;
    private string? _probeInfoText;
    private ProbenSearch? _probenSearch;
    private RollHistory _rollHistory = null!;
    private string? _selectedProbe;
    private bool _showDebugForcedRolls;

    [Inject] public ActiveHeroState ActiveHeroState { get; set; } = null!;
    [Inject] public HttpClient Http { get; set; } = null!;

    private bool CanRoll => _selectedDice.Count > 0 || _selectedAttributes.Count > 0 ||
                            !string.IsNullOrWhiteSpace(_selectedProbe);

    private IReadOnlyDictionary<string, int> CurrentAttributeValues =>
        _activeHero?.Eigenschaften ?? DefaultAttributeValues;

    private IReadOnlyList<ProbeSearchEntry> AvailableProben =>
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
        await TalentCatalog.EnsureLoadedAsync(Http);
        await ActiveHeroState.EnsureLoadedAsync();
        _activeHero = ActiveHeroState.CurrentHero;
    }

    private async void HandleServerRoll(RollResult result)
    {
        _lastProbeEvaluation = null;
        _last = ToRollSetResult(result);
        ApplyAttributeProbeRequirement(_last);

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
        await SwitchRollAreaAsync(RollArea.FreeRoll);
        _selectedDice.Add(sides);
        await Update3DView();
    }

    private async Task Reset()
    {
        await ResetRollAreaAsync();
    }

    private async Task ResetRollAreaAsync(bool clearProbeSearch = true)
    {
        _selectedDice.Clear();
        _selectedAttributes.Clear();
        ClearSelectedProbe();
        _pendingAttributeProbeContext = null;
        _lastAttributeProbeRequirement = null;
        _last = null;
        _lastProbeEvaluation = null;
        _modifier = 0;
        _forcedRollsText = string.Empty;
        _error = null;
        _activeRollArea = RollArea.None;
        await Update3DView();

        if (clearProbeSearch && _probenSearch is not null)
        {
            await _probenSearch.ClearAsync();
        }
    }

    private async Task SwitchRollAreaAsync(RollArea targetArea)
    {
        if (_activeRollArea == targetArea)
        {
            return;
        }

        if (_activeRollArea != RollArea.None)
        {
            await ResetRollAreaAsync(clearProbeSearch: targetArea != RollArea.ProbeSearch);
        }

        _activeRollArea = targetArea;
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

        _pendingAttributeProbeContext = CreateAttributeProbeContext();
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
        ApplyAttributeProbeRequirement(_last);

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

        _pendingAttributeProbeContext = null;
        _lastAttributeProbeRequirement = null;
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

        var selectedTalent = GetSelectedTalent();
        if (selectedTalent is null)
        {
            _error = "Die gewaehlte Probe konnte nicht aufgeloest werden.";
            return null;
        }

        var probeAttributes = ParseProbeAttributes(selectedTalent.Value.Talent.Probe);
        if (probeAttributes.Length != 3)
        {
            _error = $"Fuer {selectedTalent.Value.Name} ist keine vollstaendige Talentprobe hinterlegt.";
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
            TalentName = selectedTalent.Value.Name,
            TalentValue = selectedTalent.Value.Talent.Wert,
            Probe = string.Join('/', probeAttributes),
            AttributeValues = CurrentAttributeValues.ToDictionary(entry => entry.Key, entry => entry.Value),
            ForcedRolls = forcedRolls,
            Modifier = _modifier
        };
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
        _pendingAttributeProbeContext = null;
        _lastAttributeProbeRequirement = null;
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

    private async Task HandleSelectedProbeChanged(string selectedProbe)
    {
        if (!string.IsNullOrWhiteSpace(selectedProbe))
        {
            await SwitchRollAreaAsync(RollArea.ProbeSearch);
            _selectedProbe = selectedProbe;
            _probeInfoText = null;
            return;
        }

        _selectedProbe = string.IsNullOrWhiteSpace(selectedProbe) ? null : selectedProbe;
        _probeInfoText = null;

        if (_activeRollArea == RollArea.ProbeSearch)
        {
            _activeRollArea = RollArea.None;
        }
    }

    private void ClearSelectedProbe()
    {
        _selectedProbe = null;
        _probeInfoText = null;
    }

    private Task ShowSelectedProbeInfo()
    {
        _probeInfoText = string.IsNullOrWhiteSpace(_probeInfoText)
            ? BuildSelectedProbeInfoText()
            : null;
        return Task.CompletedTask;
    }

    private AttributeProbeContext? CreateAttributeProbeContext()
    {
        if (_selectedAttributes.Count != 3 || _selectedDice.Count != 3 || _selectedDice.Any(sides => sides != 20))
        {
            return null;
        }

        var attributes = _selectedAttributes.ToArray();
        var attributeValues = attributes
            .Select(attribute => CurrentAttributeValues.TryGetValue(attribute, out var value) ? value : 0)
            .ToArray();

        return new AttributeProbeContext(attributes, attributeValues, _modifier);
    }

    private void ApplyAttributeProbeRequirement(RollSetResult? result)
    {
        if (result is null || _pendingAttributeProbeContext is null)
        {
            _lastAttributeProbeRequirement = null;
            _pendingAttributeProbeContext = null;
            return;
        }

        _lastAttributeProbeRequirement = BuildAttributeProbeRequirement(_pendingAttributeProbeContext, result);
        _pendingAttributeProbeContext = null;
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
        await SwitchRollAreaAsync(RollArea.Attributes);

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
        await SwitchRollAreaAsync(RollArea.Attributes);

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

    private static AttributeProbeRequirement? BuildAttributeProbeRequirement(
        AttributeProbeContext context,
        RollSetResult result)
    {
        if (result.Rolls.Length != 3 || result.Rolls.Any(roll => roll.Sides != 20))
        {
            return null;
        }

        var details = new List<AttributeProbeRequirementDetail>(capacity: 3);
        var requiredCompensation = 0;

        for (var i = 0; i < 3; i++)
        {
            var roll = result.Rolls[i].Value;
            var baseValue = context.AttributeValues[i];
            var difference = Math.Max(roll - baseValue, 0);
            requiredCompensation += difference;

            details.Add(new AttributeProbeRequirementDetail(
                context.Attributes[i],
                baseValue,
                roll,
                difference));
        }

        return new AttributeProbeRequirement(
            string.Join('/', context.Attributes),
            context.Modifier,
            Math.Max(context.Modifier + requiredCompensation, 0),
            requiredCompensation,
            details);
    }

    private string BuildSelectedProbeInfoText()
    {
        if (string.IsNullOrWhiteSpace(_selectedProbe))
        {
            return "Bitte zuerst eine Probe auswaehlen.";
        }

        var selectedTalent = GetSelectedTalent();
        if (selectedTalent is { } talent)
        {
            var probeAttributes = ParseProbeAttributes(talent.Talent.Probe);
            var attributeInfo = probeAttributes.Length == 0
                ? "keine Eigenschaften hinterlegt"
                : string.Join(", ", probeAttributes.Select(attribute =>
                    $"{attribute} {GetAttributeValueText(attribute)}"));
            var effectiveTalentValue = talent.Talent.Wert - _modifier;
            var modifierInfo = effectiveTalentValue >= 0
                ? $"Nach dem Modifikator {FormatModifier(_modifier)} bleiben {effectiveTalentValue} Ausgleichspunkte fuer Ueberschreitungen."
                : $"Nach dem Modifikator {FormatModifier(_modifier)} liegt der effektive Talentwert bei {effectiveTalentValue}. Dadurch muessen alle drei Eigenschaftswuerfe jeweils um {Math.Abs(effectiveTalentValue)} Punkte niedriger geschafft werden.";

            return
                $"{talent.Name} hat aktuell TaW {talent.Talent.Wert}. Probe: {talent.Talent.Probe}. Verwendete Eigenschaften: {attributeInfo}. {modifierInfo}";
        }

        var probe = ExtractProbeFromLabel(_selectedProbe);
        if (!string.IsNullOrWhiteSpace(probe))
        {
            return
                $"Die ausgewaehlte Probe verwendet {probe}. Fuer heldenspezifische Zusatzinformationen bitte einen aktiven Helden waehlen.";
        }

        return $"Zur ausgewaehlten Probe '{_selectedProbe}' sind aktuell keine weiteren Informationen verfuegbar.";
    }

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

    private (string Name, TalentData Talent)? GetSelectedTalent()
    {
        if (_activeHero is null || string.IsNullOrWhiteSpace(_selectedProbe))
        {
            return null;
        }

        foreach (var talentEntry in BuildKnownTalentMap(_activeHero))
        {
            if (!IsTalentRollable(talentEntry.Key, talentEntry.Value))
            {
                continue;
            }

            if (string.Equals(BuildTalentProbeLabel(talentEntry.Key, talentEntry.Value), _selectedProbe,
                    StringComparison.Ordinal))
            {
                return (talentEntry.Key, talentEntry.Value);
            }
        }

        return null;
    }

    private static IReadOnlyList<ProbeSearchEntry> BuildTalentProben(Hero hero)
    {
        var knownTalents = BuildKnownTalentMap(hero);
        var activeAlternatives = BuildActiveAlternativeLookup(knownTalents);

        return knownTalents
            .OrderBy(entry => entry.Key)
            .Select(entry => IsTalentRollable(entry.Key, entry.Value)
                ? BuildSelectableProbeSearchEntry(BuildTalentProbeLabel(entry.Key, entry.Value))
                : BuildInactiveProbeSearchEntry(entry.Key, entry.Value, activeAlternatives))
            .ToList();
    }

    private static Dictionary<string, TalentData> BuildKnownTalentMap(Hero hero)
    {
        var knownTalents = hero.Talente.ToDictionary(
            entry => entry.Key,
            entry => new TalentData { Wert = entry.Value.Wert, Probe = entry.Value.Probe },
            StringComparer.Ordinal);

        var heroTalentNamesByCanonical = knownTalents.Keys
            .Select(name => (Name: name, Canonical: TalentCatalog.CanonicalizeName(name)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Canonical))
            .GroupBy(entry => entry.Canonical, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);

        foreach (var catalogEntry in TalentCatalog.EntriesByCanonical.Values)
        {
            var canonicalName = TalentCatalog.CanonicalizeName(catalogEntry.Name);
            if (heroTalentNamesByCanonical.TryGetValue(canonicalName, out var existingTalentName))
            {
                var existingTalent = knownTalents[existingTalentName];
                if (string.IsNullOrWhiteSpace(existingTalent.Probe))
                {
                    existingTalent.Probe = catalogEntry.Probe;
                }

                continue;
            }

            knownTalents[catalogEntry.Name] = new TalentData { Wert = 0, Probe = catalogEntry.Probe };
        }

        return knownTalents;
    }

    private static IReadOnlyDictionary<string, ProbeSearchAlternative> BuildActiveAlternativeLookup(
        IReadOnlyDictionary<string, TalentData> knownTalents)
    {
        return knownTalents
            .Where(entry => IsTalentRollable(entry.Key, entry.Value))
            .OrderByDescending(entry => entry.Value.Wert)
            .ThenBy(entry => entry.Key)
            .GroupBy(entry => TalentCatalog.CanonicalizeName(entry.Key), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var selectedTalent = group.First();
                    var label = BuildTalentProbeLabel(selectedTalent.Key, selectedTalent.Value);
                    return new ProbeSearchAlternative(label, label);
                },
                StringComparer.Ordinal);
    }

    private static bool IsTalentRollable(string talentName, TalentData talent)
    {
        if (talent.Wert > 0)
        {
            return true;
        }

        return TalentCatalog.TryGetEntry(talentName, out var catalogEntry) && catalogEntry.IsBasisTalent;
    }

    private string GetAttributeValueText(string attribute)
    {
        return CurrentAttributeValues.TryGetValue(attribute, out var value) ? value.ToString() : "?";
    }

    private static string BuildTalentProbeLabel(string talentName, TalentData talent)
    {
        return string.IsNullOrWhiteSpace(talent.Probe)
            ? $"{talentName} [{talent.Wert}]"
            : $"{talentName} [{talent.Wert}] ({talent.Probe})";
    }

    private static ProbeSearchEntry BuildSelectableProbeSearchEntry(string label)
    {
        return new ProbeSearchEntry(label, label, true, []);
    }

    private static ProbeSearchEntry BuildInactiveProbeSearchEntry(
        string talentName,
        TalentData talent,
        IReadOnlyDictionary<string, ProbeSearchAlternative> activeAlternatives)
    {
        var alternatives = BuildReplacementAlternatives(talentName, activeAlternatives);

        return new ProbeSearchEntry(
            BuildInactiveTalentLabel(talentName, talent),
            null,
            false,
            alternatives);
    }

    private static IReadOnlyList<ProbeSearchAlternative> BuildReplacementAlternatives(
        string talentName,
        IReadOnlyDictionary<string, ProbeSearchAlternative> activeAlternatives)
    {
        if (!TalentCatalog.TryGetEntry(talentName, out var catalogEntry) || catalogEntry.AlternativeNames.Count == 0)
        {
            return Array.Empty<ProbeSearchAlternative>();
        }

        return catalogEntry.AlternativeNames
            .Select(TalentCatalog.CanonicalizeName)
            .Where(canonicalName => !string.IsNullOrWhiteSpace(canonicalName) &&
                                    activeAlternatives.ContainsKey(canonicalName))
            .Distinct(StringComparer.Ordinal)
            .Select(canonicalName => activeAlternatives[canonicalName])
            .ToList();
    }

    private static string BuildInactiveTalentLabel(string talentName, TalentData talent)
    {
        return string.IsNullOrWhiteSpace(talent.Probe)
            ? $"{talentName} nicht aktiviert"
            : $"{talentName} nicht aktiviert ({talent.Probe})";
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

    private static string? ExtractProbeFromLabel(string label)
    {
        var startIndex = label.LastIndexOf('(');
        var endIndex = label.LastIndexOf(')');
        if (startIndex < 0 || endIndex <= startIndex)
        {
            return null;
        }

        return label.Substring(startIndex + 1, endIndex - startIndex - 1);
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

    private static string GetAttributeRequirementChipClass(AttributeProbeRequirementDetail detail)
    {
        return detail.Difference == 0 ? "success" : "failure";
    }

    private void HandleActiveHeroChanged()
    {
        _activeHero = ActiveHeroState.CurrentHero;
        ClearSelectedProbe();
        _pendingAttributeProbeContext = null;
        _lastAttributeProbeRequirement = null;
        _lastProbeEvaluation = null;
        _activeRollArea = RollArea.None;
        _ = _probenSearch?.ClearAsync();
        InvokeAsync(StateHasChanged);
    }

    private enum RollArea
    {
        None,
        Attributes,
        ProbeSearch,
        FreeRoll
    }

    private sealed record AttributeProbeContext(string[] Attributes, int[] AttributeValues, int Modifier);

    private sealed record AttributeProbeRequirement(
        string Probe,
        int Modifier,
        int RequiredTalentValue,
        int RequiredCompensation,
        List<AttributeProbeRequirementDetail> Details);

    private sealed record AttributeProbeRequirementDetail(
        string Attribute,
        int BaseValue,
        int Roll,
        int Difference);

    public record DiceGroup(int Sides, int Count);

    public record SingleRoll(int Sides, int Value);

    public record RollSetRequest(List<DiceGroup> Dice, int Modifier);

    public record RollSetResult(DiceGroup[] Dice, int Modifier, SingleRoll[] Rolls, int Sum, int Total);
}