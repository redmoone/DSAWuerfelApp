using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class RollMasterTalentHandler(
    HeroContextReader heroContextReader,
    ProbeResolutionService probeResolutionService,
    AttributeProbeService attributeProbeService,
    TalentProbeService talentProbeService,
    BadTraitResolver badTraitResolver)
{
    public async Task<MasterTalentRollTargetResultDto[]> HandleAsync(
        MasterTalentRollRequestDto request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new RequestRejectedException(RequestRejectionReason.Validation, "F?r diesen Meisterwurf ist eine aktive Session erforderlich.");
        }

        if (request.Targets.Length == 0)
        {
            throw new RequestRejectedException(RequestRejectionReason.Validation, "Bitte mindestens einen Spieler auswählen.");
        }

        var results = new List<MasterTalentRollTargetResultDto>(request.Targets.Length);

        var resolved = await heroContextReader.ResolveMasterTargetsAsync(request.SessionId, userId, request.Targets, cancellationToken);
        foreach (var (target, hero) in resolved)
        {
            try
            {
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
                if (probeData.Kind == ProbeSelectionKind.Spell && !probeData.UsesCatalogValue)
                {
                    var result = talentProbeService.RollTalentProbe(
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
                            ForcedRollValues.CreateOptional(request.ForcedRollsText, 3),
                            probeData.AutomaticSpellModifier,
                            probeData.SpellPreRollZfp,
                            probeData.Kind == ProbeSelectionKind.Spell
                                ? new SpellRollCalculationContext(
                                    probeData.SpellRequiresManualInput,
                                    probeData.SelectedSpellOptions.Select(option => option.DisplayName).ToArray())
                                : null),
                        target.PlayerName);

                    results.Add(new MasterTalentRollTargetResultDto(
                        target.UserId,
                        target.PlayerName,
                        target.HeroId,
                        target.HeroName,
                        result,
                        null,
                        null));
                    continue;
                }

                var requirementResult = attributeProbeService.RollAttributeProbe(
                    new ResolvedAttributeRollRequest(
                        AttributeSelection.Create(probe.ToArray()),
                        probe.ResolveValues(hero.Eigenschaften),
                        request.Modifier + probeData.SpecializationModifier + probeData.AutomaticSpellModifier,
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
                    requirementResult,
                    null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (RequestRejectedException exception)
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
            catch (InvalidOperationException exception)
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
