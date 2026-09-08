using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class RollMasterAttributeHandler(HeroContextReader heroContextReader, AttributeProbeService attributeProbeService, BadTraitResolver badTraitResolver)
{
    public async Task<MasterAttributeRollTargetResultDto[]> HandleAsync(
        MasterAttributeRollRequestDto request,
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

        var results = new List<MasterAttributeRollTargetResultDto>(request.Targets.Length);

        var resolved = await heroContextReader.ResolveMasterTargetsAsync(request.SessionId, userId, request.Targets, cancellationToken);
        foreach (var (target, hero) in resolved)
        {
            try
            {
                var attributes = AttributeSelection.Create(request.Attributes);
                var badTrait = badTraitResolver.ResolveOptional(hero, request.BadTraitName);
                var result = attributeProbeService.RollAttributeProbe(new ResolvedAttributeRollRequest(
                    attributes, attributes.ResolveValues(hero.Eigenschaften), request.Modifier,
                    badTrait?.Name, badTrait?.AttributeModifier ?? 0, null, null), target.PlayerName);

                results.Add(new MasterAttributeRollTargetResultDto(
                    target.UserId,
                    target.PlayerName,
                    target.HeroId,
                    target.HeroName,
                    result,
                    null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (RequestRejectedException exception)
            {
                results.Add(new MasterAttributeRollTargetResultDto(
                    target.UserId,
                    target.PlayerName,
                    target.HeroId,
                    target.HeroName,
                    null,
                    exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                results.Add(new MasterAttributeRollTargetResultDto(
                    target.UserId,
                    target.PlayerName,
                    target.HeroId,
                    target.HeroName,
                    null,
                    exception.Message));
            }
        }

        return results.ToArray();
    }
}
