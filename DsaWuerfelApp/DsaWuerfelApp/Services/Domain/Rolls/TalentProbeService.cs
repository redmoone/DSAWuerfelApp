using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class TalentProbeService(DiceService diceService)
{
    public TalentRollResultDto RollTalentProbe(
        ResolvedTalentRollRequest request,
        string playerName = "Unbekannt")
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TalentName))
        {
            throw new ArgumentException("TalentName is required.", nameof(request));
        }

        DiceService.ValidateModifier(request.BasisModifier);

        if (request.SchlechteEigenschaftModifier is < 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(request.SchlechteEigenschaftModifier));
        }

        if (request.SpecializationModifier is not 0 and not -2)
        {
            throw new ArgumentOutOfRangeException(nameof(request.SpecializationModifier));
        }

        if (request.Probe.Count != 3 || request.AttributeValues.Length != 3)
        {
            throw new ArgumentException("A talent probe requires exactly three attributes.", nameof(request));
        }

        var timestamp = DateTime.UtcNow;
        var rolledDice = CreateRolls(request.ForcedRolls);
        var rollValues = rolledDice.Select(roll => roll.Value).ToArray();
        var totalModifier = request.BasisModifier + request.SpecializationModifier +
                            request.SchlechteEigenschaftModifier;
        var evaluatedProbe =
            TalentProbeEvaluator.Evaluate(request.TalentValue, totalModifier, request.AttributeValues, rollValues);
        var details = BuildRollDetails(
            request.Probe.ToArray(),
            request.AttributeValues,
            rollValues,
            request.TalentValue,
            evaluatedProbe);
        var probeSuccess = TalentProbeEvaluator.IsSuccess(evaluatedProbe);
        var equation = DiceResultFactory.CreateEquation(rolledDice, 0);
        var historyEntry = DiceResultFactory.CreateHistoryEntry(playerName, timestamp, equation);

        return new TalentRollResultDto(
            playerName,
            timestamp,
            request.TalentName,
            request.TalentValue,
            request.Probe.Label,
            totalModifier,
            request.BasisModifier,
            string.IsNullOrWhiteSpace(request.SpecializationName)
                ? null
                : request.SpecializationName.Trim(),
            request.SpecializationModifier,
            string.IsNullOrWhiteSpace(request.SchlechteEigenschaftName)
                ? null
                : request.SchlechteEigenschaftName.Trim(),
            request.SchlechteEigenschaftModifier,
            evaluatedProbe.EffectiveTalentValue,
            rolledDice,
            details,
            evaluatedProbe.Status,
            evaluatedProbe.Rest,
            probeSuccess,
            evaluatedProbe.Status switch
            {
                TalentProbeStatus.Bestanden => evaluatedProbe.Rest,
                TalentProbeStatus.NichtBestanden => Math.Abs(evaluatedProbe.Rest),
                _ => 0
            },
            equation,
            historyEntry);
    }

    private DiceRollDto[] CreateRolls(ForcedRollValues? forcedRolls)
    {
#if !DEBUG
        return diceService.RollDice([new DiceRollGroupDto(20, 3)]);
#else
        if (forcedRolls is null)
        {
            return diceService.RollDice([new DiceRollGroupDto(20, 3)]);
        }

        if (forcedRolls.Count != 3)
        {
            throw new ArgumentException("Testwürfe müssen genau 3 Werte enthalten.");
        }

        return forcedRolls.ToDiceRolls(20);
#endif
    }

    private static TalentRollDetailDto[] BuildRollDetails(
        IReadOnlyList<string> probeAttributes,
        IReadOnlyList<int> attributeValues,
        IReadOnlyList<int> rollValues,
        int talentValue,
        TalentProbeEvaluation evaluatedProbe)
    {
        var details = new List<TalentRollDetailDto>(capacity: 3);
        var remainingRest = Math.Min(Math.Max(evaluatedProbe.EffectiveTalentValue, 0), talentValue);

        for (var index = 0; index < probeAttributes.Count; index++)
        {
            var targetValue = evaluatedProbe.EffectiveTalentValue >= 0
                ? attributeValues[index]
                : attributeValues[index] + evaluatedProbe.EffectiveTalentValue;
            var difference = Math.Max(rollValues[index] - targetValue, 0);
            var success = true;

            if (difference > 0)
            {
                remainingRest -= difference;
                success = evaluatedProbe.EffectiveTalentValue >= 0 && remainingRest >= 0;
            }

            details.Add(new TalentRollDetailDto(
                probeAttributes[index],
                attributeValues[index],
                targetValue,
                rollValues[index],
                difference,
                remainingRest,
                success));
        }

        return details.ToArray();
    }
}

public sealed record ResolvedTalentRollRequest(
    string TalentName,
    int TalentValue,
    ProbeAttributes Probe,
    int[] AttributeValues,
    int BasisModifier,
    string? SpecializationName,
    int SpecializationModifier,
    string? SchlechteEigenschaftName,
    int SchlechteEigenschaftModifier,
    ForcedRollValues? ForcedRolls);