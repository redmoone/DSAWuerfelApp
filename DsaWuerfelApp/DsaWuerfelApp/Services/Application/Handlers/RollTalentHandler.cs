using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class RollTalentHandler(
    HeroContextReader heroContextReader,
    ProbeResolutionService probeResolutionService,
    TalentProbeService talentProbeService,
    BadTraitResolver badTraitResolver)
{
    public async Task<TalentRollResultDto> HandleAsync(
        TalentRollRequestDto request,
        string playerName = "Unbekannt",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var hero = request.HeroId.HasValue
            ? await heroContextReader.LoadRequiredAsync(request.HeroId.Value, cancellationToken)
            : null;
        var probeData = hero is null
            ? probeResolutionService.ResolveProbeOrCatalog(null, request.TalentKey, request.SpellOptionValues)
            : probeResolutionService.ResolveProbe(hero, request.TalentKey, request.SpellOptionValues);
        var probe = ProbeAttributes.Create(probeData.ProbeData.Probe);
        if (hero is not null && probe.ToArray().Any(attribute => !hero.Eigenschaften.ContainsKey(attribute)))
        {
            throw new InvalidOperationException(
                "Die ausgewaehlte Probe enthaelt variable oder unbekannte Eigenschaften und kann aktuell nicht automatisiert gewuerfelt werden.");
        }

        var badTrait = badTraitResolver.ResolveOptional(hero, request.BadTraitName);
        var attributeValues = hero?.Eigenschaften ?? HeroAttributeCatalog.DefaultValues;

        return talentProbeService.RollTalentProbe(
            new ResolvedTalentRollRequest(
                probeData.Name,
                probeData.ProbeData.Wert,
                probe,
                probe.ResolveValues(attributeValues),
                request.Modifier,
                probeData.SpecializationName,
                probeData.SpecializationModifier,
                badTrait?.Name,
                badTrait?.TalentModifier ?? 0,
                ForcedRollValues.CreateOptional(request.ForcedRollsText, 3)),
            playerName);
    }
}
