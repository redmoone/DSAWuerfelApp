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

        var hero = await heroContextReader.LoadRequiredAsync(request.HeroId, cancellationToken);
        var badTrait = badTraitResolver.ResolveRequired(hero, request.BadTraitName);

        return schlechteEigenschaftProbeService.RollProbe(
            new ResolvedBadTraitRollRequest(
                badTrait.Name,
                badTrait.Value,
                ForcedRollValues.CreateOptional(request.ForcedRollsText, 1)),
            playerName);
    }
}