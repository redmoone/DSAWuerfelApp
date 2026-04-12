using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class RollMasterTalentHandler(
    HeroContextReader heroContextReader,
    ProbeResolutionService probeResolutionService,
    AttributeProbeService attributeProbeService,
    BadTraitResolver badTraitResolver)
{
    public async Task<MasterTalentRollTargetResultDto[]> HandleAsync(
        MasterTalentRollRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Targets.Length == 0)
        {
            throw new InvalidOperationException("Bitte mindestens einen Spieler auswählen.");
        }

        var results = new List<MasterTalentRollTargetResultDto>(request.Targets.Length);

        foreach (var target in request.Targets)
        {
            try
            {
                var hero = await heroContextReader.LoadRequiredAsync(target.HeroId, cancellationToken);
                if (!probeResolutionService.TryResolveMasterProbe(
                        hero,
                        request.TalentKey,
                        request.SpellOptionValues,
                        out var probeData,
                        out var unavailableMessage))
                {
                    results.Add(new MasterTalentRollTargetResultDto(
                        target.UserId,
                        target.PlayerName,
                        target.HeroId,
                        target.HeroName,
                        null,
                        null,
                        unavailableMessage));
                    continue;
                }

                var probe = ProbeAttributes.Create(probeData.ProbeData.Probe);
                var badTrait = badTraitResolver.ResolveOptional(hero, request.BadTraitName);
                var result = attributeProbeService.RollAttributeProbe(
                    new ResolvedAttributeRollRequest(
                        AttributeSelection.Create(probe.ToArray()),
                        probe.ResolveValues(hero.Eigenschaften),
                        request.Modifier + probeData.SpecializationModifier,
                        badTrait?.Name,
                        badTrait?.TalentModifier ?? 0,
                        probeData.Name,
                        ForcedRollValues.CreateOptional(request.ForcedRollsText, 3)),
                    target.PlayerName);

                results.Add(new MasterTalentRollTargetResultDto(
                    target.UserId,
                    target.PlayerName,
                    target.HeroId,
                    target.HeroName,
                    null,
                    result,
                    null));
            }
            catch (Exception exception)
            {
                results.Add(new MasterTalentRollTargetResultDto(
                    target.UserId,
                    target.PlayerName,
                    target.HeroId,
                    target.HeroName,
                    null,
                    null,
                    exception.Message));
            }
        }

        return results.ToArray();
    }
}
