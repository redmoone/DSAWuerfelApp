using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class RollAttributeHandler(
    HeroContextReader heroContextReader,
    AttributeProbeService attributeProbeService,
    BadTraitResolver badTraitResolver)
{
    public async Task<AttributeRollResultDto> HandleAsync(
        AttributeRollRequestDto request,
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

        var hero = await heroContextReader.LoadOptionalAsync(request.HeroId, cancellationToken);
        var attributes = AttributeSelection.Create(request.Attributes);
        var badTrait = badTraitResolver.ResolveOptional(hero, request.BadTraitName);

        return attributeProbeService.RollAttributeProbe(
            new ResolvedAttributeRollRequest(
                attributes,
                attributes.ResolveValues(hero?.Eigenschaften),
                request.Modifier,
                badTrait?.Name,
                badTrait?.AttributeModifier ?? 0,
                null,
                null),
            playerName);
    }
}
