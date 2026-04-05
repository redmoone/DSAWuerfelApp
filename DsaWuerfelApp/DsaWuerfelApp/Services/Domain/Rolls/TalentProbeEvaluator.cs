using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public static class TalentProbeEvaluator
{
    public static TalentProbeEvaluation Evaluate(
        int talentValue,
        int totalModifier,
        IReadOnlyList<int> attributeValues,
        ReadOnlySpan<int> rolls)
    {
        if (attributeValues.Count != 3 || rolls.Length != 3)
        {
            throw new ArgumentException("Es werden genau 3 Eigenschaften und 3 Würfe benötigt.");
        }

        var ones = 0;
        var twenties = 0;

        foreach (var roll in rolls)
        {
            if (roll == 1)
            {
                ones++;
            }

            if (roll == 20)
            {
                twenties++;
            }
        }

        var effectiveTalentValue = talentValue - totalModifier;
        if (twenties >= 2)
        {
            return new TalentProbeEvaluation(TalentProbeStatus.Patzer, 0, effectiveTalentValue);
        }

        if (ones >= 2)
        {
            return new TalentProbeEvaluation(TalentProbeStatus.GluecklicherWurf, 0, effectiveTalentValue);
        }

        var rest = Math.Min(Math.Max(effectiveTalentValue, 0), talentValue);
        for (var index = 0; index < attributeValues.Count; index++)
        {
            var targetValue = effectiveTalentValue >= 0
                ? attributeValues[index]
                : attributeValues[index] + effectiveTalentValue;

            if (rolls[index] > targetValue)
            {
                rest -= rolls[index] - targetValue;
            }
        }

        return new TalentProbeEvaluation(
            rest >= 0 ? TalentProbeStatus.Bestanden : TalentProbeStatus.NichtBestanden,
            rest,
            effectiveTalentValue);
    }

    public static double CalculateSuccessChance(
        int talentValue,
        int totalModifier,
        IReadOnlyList<int> attributeValues)
    {
        const int sideCount = 20;
        var successfulOutcomes = 0;

        for (var first = 1; first <= sideCount; first++)
        {
            for (var second = 1; second <= sideCount; second++)
            {
                for (var third = 1; third <= sideCount; third++)
                {
                    if (IsSuccess(Evaluate(talentValue, totalModifier, attributeValues, [first, second, third])))
                    {
                        successfulOutcomes++;
                    }
                }
            }
        }

        return successfulOutcomes / Math.Pow(sideCount, 3);
    }

    public static bool IsSuccess(TalentProbeEvaluation evaluation)
    {
        return evaluation.Status is TalentProbeStatus.Bestanden or TalentProbeStatus.GluecklicherWurf;
    }
}

public sealed record TalentProbeEvaluation(
    TalentProbeStatus Status,
    int Rest,
    int EffectiveTalentValue);