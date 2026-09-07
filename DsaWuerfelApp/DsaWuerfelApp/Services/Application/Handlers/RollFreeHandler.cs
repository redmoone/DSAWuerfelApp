using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class RollFreeHandler(DiceService diceService)
{
    public FreeRollResultDto Handle(FreeRollRequestDto request, string playerName = "Unbekannt")
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsHidden)
        {
            throw new RequestRejectedException(
                RequestRejectionReason.Validation,
                "Verdeckte Würfe sind derzeit nicht verfügbar.");
        }

        DiceService.ValidateModifier(request.Modifier);

        var timestamp = DateTime.UtcNow;
        var rolls = diceService.RollDice(request.Dice);
        var equation = DiceResultFactory.CreateEquation(rolls, request.Modifier);
        var historyEntry = DiceResultFactory.CreateHistoryEntry(playerName, timestamp, equation);

        return new FreeRollResultDto(playerName, timestamp, equation, historyEntry);
    }
}
