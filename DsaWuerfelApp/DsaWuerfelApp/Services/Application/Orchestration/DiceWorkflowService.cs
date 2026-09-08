using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class DiceWorkflowService(
    GetDicePageContextHandler getDicePageContextHandler,
    GetProbeInfoHandler getProbeInfoHandler,
    RollFreeHandler rollFreeHandler,
    RollTalentHandler rollTalentHandler,
    RollAttributeHandler rollAttributeHandler,
    RollBadTraitHandler rollBadTraitHandler,
    RollMasterTalentHandler rollMasterTalentHandler,
    RollMasterAttributeHandler rollMasterAttributeHandler)
{
    public Task<DicePageContextDto> GetContextAsync(Guid? heroId, string? sessionId, string userId, CancellationToken cancellationToken = default)
    {
        return getDicePageContextHandler.HandleAsync(heroId, sessionId, userId, cancellationToken);
    }

    public Task<DicePageContextDto> GetCatalogContextAsync(CancellationToken cancellationToken = default)
    {
        return getDicePageContextHandler.HandleCatalogAsync(cancellationToken);
    }

    public Task<ProbeInfoResultDto> GetProbeInfoAsync(
        ProbeInfoRequestDto request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        return getProbeInfoHandler.HandleAsync(request, userId, cancellationToken);
    }

    public FreeRollResultDto RollFree(FreeRollRequestDto request, string playerName = "Unbekannt")
    {
        return rollFreeHandler.Handle(request, playerName);
    }

    public Task<TalentRollResultDto> RollTalentAsync(
        TalentRollRequestDto request,
        string userId,
        string playerName = "Unbekannt",
        CancellationToken cancellationToken = default)
    {
        return rollTalentHandler.HandleAsync(request, userId, playerName, cancellationToken);
    }

    public Task<AttributeRollResultDto> RollAttributeAsync(
        AttributeRollRequestDto request,
        string userId,
        string playerName = "Unbekannt",
        CancellationToken cancellationToken = default)
    {
        return rollAttributeHandler.HandleAsync(request, userId, playerName, cancellationToken);
    }

    public Task<BadTraitRollResultDto> RollBadTraitAsync(
        BadTraitRollRequestDto request,
        string userId,
        string playerName = "Unbekannt",
        CancellationToken cancellationToken = default)
    {
        return rollBadTraitHandler.HandleAsync(request, userId, playerName, cancellationToken);
    }

    public Task<MasterTalentRollTargetResultDto[]> RollMasterTalentAsync(
        MasterTalentRollRequestDto request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        return rollMasterTalentHandler.HandleAsync(request, userId, cancellationToken);
    }

    public Task<MasterAttributeRollTargetResultDto[]> RollMasterAttributeAsync(
        MasterAttributeRollRequestDto request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        return rollMasterAttributeHandler.HandleAsync(request, userId, cancellationToken);
    }
}
