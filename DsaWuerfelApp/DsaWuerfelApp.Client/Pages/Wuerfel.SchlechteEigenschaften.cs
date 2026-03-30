using System.Net.Http.Json;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Pages;

public partial class Wuerfel
{
    private static readonly IReadOnlyDictionary<string, int> EmptyBadTraitValues = new Dictionary<string, int>();

    private AttributeProbeEvaluation? _lastAttributeProbeEvaluation;
    private SchlechteEigenschaftProbeResult? _lastSchlechteEigenschaftProbe;
    private string? _selectedSchlechteEigenschaftName;

    private IReadOnlyDictionary<string, int> CurrentSchlechteEigenschaften =>
        _activeHero?.SchlechteEigenschaften ?? EmptyBadTraitValues;

    private IReadOnlyList<SchlechteEigenschaftSelection> AvailableSchlechteEigenschaften =>
        CurrentSchlechteEigenschaften
            .OrderBy(entry => entry.Key)
            .Select(entry => new SchlechteEigenschaftSelection(entry.Key, entry.Value))
            .ToList();

    private SchlechteEigenschaftSelection? SelectedSchlechteEigenschaft => GetSelectedSchlechteEigenschaft();

    private async void HandleSchlechteEigenschaftProbeResult(SchlechteEigenschaftProbeResult result)
    {
        await ApplySchlechteEigenschaftProbeResultAsync(result, addLocalHistory: false);
    }

