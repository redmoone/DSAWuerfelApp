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

        if (request.Probe.Count != 3 || request.AttributeValues.Length != 3)
        {
            throw new ArgumentException("A talent probe requires exactly three attributes.", nameof(request));
        }

        var timestamp = DateTime.UtcNow;
        var rolledDice = CreateRolls(request.ForcedRolls);
        var rollValues = rolledDice.Select(roll => roll.Value).ToArray();
        var totalModifier = request.BasisModifier + request.SchlechteEigenschaftModifier;
        var evaluatedProbe = TalentProbe.Check(request.TalentValue, totalModifier, request.AttributeValues, rollValues);
        var details = BuildRollDetails(
            request.Probe.ToArray(),
            request.AttributeValues,
            rollValues,
            request.TalentValue,
            evaluatedProbe);
        var probeSuccess = evaluatedProbe.Status is TalentProbeStatus.Bestanden or TalentProbeStatus.GluecklicherWurf;
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
            string.IsNullOrWhiteSpace(request.SchlechteEigenschaftName)
                ? null
                : request.SchlechteEigenschaftName.Trim(),
            request.SchlechteEigenschaftModifier,
            evaluatedProbe.EffektiverWert,
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
        TalentProbe.Result evaluatedProbe)
    {
        var details = new List<TalentRollDetailDto>(capacity: 3);
        var remainingRest = Math.Min(Math.Max(evaluatedProbe.EffektiverWert, 0), talentValue);

        for (var index = 0; index < probeAttributes.Count; index++)
        {
            var targetValue = evaluatedProbe.EffektiverWert >= 0
                ? attributeValues[index]
                : attributeValues[index] + evaluatedProbe.EffektiverWert;
            var difference = Math.Max(rollValues[index] - targetValue, 0);
            var success = true;

            if (difference > 0)
            {
                remainingRest -= difference;
                success = evaluatedProbe.EffektiverWert >= 0 && remainingRest >= 0;
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

    private static class TalentProbe
    {
        public static Result Check(int talentWert, int erschwernis, int[] eigenschaften, int[] wuerfe)
        {
            if (eigenschaften.Length != 3 || wuerfe.Length != 3)
            {
                throw new ArgumentException("Es werden genau 3 Eigenschaften und 3 Würfe benötigt.");
            }

            var anzahlEinser = 0;
            var anzahlZwanziger = 0;

            for (var index = 0; index < 3; index++)
            {
                if (wuerfe[index] == 1)
                {
                    anzahlEinser++;
                }

                if (wuerfe[index] == 20)
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

            var rest = Math.Min(Math.Max(effektiverWert, 0), talentWert);

            for (var index = 0; index < 3; index++)
            {
                var grenze = effektiverWert >= 0
                    ? eigenschaften[index]
                    : eigenschaften[index] + effektiverWert;

                if (wuerfe[index] > grenze)
                {
                    rest -= wuerfe[index] - grenze;
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

public sealed record ResolvedTalentRollRequest(
    string TalentName,
    int TalentValue,
    ProbeAttributes Probe,
    int[] AttributeValues,
    int BasisModifier,
    string? SchlechteEigenschaftName,
    int SchlechteEigenschaftModifier,
    ForcedRollValues? ForcedRolls);