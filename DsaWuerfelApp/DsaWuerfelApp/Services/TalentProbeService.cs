using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public class TalentProbeService(DiceService diceService)
{
    public TalentProbeResult RollTalentProbe(TalentProbeRequest request, string playerName = "Unbekannt")
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TalentName))
            throw new ArgumentException("TalentName is required.", nameof(request));

        if (request.Modifier is < -999 or > 999)
            throw new ArgumentOutOfRangeException(nameof(request.Modifier));

        if (request.SchlechteEigenschaftWert is < 0 or > 20)
            throw new ArgumentOutOfRangeException(nameof(request.SchlechteEigenschaftWert));

        var probeAttributes = ParseProbeAttributes(request.Probe);
        if (probeAttributes.Length != 3)
            throw new ArgumentException("A talent probe requires exactly three attributes.", nameof(request));

        foreach (var attribute in probeAttributes)
        {
            if (!request.AttributeValues.ContainsKey(attribute))
                throw new ArgumentException($"Missing attribute value for '{attribute}'.", nameof(request));
        }

        var rollResult = CreateRollResult(request.ForcedRolls, playerName);
        var attributeValues = probeAttributes
            .Select(attribute => request.AttributeValues[attribute])
            .ToArray();
        var rollValues = rollResult.Rolls
            .Select(roll => roll.Value)
            .ToArray();
        var totalModifier = request.Modifier + request.SchlechteEigenschaftWert;
        var evaluatedProbe = TalentProbe.Check(request.TalentValue, totalModifier, attributeValues, rollValues);
        var details = BuildRollDetails(probeAttributes, attributeValues, rollValues, evaluatedProbe);
        var probeSuccess = evaluatedProbe.Status is TalentProbeStatus.Bestanden or TalentProbeStatus.GluecklicherWurf;

        return new TalentProbeResult
        {
            PlayerName = playerName,
            Timestamp = rollResult.Timestamp,
            TalentName = request.TalentName,
            TalentValue = request.TalentValue,
            Probe = string.Join('/', probeAttributes),
            Modifier = totalModifier,
            BasisModifier = request.Modifier,
            SchlechteEigenschaftName = string.IsNullOrWhiteSpace(request.SchlechteEigenschaftName)
                ? null
                : request.SchlechteEigenschaftName.Trim(),
            SchlechteEigenschaftModifier = request.SchlechteEigenschaftWert,
            EffectiveTalentValue = evaluatedProbe.EffektiverWert,
            Rolls = rollResult.Rolls,
            Details = details,
            Status = evaluatedProbe.Status,
            Rest = evaluatedProbe.Rest,
            Success = probeSuccess,
            Margin = evaluatedProbe.Status switch
            {
                TalentProbeStatus.Bestanden => evaluatedProbe.Rest,
                TalentProbeStatus.NichtBestanden => Math.Abs(evaluatedProbe.Rest),
                _ => 0
            }
        };
    }

    public static RollResult ToRollResult(TalentProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new RollResult
        {
            PlayerName = result.PlayerName,
            Timestamp = result.Timestamp,
            Rolls = result.Rolls,
            Modifier = 0,
            TotalSum = result.Rolls.Sum(roll => roll.Value)
        };
    }

    private RollResult CreateRollResult(IReadOnlyList<int>? forcedRolls, string playerName)
    {
#if !DEBUG
        return diceService.RollSet([new DiceGroup(20, 3)], 0, playerName);
#else
        if (forcedRolls is null)
        {
            return diceService.RollSet([new DiceGroup(20, 3)], 0, playerName);
        }

        if (forcedRolls.Count != 3)
            throw new ArgumentException("ForcedRolls muessen genau 3 Werte enthalten.");

        if (forcedRolls.Any(roll => roll is < 1 or > 20))
            throw new ArgumentException("ForcedRolls duerfen nur Werte von 1 bis 20 enthalten.");

        var rolls = forcedRolls
            .Select(roll => new SingleRoll { Sides = 20, Value = roll })
            .ToList();

        return new RollResult
        {
            PlayerName = playerName,
            Timestamp = DateTime.UtcNow,
            Rolls = rolls,
            Modifier = 0,
            TotalSum = rolls.Sum(roll => roll.Value)
        };
#endif
    }

    private static List<TalentProbeRollDetail> BuildRollDetails(
        IReadOnlyList<string> probeAttributes,
        IReadOnlyList<int> attributeValues,
        IReadOnlyList<int> rollValues,
        TalentProbe.Result evaluatedProbe)
    {
        var details = new List<TalentProbeRollDetail>(capacity: 3);
        var remainingRest = Math.Max(evaluatedProbe.EffektiverWert, 0);

        for (var i = 0; i < probeAttributes.Count; i++)
        {
            var targetValue = evaluatedProbe.EffektiverWert >= 0
                ? attributeValues[i]
                : attributeValues[i] + evaluatedProbe.EffektiverWert;
            var difference = Math.Max(rollValues[i] - targetValue, 0);
            var success = true;

            if (difference > 0)
            {
                remainingRest -= difference;
                success = evaluatedProbe.EffektiverWert >= 0 && remainingRest >= 0;
            }

            details.Add(new TalentProbeRollDetail
            {
                Attribute = probeAttributes[i],
                BaseValue = attributeValues[i],
                TargetValue = targetValue,
                Roll = rollValues[i],
                Difference = difference,
                RemainingRest = remainingRest,
                Success = success
            });
        }

        return details;
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

    private static class TalentProbe
    {
        public static Result Check(int talentWert, int erschwernis, int[] eigenschaften, int[] wuerfe)
        {
            if (eigenschaften.Length != 3 || wuerfe.Length != 3)
                throw new ArgumentException("Es werden genau 3 Eigenschaften und 3 Wuerfe benoetigt.");

            var anzahlEinser = 0;
            var anzahlZwanziger = 0;

            for (var i = 0; i < 3; i++)
            {
                if (wuerfe[i] == 1)
                {
                    anzahlEinser++;
                }

                if (wuerfe[i] == 20)
                {
                    anzahlZwanziger++;
                }
            }

            var effektiverWert = talentWert - erschwernis;

            if (anzahlZwanziger >= 2)
            {
                return new Result { Status = TalentProbeStatus.Patzer, Rest = 0, EffektiverWert = effektiverWert };
            }

            if (anzahlEinser >= 2)
            {
                return new Result
                {
                    Status = TalentProbeStatus.GluecklicherWurf, Rest = 0, EffektiverWert = effektiverWert
                };
            }

            var rest = Math.Max(effektiverWert, 0);

            for (var i = 0; i < 3; i++)
            {
                var grenze = effektiverWert >= 0
                    ? eigenschaften[i]
                    : eigenschaften[i] + effektiverWert;

                if (wuerfe[i] > grenze)
                {
                    rest -= wuerfe[i] - grenze;
                }
            }

            return new Result
            {
                Status = rest >= 0 ? TalentProbeStatus.Bestanden : TalentProbeStatus.NichtBestanden,
                Rest = rest,
                EffektiverWert = effektiverWert
            };
        }

        public sealed class Result
        {
            public TalentProbeStatus Status { get; init; }
            public int Rest { get; init; }
            public int EffektiverWert { get; init; }
        }
    }
}