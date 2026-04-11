using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelContextService(
    WuerfelState state,
    ActiveHeroState activeHeroState,
    IWuerfelApiClient apiClient,
    WuerfelUiOperationRunner operationRunner)
{
    private CancellationTokenSource? _probeInfoRefreshCancellation;
    private int _probeInfoRefreshVersion;

    public Task LoadContextAsync(IReadOnlyList<SessionPlayerDto>? masterTargets = null)
    {
        return operationRunner.RunAsync(async () =>
        {
            var resolvedMasterTargets = (masterTargets ?? state.Current.MasterTargets)
                .Where(target => target.ActiveHeroId.HasValue)
                .ToArray();

            var loadedContext = resolvedMasterTargets.Length > 0
                ? await LoadMasterContextAsync(resolvedMasterTargets)
                : new LoadedDicePageContext(
                    await apiClient.GetContextAsync(activeHeroState.CurrentHero?.Id),
                    new Dictionary<string, IReadOnlyList<BadTraitOwnerInfo>>(StringComparer.Ordinal));

            state.ApplyContext(loadedContext.Context, loadedContext.BadTraitOwners);
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
                $"Sammelwurf für {state.Current.MasterTargets.Count} Spieler vorbereitet.",
                "Detailinfos und Zauberoptionen sind in der Meisteransicht nur bei Einzelauswahl verfügbar.",
                [],
                null));
            return Task.CompletedTask;
        }

        var request = new ProbeInfoRequestDto(
            ResolveCurrentHeroId(),
            state.Current.SelectedProbeValue,
            state.Current.Modifier,
            state.Current.SelectedBadTraitName,
            state.Current.SelectedSpellOptionValues.ToArray());
        var refreshVersion = Interlocked.Increment(ref _probeInfoRefreshVersion);
        var cancellationToken = ResetProbeInfoRefreshCancellation().Token;

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

    private async Task<LoadedDicePageContext> LoadMasterContextAsync(IReadOnlyList<SessionPlayerDto> masterTargets)
    {
        var targetContexts = await Task.WhenAll(masterTargets.Select(async target => new TargetDicePageContext(
            target,
            await apiClient.GetContextAsync(target.ActiveHeroId))));

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
                    context.AvailableProbes,
                    context.BadTraits,
                    $"Proben von {target.Name} durchsuchen...",
                    context.ShowDebugForcedRolls),
                BuildBadTraitOwners([targetContext]));
        }

        var badTraitOwners = BuildBadTraitOwners(targetContexts);
        return new LoadedDicePageContext(
            new DicePageContextDto(
                null,
                $"{masterTargets.Count} Spieler",
                BuildAverageAttributes(targetContexts.Select(entry => entry.Context).ToArray()),
                BuildCommonProbes(targetContexts.Select(entry => entry.Context).ToArray()),
                BuildAggregatedBadTraits(badTraitOwners),
                $"Gemeinsame Proben für {masterTargets.Count} Spieler durchsuchen...",
                targetContexts.Any(entry => entry.Context.ShowDebugForcedRolls)),
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

    private static ProbeSearchEntryDto[] BuildCommonProbes(IReadOnlyList<DicePageContextDto> contexts)
    {
        if (contexts.Count == 0)
        {
            return [];
        }

        var probeMaps = contexts
            .Select(context => context.AvailableProbes
                .Where(entry => entry.IsSelectable && !string.IsNullOrWhiteSpace(entry.Value))
                .GroupBy(entry => entry.Value!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal))
            .ToArray();

        var commonProbeValues = probeMaps[0].Keys
            .Where(value => probeMaps.Skip(1).All(map => map.ContainsKey(value)))
            .OrderBy(value => ProbeSelectionValue.Parse(value).ProbeName, StringComparer.Ordinal)
            .ToArray();

        return commonProbeValues
            .Select(value => BuildCommonProbeEntry(probeMaps[0][value]))
            .ToArray();
    }

    private static ProbeSearchEntryDto BuildCommonProbeEntry(ProbeSearchEntryDto source)
    {
        var parsedSelection = ProbeSelectionValue.Parse(source.Value);
        var displayLabel = parsedSelection.Kind is ProbeSelectionKind.Spell or ProbeSelectionKind.Talent
            ? parsedSelection.ProbeName
            : source.DisplayLabel;

        return new ProbeSearchEntryDto(displayLabel, source.Value, true, []);
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
