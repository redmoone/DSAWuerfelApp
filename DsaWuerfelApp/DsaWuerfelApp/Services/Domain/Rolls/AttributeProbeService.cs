using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class AttributeProbeService(DiceService diceService)
{
    public AttributeRollResultDto RollAttributeProbe(
        ResolvedAttributeRollRequest request,
        string playerName = "Unbekannt")
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Attributes.Count == 0 || request.Attributes.Count > 3)
        {
            throw new ArgumentException("Es muessen zwischen 1 und 3 Eigenschaften ausgewaehlt werden.",
                nameof(request));
        }

        if (request.Attributes.Count != request.AttributeValues.Length)
        {
            throw new ArgumentException("Eigenschaften und Eigenschaftswerte muessen deckungsgleich sein.",
                nameof(request));
        }

        DiceService.ValidateModifier(request.BasisModifier);

        var timestamp = DateTime.UtcNow;
        var attributeNames = request.Attributes.ToArray();
        var rolls = diceService.RollDice([new DiceRollGroupDto(20, request.Attributes.Count)]);
        var effectiveModifier = request.BasisModifier + request.SchlechteEigenschaftModifier;
        var details = BuildDetails(attributeNames, request.AttributeValues, rolls, effectiveModifier);
        var successCount = details.Count(detail => detail.Success);
        var equation = DiceResultFactory.CreateEquation(rolls, 0);
        var historyEntry = DiceResultFactory.CreateHistoryEntry(playerName, timestamp, equation);

        return new AttributeRollResultDto(
            playerName,
            timestamp,
            request.Attributes.Label,
            request.BasisModifier,
            request.SchlechteEigenschaftName,
            request.SchlechteEigenschaftModifier,
            effectiveModifier,
            successCount == details.Length,
            successCount,
            details.Length - successCount,
            details,
            BuildRequirement(attributeNames, request, rolls, effectiveModifier),
            rolls,
            equation,
            historyEntry);
    }

    private static AttributeRollDetailDto[] BuildDetails(
        IReadOnlyList<string> attributeNames,
        IReadOnlyList<int> attributeValues,
        IReadOnlyList<DiceRollDto> rolls,
        int effectiveModifier)
    {
        var details = new List<AttributeRollDetailDto>(attributeNames.Count);

        for (var index = 0; index < attributeNames.Count; index++)
        {
            var roll = rolls[index].Value;
            var baseValue = attributeValues[index];
            var targetValue = Math.Clamp(baseValue - effectiveModifier, 0, 20);
            var difference = Math.Max(roll - targetValue, 0);

            details.Add(new AttributeRollDetailDto(
                attributeNames[index],
                baseValue,
                targetValue,
                roll,
                difference,
                roll <= targetValue));
        }

        return details.ToArray();
    }

    private static AttributeRollRequirementDto? BuildRequirement(
        IReadOnlyList<string> attributeNames,
        ResolvedAttributeRollRequest request,
        IReadOnlyList<DiceRollDto> rolls,
        int effectiveModifier)
    {
        if (attributeNames.Count != 3)
        {
            return null;
        }

        var details = new List<AttributeRollRequirementDetailDto>(capacity: 3);
        var requiredCompensation = 0;

        for (var index = 0; index < attributeNames.Count; index++)
        {
            var difference = Math.Max(rolls[index].Value - request.AttributeValues[index], 0);
            requiredCompensation += difference;

            details.Add(new AttributeRollRequirementDetailDto(
                attributeNames[index],
                request.AttributeValues[index],
                rolls[index].Value,
                difference));
        }

        return new AttributeRollRequirementDto(
            string.Join('/', attributeNames),
            request.BasisModifier,
            request.SchlechteEigenschaftName,
            request.SchlechteEigenschaftModifier,
            effectiveModifier,
            Math.Max(effectiveModifier + requiredCompensation, 0),
            requiredCompensation,
            details.ToArray());
    }
}

public sealed record ResolvedAttributeRollRequest(
    AttributeSelection Attributes,
    int[] AttributeValues,
    int BasisModifier,
    string? SchlechteEigenschaftName,
    int SchlechteEigenschaftModifier);