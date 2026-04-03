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
        var talent = talentCatalogService.ResolveTalent(hero, request.TalentKey);
        var probe = ProbeAttributes.Create(talent.Talent.Probe);
        var badTrait = badTraitResolver.ResolveOptional(hero, request.BadTraitName);

        return talentProbeService.RollTalentProbe(
            new ResolvedTalentRollRequest(
                talent.Name,
                talent.Talent.Wert,
                probe,
                probe.ResolveValues(hero.Eigenschaften),
                request.Modifier,
                talent.SpecializationName,
                talent.SpecializationModifier,
                badTrait?.Name,
                badTrait?.TalentModifier ?? 0,
                ForcedRollValues.CreateOptional(request.ForcedRollsText, 3)),
            playerName);
    }
}