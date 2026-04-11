using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class RollMasterTalentHandler(RollTalentHandler rollTalentHandler)
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
                var result = await rollTalentHandler.HandleAsync(
                    new TalentRollRequestDto(
                        null,
                        target.HeroId,
                        request.TalentKey,
                        request.Modifier,
                        request.BadTraitName,
                        request.SpellOptionValues,
                        request.ForcedRollsText,
                        true),
                    target.PlayerName,
                    cancellationToken);

                results.Add(new MasterTalentRollTargetResultDto(
                    target.UserId,
                    target.PlayerName,
                    target.HeroId,
                    target.HeroName,
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
                    exception.Message));
            }
        }

        return results.ToArray();
    }
}
