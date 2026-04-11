using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class RollMasterAttributeHandler(RollAttributeHandler rollAttributeHandler)
{
    public async Task<MasterAttributeRollTargetResultDto[]> HandleAsync(
        MasterAttributeRollRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Targets.Length == 0)
        {
            throw new InvalidOperationException("Bitte mindestens einen Spieler auswählen.");
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
                        true),
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
            catch (Exception exception)
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
