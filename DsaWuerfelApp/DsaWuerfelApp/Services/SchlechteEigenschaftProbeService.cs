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
        var roll = CreateRoll(request.ForcedRollsText);
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

    private DiceRollDto CreateRoll(string? forcedRollsText)
    {
#if !DEBUG
        return diceService.RollDice([new DiceRollGroupDto(20, 1)]).Single();
#else
        if (string.IsNullOrWhiteSpace(forcedRollsText))
        {
            return diceService.RollDice([new DiceRollGroupDto(20, 1)]).Single();
        }

        var parts = forcedRollsText.Split([',', ';', '/', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 1)
        {
            throw new ArgumentException(
                "Fuer die direkte Probe auf eine schlechte Eigenschaft ist genau 1 Testwurf noetig.");
        }

        if (!int.TryParse(parts[0], out var roll) || roll is < 1 or > 20)
        {
            throw new ArgumentException("Testwuerfe muessen Zahlen von 1 bis 20 sein.");
        }

        return new DiceRollDto(20, roll);
#endif
    }
}

public sealed record ResolvedBadTraitRollRequest(
    string EigenschaftName,
    int EigenschaftWert,
    string? ForcedRollsText);