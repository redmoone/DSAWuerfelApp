using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class HeroContextReader(
    IHeroReadRepository heroReadRepository, SessionService sessionService)
{
    public async Task<Hero?> LoadOptionalAsync(Guid? heroId, string userId, CancellationToken cancellationToken = default)
    {
        return heroId.HasValue
            ? await LoadRequiredAsync(heroId.Value, userId, cancellationToken)
            : await heroReadRepository.GetActiveAsync(userId, cancellationToken);
    }

    public async Task<Hero?> LoadContextAsync(Guid? heroId, string? sessionId, string userId, CancellationToken cancellationToken = default)
    {
        if (!heroId.HasValue) return await LoadOptionalAsync(null, userId, cancellationToken);
        var owned = await heroReadRepository.GetOwnedByIdAsync(heroId.Value, userId, cancellationToken);
        if (owned is not null) return owned;
        if (string.IsNullOrWhiteSpace(sessionId)) throw MissingHero();
        try { return await LoadRequiredForMasterAsync(heroId.Value, sessionId, userId, cancellationToken); }
        catch (RequestRejectedException exception) when (exception.Reason == RequestRejectionReason.Forbidden) { throw MissingHero(); }
    }

    public async Task<Hero> LoadRequiredForMasterAsync(Guid heroId, string sessionId, string userId, CancellationToken cancellationToken = default)
    {
        var target = sessionService.GetMasterHeroTargets(sessionId, userId).FirstOrDefault(target => target.HeroId == heroId)
            ?? throw MissingHero();
        return await LoadRequiredAsync(heroId, target.UserId, cancellationToken);
    }

    public async Task<IReadOnlyList<(DsaWuerfelApp.Shared.MasterRollTargetDto Target, Hero Hero)>> ResolveMasterTargetsAsync(
        string sessionId, string userId, DsaWuerfelApp.Shared.MasterRollTargetDto[] targets, CancellationToken cancellationToken)
    {
        var snapshot = sessionService.GetMasterHeroTargets(sessionId, userId);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(DsaWuerfelApp.Shared.MasterRollTargetDto, Hero)>();
        foreach (var selection in targets)
        {
            if (selection is null || !seen.Add(selection.UserId))
                throw new RequestRejectedException(RequestRejectionReason.Validation, "Ungültige oder doppelte Zielauswahl.");
            var target = snapshot.FirstOrDefault(target => target.UserId == selection.UserId);
            if (target is null || target.HeroId != selection.HeroId)
                throw new RequestRejectedException(RequestRejectionReason.Forbidden, "Zielheld hat sich geändert oder gehört nicht zur Sitzung. Bitte aktualisieren.");
            var hero = await LoadRequiredAsync(target.HeroId, target.UserId, cancellationToken);
            result.Add((new(target.UserId, target.PlayerName, hero.Id, hero.Name), hero));
        }
        return result;
    }

    public async Task<Hero> LoadRequiredAsync(Guid heroId, string userId, CancellationToken cancellationToken = default)
    {
        return await heroReadRepository.GetOwnedByIdAsync(heroId, userId, cancellationToken)
               ?? throw MissingHero();
    }

    private static RequestRejectedException MissingHero() => new(RequestRejectionReason.NotFound, "Der ausgewählte Held konnte nicht geladen werden.");

}
