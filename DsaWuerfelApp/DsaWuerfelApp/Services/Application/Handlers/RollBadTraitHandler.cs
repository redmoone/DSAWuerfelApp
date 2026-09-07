using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class RollBadTraitHandler(
    HeroContextReader heroContextReader,
    BadTraitResolver badTraitResolver,
    SchlechteEigenschaftProbeService schlechteEigenschaftProbeService)
{
    public async Task<BadTraitRollResultDto> HandleAsync(
        BadTraitRollRequestDto request,
        string playerName = "Unbekannt",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var badTrait = request.HeroId.HasValue
            ? badTraitResolver.ResolveRequired(
                await heroContextReader.LoadRequiredAsync(request.HeroId.Value, cancellationToken),
                request.BadTraitName)
            : new BadTraitDto(request.BadTraitName, request.BadTraitValue, request.BadTraitValue, request.BadTraitValue);

        return schlechteEigenschaftProbeService.RollProbe(
            new ResolvedBadTraitRollRequest(
                badTrait.Name,
                badTrait.Value,
                ForcedRollValues.CreateOptional(request.ForcedRollsText, 1)),
            playerName);
    }
}
