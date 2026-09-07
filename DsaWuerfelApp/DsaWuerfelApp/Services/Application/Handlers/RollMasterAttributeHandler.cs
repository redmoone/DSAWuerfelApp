using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class RollMasterAttributeHandler(RollAttributeHandler rollAttributeHandler)
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

        foreach (var target in request.Targets)
        {
            try
            {
                var result = await rollAttributeHandler.HandleAsync(
                    new AttributeRollRequestDto(
                        null,
                        target.HeroId,
                        request.Attributes,
                        request.Modifier,
                        request.BadTraitName,
                        false),
                    userId,
                    target.PlayerName,
                    cancellationToken);

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
