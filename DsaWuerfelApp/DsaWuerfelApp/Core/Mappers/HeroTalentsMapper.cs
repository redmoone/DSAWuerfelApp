using DsaWuerfelApp.Core.Dtos;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Core.Mappers;

public sealed class HeroTalentsMapper
{
    private const string RitualKnowledgeLabel = "Ritualkenntnis";

    public Dictionary<string, TalentData> Map(IEnumerable<TalentDto> talents)
    {
        ArgumentNullException.ThrowIfNull(talents);

        return talents.ToDictionary(
            BuildTalentName,
            talent => new TalentData
            {
                Wert = talent.Wert,
                Probe = TalentCatalogText.NormalizeAttributeProbe(talent.Probe),
                Specializations = TalentCatalogText.ParseSpecializations(talent.Specializations)
            });
    }

    private static string BuildTalentName(TalentDto talent)
    {
        var normalizedName = TalentCatalogText.NormalizeCatalogText(talent.Name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return string.Empty;
        }

        var isRitualKnowledge = talent.Bereiche.Any(area =>
            string.Equals(
                TalentCatalogText.CanonicalizeText(area),
                TalentCatalogText.CanonicalizeText(RitualKnowledgeLabel),
                StringComparison.Ordinal));

        if (!isRitualKnowledge ||
            normalizedName.StartsWith($"{RitualKnowledgeLabel}:", StringComparison.Ordinal))
        {
            return normalizedName;
        }

        return $"{RitualKnowledgeLabel}: {normalizedName}";
    }
}