    private async Task ExecuteSchlechteEigenschaftProbeAsync()
    {
        _error = null;
        var request = CreateSchlechteEigenschaftProbeRequest();
        if (request is null)
        {
            return;
        }

        _pendingAttributeProbeContext = null;
        _lastAttributeProbeEvaluation = null;
        _lastAttributeProbeRequirement = null;
        _lastProbeEvaluation = null;
        _lastSchlechteEigenschaftProbe = null;
        _selectedDice.Clear();
        _selectedAttributes.Clear();
        ClearSelectedProbe();
        _selectedDice.Add(20);
        await Update3DView();

        try
        {
            if (GameClient.IsConnected && !string.IsNullOrEmpty(GameClient.CurrentSessionId))
            {
                request.SessionId = GameClient.CurrentSessionId!;
                await GameClient.RollSchlechteEigenschaftProbe(request);
            }
            else
            {
                await RollSchlechteEigenschaftProbeOfflineAsync(request);
            }
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private SchlechteEigenschaftProbeRequest? CreateSchlechteEigenschaftProbeRequest()
    {
        if (_activeHero is null)
        {
            _error = "Fuer diese Probe muss ein aktiver Held gewaehlt sein.";
            return null;
        }

        var selectedSchlechteEigenschaft = SelectedSchlechteEigenschaft;
        if (selectedSchlechteEigenschaft is null)
        {
            _error = "Bitte zuerst eine relevante schlechte Eigenschaft waehlen.";
            return null;
        }

        int? forcedRoll = null;
        if (_showDebugForcedRolls)
        {
            forcedRoll = ParseForcedSingleRoll();
            if (!string.IsNullOrWhiteSpace(_forcedRollsText) && forcedRoll is null)
            {
                return null;
            }
        }

        return new SchlechteEigenschaftProbeRequest
        {
            EigenschaftName = selectedSchlechteEigenschaft.Name,
            EigenschaftWert = selectedSchlechteEigenschaft.Wert,
            ForcedRoll = forcedRoll
        };
    }

    private async Task RollSchlechteEigenschaftProbeOfflineAsync(SchlechteEigenschaftProbeRequest request)
    {
        var response = await Http.PostAsJsonAsync("api/dice/schlechteeigenschaftprobe", request);
        if (!response.IsSuccessStatusCode)
        {
            _error = "Fehler bei der Probe auf die schlechte Eigenschaft";
            return;
        }

        var result = await response.Content.ReadFromJsonAsync<SchlechteEigenschaftProbeResult>();
        if (result is null)
        {
            _error = "Die Probe konnte nicht gelesen werden.";
            return;
        }

        result.PlayerName = "Du (Offline)";
        await ApplySchlechteEigenschaftProbeResultAsync(result, addLocalHistory: true);
    }

    private async Task ApplySchlechteEigenschaftProbeResultAsync(
        SchlechteEigenschaftProbeResult result,
        bool addLocalHistory)
    {
        _pendingAttributeProbeContext = null;
        _lastAttributeProbeEvaluation = null;
        _lastAttributeProbeRequirement = null;
        _lastProbeEvaluation = null;
        _lastSchlechteEigenschaftProbe = result;
        _last = ToRollSetResult(result);

        if (_dice3d != null)
        {
            await _dice3d.Roll([result.Roll.Value]);
        }

        if (addLocalHistory)
        {
            _rollHistory.AddLocalRoll(new RollResult
            {
                PlayerName = result.PlayerName,
                Timestamp = result.Timestamp,
                Rolls = [new Shared.SingleRoll { Sides = result.Roll.Sides, Value = result.Roll.Value }],
                Modifier = 0,
                TotalSum = result.Roll.Value
            });
        }

        await InvokeAsync(StateHasChanged);
    }

    private void ToggleSchlechteEigenschaftSelection(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !CurrentSchlechteEigenschaften.ContainsKey(name))
        {
            return;
        }

        _selectedSchlechteEigenschaftName =
            string.Equals(_selectedSchlechteEigenschaftName, name, StringComparison.Ordinal)
                ? null
                : name;
    }

    private SchlechteEigenschaftSelection? GetSelectedSchlechteEigenschaft()
    {
        if (string.IsNullOrWhiteSpace(_selectedSchlechteEigenschaftName) ||
            !CurrentSchlechteEigenschaften.TryGetValue(_selectedSchlechteEigenschaftName, out var value))
        {
            return null;
        }

        return new SchlechteEigenschaftSelection(_selectedSchlechteEigenschaftName, value);
    }

    private void EnsureSelectedSchlechteEigenschaftIsValid()
    {
        if (string.IsNullOrWhiteSpace(_selectedSchlechteEigenschaftName))
        {
            _selectedSchlechteEigenschaftName = null;
            return;
        }

        if (!CurrentSchlechteEigenschaften.ContainsKey(_selectedSchlechteEigenschaftName))
        {
            _selectedSchlechteEigenschaftName = null;
        }
    }

    private static AttributeProbeEvaluation? BuildAttributeProbeEvaluation(
        AttributeProbeContext context,
        RollSetResult result)
    {
        if (result.Rolls.Length != context.Attributes.Length || result.Rolls.Any(roll => roll.Sides != 20))
        {
            return null;
        }

        var effectiveModifier = context.BasisModifier + context.SchlechteEigenschaftAttributModifier;
        var details = new List<AttributeProbeEvaluationDetail>(capacity: context.Attributes.Length);

        for (var i = 0; i < context.Attributes.Length; i++)
        {
            var roll = result.Rolls[i].Value;
            var baseValue = context.AttributeValues[i];
            var targetValue = Math.Clamp(baseValue - effectiveModifier, 0, 20);
            var difference = Math.Max(roll - targetValue, 0);

            details.Add(new AttributeProbeEvaluationDetail(
                context.Attributes[i],
                baseValue,
                targetValue,
                roll,
                difference,
                roll <= targetValue));
        }

        var successCount = details.Count(detail => detail.Success);
        return new AttributeProbeEvaluation(
            string.Join('/', context.Attributes),
            context.BasisModifier,
            context.SchlechteEigenschaftName,
            context.SchlechteEigenschaftAttributModifier,
            effectiveModifier,
            successCount == details.Count,
            successCount,
            details.Count - successCount,
            details);
    }

    private int? ParseForcedSingleRoll()
    {
        if (string.IsNullOrWhiteSpace(_forcedRollsText))
        {
            return null;
        }

        var parts = _forcedRollsText.Split([',', ';', '/', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 1)
        {
            _error = "Fuer die direkte Probe auf eine schlechte Eigenschaft ist genau 1 Testwurf noetig.";
            return null;
        }

        if (!int.TryParse(parts[0], out var roll) || roll is < 1 or > 20)
        {
            _error = "Testwuerfe muessen Zahlen von 1 bis 20 sein.";
            return null;
        }

        return roll;
    }

    private static RollSetResult ToRollSetResult(SchlechteEigenschaftProbeResult result)
    {
        var rolls = new[] { new SingleRoll(result.Roll.Sides, result.Roll.Value) };
        return new RollSetResult([new DiceGroup(result.Roll.Sides, 1)], 0, rolls, result.Roll.Value, result.Roll.Value);
    }

    private static int GetHalvedSchlechteEigenschaftWert(int value)
    {
        return value <= 0 ? 0 : (value + 1) / 2;
    }

    private static string GetAttributeProbeStatusText(AttributeProbeEvaluation result)
    {
        return result.Success
            ? $"Bestanden ({result.SuccessCount}/{result.Details.Count})"
            : $"Misslungen ({result.FailureCount} fehlgeschlagen)";
    }

    private static string GetAttributeProbeEvaluationClass(AttributeProbeEvaluation result)
    {
        return result.Success ? "success" : "failure";
    }

    private static string GetAttributeProbeRollChipClass(AttributeProbeEvaluationDetail detail)
    {
        return detail.Success ? "success" : "failure";
    }

    private static string GetSchlechteEigenschaftProbeStatusText(SchlechteEigenschaftProbeResult result)
    {
        return result.Status == SchlechteEigenschaftProbeStatus.Misslungen
            ? "Misslungen - Eigenschaft setzt sich durch"
            : "Bestanden - Held widersteht";
    }

    private static string GetSchlechteEigenschaftProbeEvaluationClass(SchlechteEigenschaftProbeResult result)
    {
        return result.Success ? "success" : "failure";
    }

    private sealed record SchlechteEigenschaftSelection(string Name, int Wert);

    private sealed record AttributeProbeEvaluation(
        string Probe,
        int BasisModifier,
        string? SchlechteEigenschaftName,
        int SchlechteEigenschaftModifier,
        int EffektiverModifier,
        bool Success,
        int SuccessCount,
        int FailureCount,
        List<AttributeProbeEvaluationDetail> Details);

    private sealed record AttributeProbeEvaluationDetail(
        string Attribute,
        int BaseValue,
        int TargetValue,
        int Roll,
        int Difference,
        bool Success);
}