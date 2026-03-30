using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public class SchlechteEigenschaftProbeService(DiceService diceService)
{
    public SchlechteEigenschaftProbeResult RollProbe(
        SchlechteEigenschaftProbeRequest request,
        string playerName = "Unbekannt")
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.EigenschaftName))
            throw new ArgumentException("EigenschaftName is required.", nameof(request));

        if (request.EigenschaftWert is < 0 or > 20)
            throw new ArgumentOutOfRangeException(nameof(request.EigenschaftWert));

        var rollResult = CreateRollResult(request.ForcedRoll, playerName);
        var roll = rollResult.Rolls.Single();
        var probeMisslungen = roll.Value <= request.EigenschaftWert;

        return new SchlechteEigenschaftProbeResult
        {
            PlayerName = playerName,
            Timestamp = rollResult.Timestamp,
            EigenschaftName = request.EigenschaftName,
            EigenschaftWert = request.EigenschaftWert,
            TargetValue = request.EigenschaftWert,
            Roll = roll,
            Status = probeMisslungen
                ? SchlechteEigenschaftProbeStatus.Misslungen
                : SchlechteEigenschaftProbeStatus.Bestanden,
            Success = !probeMisslungen,
            EigenschaftSetztSichDurch = probeMisslungen,
            Margin = Math.Abs(roll.Value - request.EigenschaftWert)
        };
    }

    public static RollResult ToRollResult(SchlechteEigenschaftProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new RollResult
        {
            PlayerName = result.PlayerName,
            Timestamp = result.Timestamp,
            Rolls = [result.Roll],
            Modifier = 0,
            TotalSum = result.Roll.Value
        };
    }

    private RollResult CreateRollResult(int? forcedRoll, string playerName)
    {
#if !DEBUG
        return diceService.RollSet([new DiceGroup(20, 1)], 0, playerName);
#else
        if (forcedRoll is null)
        {
            return diceService.RollSet([new DiceGroup(20, 1)], 0, playerName);
        }

        if (forcedRoll is < 1 or > 20)
            throw new ArgumentException("ForcedRoll muss zwischen 1 und 20 liegen.", nameof(forcedRoll));

        return new RollResult
        {
            PlayerName = playerName,
            Timestamp = DateTime.UtcNow,
            Rolls = [new SingleRoll { Sides = 20, Value = forcedRoll.Value }],
            Modifier = 0,
            TotalSum = forcedRoll.Value
        };
#endif
    }
}