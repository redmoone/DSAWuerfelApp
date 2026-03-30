using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class DiceWorkflowService(
    GetDicePageContextHandler getDicePageContextHandler,
    GetProbeInfoHandler getProbeInfoHandler,
    RollFreeHandler rollFreeHandler,
    RollTalentHandler rollTalentHandler,
    RollAttributeHandler rollAttributeHandler,
    RollBadTraitHandler rollBadTraitHandler)
{
    public Task<DicePageContextDto> GetContextAsync(Guid? heroId, CancellationToken cancellationToken = default)
    {
        return getDicePageContextHandler.HandleAsync(heroId, cancellationToken);
    }

    public Task<ProbeInfoResultDto> GetProbeInfoAsync(
        ProbeInfoRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return getProbeInfoHandler.HandleAsync(request, cancellationToken);
    }

    public FreeRollResultDto RollFree(FreeRollRequestDto request, string playerName = "Unbekannt")
    {
        return rollFreeHandler.Handle(request, playerName);
    }

    public Task<TalentRollResultDto> RollTalentAsync(
        TalentRollRequestDto request,
        string playerName = "Unbekannt",
        CancellationToken cancellationToken = default)
    {
        return rollTalentHandler.HandleAsync(request, playerName, cancellationToken);
    }

    public Task<AttributeRollResultDto> RollAttributeAsync(
        AttributeRollRequestDto request,
        string playerName = "Unbekannt",
        CancellationToken cancellationToken = default)
    {
        return rollAttributeHandler.HandleAsync(request, playerName, cancellationToken);
    }

    public Task<BadTraitRollResultDto> RollBadTraitAsync(
        BadTraitRollRequestDto request,
        string playerName = "Unbekannt",
        CancellationToken cancellationToken = default)
    {
        return rollBadTraitHandler.HandleAsync(request, playerName, cancellationToken);
    }
}