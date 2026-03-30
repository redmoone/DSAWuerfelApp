using System.Diagnostics;

using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

using Microsoft.EntityFrameworkCore;

namespace DsaWuerfelApp.Services;

public sealed class DiceWorkflowService(
    HeroDbContext dbContext,
    DiceService diceService,
    TalentProbeService talentProbeService,
    AttributeProbeService attributeProbeService,
    SchlechteEigenschaftProbeService schlechteEigenschaftProbeService,
    TalentCatalogService talentCatalogService)
{
    private static readonly IReadOnlyDictionary<string, int> DefaultAttributeValues = new Dictionary<string, int>
    {
        ["MU"] = 14,
        ["KL"] = 13,
        ["IN"] = 15,
        ["CH"] = 12,
        ["FF"] = 15,
        ["GE"] = 15,
        ["KO"] = 14,
        ["KK"] = 13
    };

    public async Task<DicePageContextDto> GetContextAsync(Guid? heroId)
    {
        var hero = await LoadHeroAsync(heroId);
        return talentCatalogService.BuildContext(hero, Debugger.IsAttached);
    }

    public async Task<ProbeInfoResultDto> GetProbeInfoAsync(ProbeInfoRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ProbeValue))
        {
            throw new ArgumentException("Bitte zuerst eine Probe auswaehlen.", nameof(request));
        }

        var hero = await LoadHeroAsync(request.HeroId);
        return talentCatalogService.BuildProbeInfo(hero, request.ProbeValue, request.Modifier, request.BadTraitName);
    }

    public FreeRollResultDto RollFree(FreeRollRequestDto request, string playerName = "Unbekannt")
    {
        ArgumentNullException.ThrowIfNull(request);

        DiceService.ValidateModifier(request.Modifier);

        var timestamp = DateTime.UtcNow;
        var rolls = diceService.RollDice(request.Dice);
        var equation = DiceResultFactory.CreateEquation(rolls, request.Modifier);
        var historyEntry = DiceResultFactory.CreateHistoryEntry(playerName, timestamp, equation);

        return new FreeRollResultDto(playerName, timestamp, equation, historyEntry);
    }

    public async Task<TalentRollResultDto> RollTalentAsync(
        TalentRollRequestDto request,
        string playerName = "Unbekannt")
    {
        ArgumentNullException.ThrowIfNull(request);

        var hero = await GetRequiredHeroAsync(request.HeroId);
        var talent = talentCatalogService.ResolveTalent(hero, request.TalentKey);
        var probeAttributes = ParseProbeAttributes(talent.Talent.Probe);
        var attributeValues = probeAttributes.Select(attribute => GetAttributeValue(hero, attribute)).ToArray();
        var badTrait = ResolveBadTrait(hero, request.BadTraitName);

        return talentProbeService.RollTalentProbe(
            new ResolvedTalentRollRequest(
                talent.Name,
                talent.Talent.Wert,
                string.Join('/', probeAttributes),
                attributeValues,
                request.Modifier,
                badTrait?.Name,
                badTrait?.TalentModifier ?? 0,
                request.ForcedRollsText),
            playerName);
    }

    public async Task<AttributeRollResultDto> RollAttributeAsync(
        AttributeRollRequestDto request,
        string playerName = "Unbekannt")
    {
        ArgumentNullException.ThrowIfNull(request);

        var hero = await LoadHeroAsync(request.HeroId);
        var attributes = NormalizeAttributes(request.Attributes);
        var attributeValues = attributes.Select(attribute => GetAttributeValue(hero, attribute)).ToArray();
        var badTrait = hero is null ? null : ResolveBadTrait(hero, request.BadTraitName);

        return attributeProbeService.RollAttributeProbe(
            new ResolvedAttributeRollRequest(
                attributes,
                attributeValues,
                request.Modifier,
                badTrait?.Name,
                badTrait?.AttributeModifier ?? 0),
            playerName);
    }

    public async Task<BadTraitRollResultDto> RollBadTraitAsync(
        BadTraitRollRequestDto request,
        string playerName = "Unbekannt")
    {
        ArgumentNullException.ThrowIfNull(request);

        var hero = await GetRequiredHeroAsync(request.HeroId);
        var badTrait = ResolveRequiredBadTrait(hero, request.BadTraitName);

        return schlechteEigenschaftProbeService.RollProbe(
            new ResolvedBadTraitRollRequest(
                badTrait.Name,
                badTrait.Value,
                request.ForcedRollsText),
            playerName);
    }

    private async Task<Hero?> LoadHeroAsync(Guid? heroId)
    {
        if (heroId.HasValue)
        {
            return await dbContext.Heroes
                .AsNoTracking()
                .FirstOrDefaultAsync(hero => hero.Id == heroId.Value);
        }

        return await dbContext.Heroes
            .AsNoTracking()
            .FirstOrDefaultAsync(hero => hero.IsActive);
    }

    private async Task<Hero> GetRequiredHeroAsync(Guid heroId)
    {
        var hero = await LoadHeroAsync(heroId);
        return hero ?? throw new InvalidOperationException("Der ausgewaehlte Held konnte nicht geladen werden.");
    }

    private static int GetAttributeValue(Hero? hero, string attribute)
    {
        if (hero is not null)
        {
            return hero.Eigenschaften.TryGetValue(attribute, out var value)
                ? value
                : throw new InvalidOperationException(
                    $"Die Eigenschaft '{attribute}' ist fuer den Held nicht vorhanden.");
        }

        return DefaultAttributeValues.TryGetValue(attribute, out var fallbackValue)
            ? fallbackValue
            : throw new InvalidOperationException($"Die Eigenschaft '{attribute}' ist nicht bekannt.");
    }

    private BadTraitDto? ResolveBadTrait(Hero hero, string? badTraitName)
    {
        if (string.IsNullOrWhiteSpace(badTraitName))
        {
            return null;
        }

        return talentCatalogService.ResolveBadTrait(hero, badTraitName) ??
               throw new InvalidOperationException(
                   "Die ausgewaehlte schlechte Eigenschaft ist fuer den Held nicht vorhanden.");
    }

    private static BadTraitDto ResolveRequiredBadTrait(Hero hero, string? badTraitName)
    {
        if (string.IsNullOrWhiteSpace(badTraitName))
        {
            throw new InvalidOperationException("Bitte zuerst eine relevante schlechte Eigenschaft waehlen.");
        }

        return hero.SchlechteEigenschaften.TryGetValue(badTraitName, out var value)
            ? new BadTraitDto(badTraitName, value, value, value <= 0 ? 0 : (value + 1) / 2)
            : throw new InvalidOperationException(
                "Die ausgewaehlte schlechte Eigenschaft ist fuer den Held nicht vorhanden.");
    }

    private static string[] NormalizeAttributes(IEnumerable<string> attributes)
    {
        var normalizedAttributes = attributes
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute))
            .Select(attribute => attribute.Trim().ToUpperInvariant())
            .ToArray();

        if (normalizedAttributes.Length == 0 || normalizedAttributes.Length > 3)
        {
            throw new InvalidOperationException("Es muessen zwischen 1 und 3 Eigenschaften ausgewaehlt werden.");
        }

        return normalizedAttributes;
    }

    private static string[] ParseProbeAttributes(string? probe)
    {
        if (string.IsNullOrWhiteSpace(probe))
        {
            throw new InvalidOperationException(
                "Fuer die ausgewaehlte Probe ist keine gueltige Eigenschaftskombination hinterlegt.");
        }

        var attributes = probe.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(attribute => attribute.ToUpperInvariant())
            .ToArray();

        return attributes.Length == 3
            ? attributes
            : throw new InvalidOperationException(
                "Fuer die ausgewaehlte Probe ist keine vollstaendige Talentprobe hinterlegt.");
    }
}