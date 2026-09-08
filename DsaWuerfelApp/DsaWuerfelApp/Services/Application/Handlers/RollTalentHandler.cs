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
        string userId,
        string playerName = "Unbekannt",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsHidden)
        {
            throw new RequestRejectedException(
                RequestRejectionReason.Validation,
                "Verdeckte Würfe sind derzeit nicht verfügbar.");
        }

        var hero = request.HeroId.HasValue
            ? await heroContextReader.LoadRequiredAsync(request.HeroId.Value, userId, cancellationToken)
            : null;
        var probeData = hero is null
            ? probeResolutionService.ResolveProbeOrCatalog(null, request.TalentKey, request.SpellOptionValues)
            : probeResolutionService.ResolveProbe(hero, request.TalentKey, request.SpellOptionValues);
        var probe = ProbeAttributes.Create(probeData.ProbeData.Probe);
        if (hero is not null && probe.ToArray().Any(attribute => !hero.Eigenschaften.ContainsKey(attribute)))
        {
            throw new RequestRejectedException(RequestRejectionReason.Validation,
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
                ForcedRollValues.CreateOptional(request.ForcedRollsText, 3),
                probeData.AutomaticSpellModifier,
                probeData.SpellPreRollZfp,
                probeData.Kind == ProbeSelectionKind.Spell
                    ? new SpellRollCalculationContext(
                        probeData.SpellRequiresManualInput,
                        probeData.SelectedSpellOptions.Select(option => option.DisplayName).ToArray())
                    : null),
            playerName);
    }
}
