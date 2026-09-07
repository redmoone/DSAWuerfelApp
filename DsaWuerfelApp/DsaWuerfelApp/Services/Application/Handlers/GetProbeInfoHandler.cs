using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class GetProbeInfoHandler(
    HeroContextReader heroContextReader,
    ProbeInfoService probeInfoService)
{
    public async Task<ProbeInfoResultDto> HandleAsync(
        ProbeInfoRequestDto request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ProbeValue))
        {
            throw new ArgumentException("Bitte zuerst eine Probe auswählen.", nameof(request));
        }

        var hero = await heroContextReader.LoadOptionalAsync(request.HeroId, userId, cancellationToken);
        return probeInfoService.BuildProbeInfo(
            hero,
            request.ProbeValue,
            request.Modifier,
            request.BadTraitName,
            request.SpellOptionValues);
    }
}
