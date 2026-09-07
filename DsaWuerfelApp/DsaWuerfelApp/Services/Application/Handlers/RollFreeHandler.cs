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

        DiceRollDto[] rolls;
        try
        {
            DiceService.ValidateModifier(request.Modifier);
            rolls = diceService.RollDice(request.Dice);
        }
        catch (ArgumentException exception)
        {
            throw new RequestRejectedException(RequestRejectionReason.Validation, exception.Message);
        }

        var timestamp = DateTime.UtcNow;
        var equation = DiceResultFactory.CreateEquation(rolls, request.Modifier);
        var historyEntry = DiceResultFactory.CreateHistoryEntry(playerName, timestamp, equation);

        return new FreeRollResultDto(playerName, timestamp, equation, historyEntry);
    }
}
