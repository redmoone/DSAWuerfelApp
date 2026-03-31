using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class GetProbeInfoHandler(
    HeroContextReader heroContextReader,
    TalentCatalogService talentCatalogService)
{
    public async Task<ProbeInfoResultDto> HandleAsync(
        ProbeInfoRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ProbeValue))
        {
            throw new ArgumentException("Bitte zuerst eine Probe auswählen.", nameof(request));
        }

        var hero = await heroContextReader.LoadOptionalAsync(request.HeroId, cancellationToken);
        return talentCatalogService.BuildProbeInfo(hero, request.ProbeValue, request.Modifier, request.BadTraitName);
    }
}