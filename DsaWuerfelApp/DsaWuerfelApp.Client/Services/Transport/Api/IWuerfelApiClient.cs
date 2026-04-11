using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public interface IWuerfelApiClient
{
    Task<DicePageContextDto> GetContextAsync(Guid? heroId, CancellationToken cancellationToken = default);

    Task<ProbeInfoResultDto> GetProbeInfoAsync(
        ProbeInfoRequestDto request,
        CancellationToken cancellationToken = default);

    Task<FreeRollResultDto> RollFreeAsync(
        FreeRollRequestDto request,
        CancellationToken cancellationToken = default);

    Task<TalentRollResultDto> RollTalentAsync(
        TalentRollRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AttributeRollResultDto> RollAttributeAsync(
        AttributeRollRequestDto request,
        CancellationToken cancellationToken = default);

    Task<MasterTalentRollTargetResultDto[]> RollMasterTalentAsync(
        MasterTalentRollRequestDto request,
        CancellationToken cancellationToken = default);

    Task<MasterAttributeRollTargetResultDto[]> RollMasterAttributeAsync(
        MasterAttributeRollRequestDto request,
        CancellationToken cancellationToken = default);

    Task<BadTraitRollResultDto> RollBadTraitAsync(
        BadTraitRollRequestDto request,
        CancellationToken cancellationToken = default);
}
