using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelContextService : IDisposable
{
    private readonly WuerfelState state;
    private readonly ActiveHeroState activeHeroState;
    private readonly SessionState sessionState;
    private readonly IWuerfelApiClient apiClient;
    private readonly WuerfelUiOperationRunner operationRunner;
    private readonly AuthState authState;
    private CancellationTokenSource? _contextCancellation;
    private long _contextVersion;
    private ContextKey? _contextKey;
    private bool _disposed;
    private sealed record ContextKey(string? SessionId, bool MasterMode, Guid? HeroId, string Targets);

    public WuerfelContextService(WuerfelState state, ActiveHeroState activeHeroState, SessionState sessionState,
        IWuerfelApiClient apiClient, WuerfelUiOperationRunner operationRunner, AuthState authState)
    {
        this.state = state; this.activeHeroState = activeHeroState; this.sessionState = sessionState;
        this.apiClient = apiClient; this.operationRunner = operationRunner; this.authState = authState;
        sessionState.ActiveSessionChanged += CheckContext;
        authState.Changed += CheckContext;
    }

    private void CheckContext()
    {
        if (!authState.Current.IsAuthenticated || _contextKey?.SessionId != sessionState.ActiveSessionId) Invalidate();
    }

    private void Invalidate()
    {
        ++_contextVersion;
        _contextCancellation?.Cancel();
        _contextCancellation?.Dispose();
        _contextCancellation = null;
        _contextKey = null;
        CancelProbeInfoRefresh();
    }

    public void Dispose()
    {
        _disposed = true;
        sessionState.ActiveSessionChanged -= CheckContext;
        authState.Changed -= CheckContext;
        Invalidate();
    }

    private CancellationTokenSource? _probeInfoRefreshCancellation;
    private int _probeInfoRefreshVersion;

    public Task LoadContextAsync(
        IReadOnlyList<SessionPlayerDto>? masterTargets = null,
        bool useCatalogWhenNoMasterTargets = false,
        bool forceRefresh = false)
    {
        var targets = (masterTargets ?? state.Current.MasterTargets).Where(target => target.ActiveHeroId.HasValue).ToArray();
        var sessionId = sessionState.ActiveSessionId;
        var heroId = activeHeroState.CurrentHero?.Id;
        var key = new ContextKey(sessionId, useCatalogWhenNoMasterTargets, heroId,
            System.Text.Json.JsonSerializer.Serialize(targets.OrderBy(target => target.UserId, StringComparer.Ordinal)
                .Select(target => (target.UserId, target.ActiveHeroId)).Select(pair => new { pair.UserId, pair.ActiveHeroId })));
        if (!forceRefresh && key == _contextKey) return Task.CompletedTask;
        Invalidate();
        _contextKey = key;
        var version = _contextVersion;
        _contextCancellation = new CancellationTokenSource();
        var token = _contextCancellation.Token;
        bool IsCurrent() => !_disposed && !token.IsCancellationRequested && version == _contextVersion && sessionId == sessionState.ActiveSessionId;
        return operationRunner.RunAsync(async () =>
        {
            try
            {
                var loadedContext = targets.Length > 0
                    ? await LoadMasterContextAsync(targets, sessionId, token)
                    : useCatalogWhenNoMasterTargets
                        ? new LoadedDicePageContext(await apiClient.GetCatalogContextAsync(token), new Dictionary<string, IReadOnlyList<BadTraitOwnerInfo>>(StringComparer.Ordinal))
                        : new LoadedDicePageContext(await apiClient.GetContextAsync(heroId, sessionId, token), new Dictionary<string, IReadOnlyList<BadTraitOwnerInfo>>(StringComparer.Ordinal));
                if (IsCurrent()) state.ApplyContext(loadedContext.Context, loadedContext.BadTraitOwners);
            }
            catch (Exception) when (!IsCurrent()) { }
            catch
            {
                if (IsCurrent()) _contextKey = null;
                throw;
            }
        });
    }

    public Task RefreshProbeInfoAsync()
    {
        if (string.IsNullOrWhiteSpace(state.Current.SelectedProbeValue))
        {
            CancelProbeInfoRefresh();
            state.ClearProbeInfo();
            return Task.CompletedTask;
        }

        if (state.Current.MasterTargets.Count > 1)
        {
            CancelProbeInfoRefresh();
            state.SetProbeInfo(new ProbeInfoResultDto(
                $"Sammelwurf fuer {state.Current.MasterTargets.Count} Spieler vorbereitet.",
                "Detailinfos und Zauberoptionen sind in der Meisteransicht nur bei Einzelauswahl verfuegbar.",
                [],
                null));
            return Task.CompletedTask;
        }

        var request = new ProbeInfoRequestDto(
            sessionState.ActiveSessionId,
            ResolveCurrentHeroId(),
            state.Current.SelectedProbeValue,
            state.Current.Modifier,
            state.Current.SelectedBadTraitName,
            state.Current.SelectedSpellOptionValues.ToArray());
        var cancellationToken = ResetProbeInfoRefreshCancellation().Token;
        var refreshVersion = Interlocked.Increment(ref _probeInfoRefreshVersion);

        return RefreshProbeInfoAsync(request, refreshVersion, cancellationToken);
    }

    private async Task RefreshProbeInfoAsync(
        ProbeInfoRequestDto request,
        int refreshVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await apiClient.GetProbeInfoAsync(request, cancellationToken);
            if (ShouldIgnoreProbeInfoResult(refreshVersion, cancellationToken))
            {
                return;
            }

            state.SetProbeInfo(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (ShouldIgnoreProbeInfoResult(refreshVersion, cancellationToken))
            {
                return;
            }

            state.ClearProbeInfo();
            state.SetError(exception.Message);
        }
    }

    private CancellationTokenSource ResetProbeInfoRefreshCancellation()
    {
        CancelProbeInfoRefresh();
        _probeInfoRefreshCancellation = new CancellationTokenSource();
        return _probeInfoRefreshCancellation;
    }

    private void CancelProbeInfoRefresh()
    {
        ++_probeInfoRefreshVersion;
        _probeInfoRefreshCancellation?.Cancel();
        _probeInfoRefreshCancellation?.Dispose();
        _probeInfoRefreshCancellation = null;
    }

    private bool ShouldIgnoreProbeInfoResult(int refreshVersion, CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested ||
               refreshVersion != _probeInfoRefreshVersion;
    }

    private Guid? ResolveCurrentHeroId()
    {
        if (state.Current.MasterTargets.Count == 1)
        {
            return state.Current.MasterTargets[0].ActiveHeroId;
        }

        return activeHeroState.CurrentHero?.Id ?? state.Current.ActiveHeroId;
    }

    private async Task<LoadedDicePageContext> LoadMasterContextAsync(IReadOnlyList<SessionPlayerDto> masterTargets, string? sessionId, CancellationToken cancellationToken)
    {
        var catalogContextTask = apiClient.GetCatalogContextAsync(cancellationToken);
        var targetContextsTask = Task.WhenAll(masterTargets.Select(async target => new TargetDicePageContext(
            target,
            await apiClient.GetContextAsync(target.ActiveHeroId, sessionId, cancellationToken))));
        await Task.WhenAll(catalogContextTask, targetContextsTask);

        var catalogContext = catalogContextTask.Result;
        var targetContexts = targetContextsTask.Result;

        if (targetContexts.Length == 1)
        {
            var targetContext = targetContexts[0];
            var context = targetContext.Context;
            var target = targetContext.Target;
            var heroName = string.IsNullOrWhiteSpace(target.ActiveHeroName)
                ? context.ActiveHeroName
                : target.ActiveHeroName;

            return new LoadedDicePageContext(
                new DicePageContextDto(
                    context.ActiveHeroId,
                    heroName,
                    context.Attributes,
                    catalogContext.AvailableProbes,
                    context.BadTraits,
                    $"Talente und Zauber fuer {target.Name} durchsuchen...",
                    context.ShowDebugForcedRolls || catalogContext.ShowDebugForcedRolls),
                BuildBadTraitOwners([targetContext]));
        }

        var badTraitOwners = BuildBadTraitOwners(targetContexts);
        return new LoadedDicePageContext(
            new DicePageContextDto(
                null,
                $"{masterTargets.Count} Spieler",
                BuildAverageAttributes(targetContexts.Select(entry => entry.Context).ToArray()),
                catalogContext.AvailableProbes,
                BuildAggregatedBadTraits(badTraitOwners),
                $"Talente und Zauber fuer {masterTargets.Count} Spieler durchsuchen...",
                targetContexts.Any(entry => entry.Context.ShowDebugForcedRolls) || catalogContext.ShowDebugForcedRolls),
            badTraitOwners);
    }

    private static AttributeValueDto[] BuildAverageAttributes(IReadOnlyList<DicePageContextDto> contexts)
    {
        return HeroAttributeCatalog.Order
            .Select(attribute => new AttributeValueDto(
                attribute,
                (int)Math.Round(contexts.Average(context =>
                    context.Attributes.FirstOrDefault(current => string.Equals(current.Name, attribute, StringComparison.Ordinal))
                        ?.Value ?? HeroAttributeCatalog.DefaultValues.GetValueOrDefault(attribute)))))
            .ToArray();
    }

    private static Dictionary<string, IReadOnlyList<BadTraitOwnerInfo>> BuildBadTraitOwners(
        IReadOnlyList<TargetDicePageContext> targetContexts)
    {
        var badTraitOwners = new Dictionary<string, List<BadTraitOwnerInfo>>(StringComparer.Ordinal);

        foreach (var targetContext in targetContexts)
        {
            var heroName = string.IsNullOrWhiteSpace(targetContext.Target.ActiveHeroName)
                ? targetContext.Context.ActiveHeroName
                : targetContext.Target.ActiveHeroName;

            foreach (var badTrait in targetContext.Context.BadTraits)
            {
                if (!badTraitOwners.TryGetValue(badTrait.Name, out var owners))
                {
                    owners = [];
                    badTraitOwners[badTrait.Name] = owners;
                }

                owners.Add(new BadTraitOwnerInfo(
                    targetContext.Target.Name,
                    heroName,
                    badTrait.Value,
                    badTrait.TalentModifier,
                    badTrait.AttributeModifier));
            }
        }

        return badTraitOwners.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<BadTraitOwnerInfo>)entry.Value
                .OrderBy(owner => owner.PlayerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(owner => owner.HeroName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.Ordinal);
    }

    private static BadTraitDto[] BuildAggregatedBadTraits(
        IReadOnlyDictionary<string, IReadOnlyList<BadTraitOwnerInfo>> badTraitOwners)
    {
        return badTraitOwners
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry =>
            {
                var strongestOwner = entry.Value
                    .OrderByDescending(owner => owner.Value)
                    .ThenBy(owner => owner.PlayerName, StringComparer.OrdinalIgnoreCase)
                    .First();

                return new BadTraitDto(
                    entry.Key,
                    strongestOwner.Value,
                    strongestOwner.TalentModifier,
                    strongestOwner.AttributeModifier);
            })
            .ToArray();
    }
}

internal sealed record LoadedDicePageContext(
    DicePageContextDto Context,
    IReadOnlyDictionary<string, IReadOnlyList<BadTraitOwnerInfo>> BadTraitOwners);

internal sealed record TargetDicePageContext(
    SessionPlayerDto Target,
    DicePageContextDto Context);
