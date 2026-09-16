namespace DsaWuerfelApp.Shared;

public static class CombatWoundRules
{
    public static CombatWoundThresholdsDto CreateThresholds(
        int? woundThreshold,
        int? constitution)
    {
        ValidateThreshold(woundThreshold, nameof(woundThreshold));
        ValidateThreshold(constitution, nameof(constitution));

        int? third = constitution.HasValue
            ? checked((constitution.Value * 3 + 1) / 2)
            : null;
        return new CombatWoundThresholdsDto(woundThreshold, constitution, third);
    }

    public static CombatWoundApplicationDto Resolve(
        int? structurePoints,
        int? currentLeP,
        CombatWoundZone? zone,
        int? currentWounds,
        CombatWoundThresholdsDto thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        ValidateThreshold(thresholds.First, nameof(thresholds.First));
        ValidateThreshold(thresholds.Second, nameof(thresholds.Second));
        ValidateThreshold(thresholds.Third, nameof(thresholds.Third));

        if (structurePoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(structurePoints), "SP darf nicht negativ sein.");
        }

        if (currentWounds is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(currentWounds), "Der Wundstand muss zwischen 0 und 3 liegen.");
        }

        var resultingWounds = currentWounds;
        if (currentWounds.HasValue && structurePoints.HasValue)
        {
            var thresholdWounds = ResolveThresholdWounds(structurePoints.Value, thresholds);
            resultingWounds = Math.Clamp(currentWounds.Value + thresholdWounds, 0, 3);
        }

        int? lepAfter = currentLeP;
        if (currentLeP.HasValue && structurePoints.HasValue)
        {
            lepAfter = currentLeP.Value - structurePoints.Value;
        }

        var addedWounds = currentWounds.HasValue && resultingWounds.HasValue
            ? resultingWounds.Value - currentWounds.Value
            : 0;
        return new CombatWoundApplicationDto(
            structurePoints,
            currentLeP,
            lepAfter,
            zone,
            currentWounds,
            resultingWounds,
            addedWounds,
            lepAfter.HasValue && lepAfter.Value <= 0);
    }

    private static int ResolveThresholdWounds(
        int structurePoints,
        CombatWoundThresholdsDto thresholds)
    {
        if (thresholds.Third.HasValue && structurePoints > thresholds.Third.Value)
        {
            return 3;
        }

        if (thresholds.Second.HasValue && structurePoints > thresholds.Second.Value)
        {
            return 2;
        }

        return thresholds.First.HasValue && structurePoints > thresholds.First.Value ? 1 : 0;
    }

    private static void ValidateThreshold(int? value, string parameterName)
    {
        if (value is < 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Wundschwellen müssen positiv sein.");
        }
    }
}
