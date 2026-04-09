using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class RollTalentHandler(
    HeroContextReader heroContextReader,
    TalentCatalogService talentCatalogService,
    TalentProbeService talentProbeService,
    BadTraitResolver badTraitResolver)
{
    public async Task<TalentRollResultDto> HandleAsync(
        TalentRollRequestDto request,
        string playerName = "Unbekannt",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var hero = await heroContextReader.LoadRequiredAsync(request.HeroId, cancellationToken);
        var probeData = talentCatalogService.ResolveProbe(hero, request.TalentKey);
        var probe = ProbeAttributes.Create(probeData.ProbeData.Probe);
        if (probe.ToArray().Any(attribute => !hero.Eigenschaften.ContainsKey(attribute)))
        {
            throw new InvalidOperationException(
                "Die ausgewählte Probe enthält variable oder unbekannte Eigenschaften und kann aktuell nicht automatisiert gewürfelt werden.");
        }

        var badTrait = badTraitResolver.ResolveOptional(hero, request.BadTraitName);

        return talentProbeService.RollTalentProbe(
            new ResolvedTalentRollRequest(
                probeData.Name,
                probeData.ProbeData.Wert,
                probe,
                probe.ResolveValues(hero.Eigenschaften),
                request.Modifier,
                probeData.SpecializationName,
                probeData.SpecializationModifier,
                badTrait?.Name,
                badTrait?.TalentModifier ?? 0,
                ForcedRollValues.CreateOptional(request.ForcedRollsText, 3)),
            playerName);
    }
}