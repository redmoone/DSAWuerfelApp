using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class SchlechteEigenschaftProbeService(DiceService diceService)
{
    public BadTraitRollResultDto RollProbe(
        ResolvedBadTraitRollRequest request,
        string playerName = "Unbekannt")
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.EigenschaftName))
        {
            throw new ArgumentException("EigenschaftName is required.", nameof(request));
        }

        if (request.EigenschaftWert is < 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(request.EigenschaftWert));
        }

        var timestamp = DateTime.UtcNow;
        var roll = CreateRoll(request.ForcedRolls);
        var probeMisslungen = roll.Value <= request.EigenschaftWert;
        var equation = DiceResultFactory.CreateEquation([roll], 0);
        var historyEntry = DiceResultFactory.CreateHistoryEntry(playerName, timestamp, equation);

        return new BadTraitRollResultDto(
            playerName,
            timestamp,
            request.EigenschaftName,
            request.EigenschaftWert,
            request.EigenschaftWert,
            roll,
            probeMisslungen
                ? SchlechteEigenschaftProbeStatus.Misslungen
                : SchlechteEigenschaftProbeStatus.Bestanden,
            !probeMisslungen,
            probeMisslungen,
            Math.Abs(roll.Value - request.EigenschaftWert),
            equation,
            historyEntry);
    }

    private DiceRollDto CreateRoll(ForcedRollValues? forcedRolls)
    {
#if !DEBUG
        return diceService.RollDice([new DiceRollGroupDto(20, 1)]).Single();
#else
        if (forcedRolls is null)
        {
            return diceService.RollDice([new DiceRollGroupDto(20, 1)]).Single();
        }

        if (forcedRolls.Count != 1)
        {
            throw new ArgumentException(
                "Fuer die direkte Probe auf eine schlechte Eigenschaft ist genau 1 Testwurf noetig.");
        }

        return new DiceRollDto(20, forcedRolls.SingleValue);
#endif
    }
}

public sealed record ResolvedBadTraitRollRequest(
    string EigenschaftName,
    int EigenschaftWert,
    ForcedRollValues? ForcedRolls);