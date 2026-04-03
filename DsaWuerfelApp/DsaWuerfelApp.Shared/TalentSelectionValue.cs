namespace DsaWuerfelApp.Shared;

public static class TalentSelectionValue
{
    private const string SpecializationDelimiter = "|||";

    public static string Encode(string talentName, string specializationName)
    {
        var normalizedTalentName = TalentCatalogText.NormalizeCatalogText(talentName);
        var normalizedSpecializationName = TalentCatalogText.NormalizeCatalogText(specializationName);

        return string.IsNullOrWhiteSpace(normalizedTalentName) ||
               string.IsNullOrWhiteSpace(normalizedSpecializationName)
            ? normalizedTalentName
            : $"{normalizedTalentName}{SpecializationDelimiter}{normalizedSpecializationName}";
    }

    public static ParsedTalentSelection Parse(string? value)
    {
        var normalizedValue = TalentCatalogText.NormalizeCatalogText(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return new ParsedTalentSelection(string.Empty, null);
        }

        var separatorIndex = normalizedValue.IndexOf(SpecializationDelimiter, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return new ParsedTalentSelection(normalizedValue, null);
        }

        var talentName = normalizedValue[..separatorIndex].Trim();
        var specializationName = normalizedValue[(separatorIndex + SpecializationDelimiter.Length)..].Trim();

        return string.IsNullOrWhiteSpace(talentName) || string.IsNullOrWhiteSpace(specializationName)
            ? new ParsedTalentSelection(normalizedValue, null)
            : new ParsedTalentSelection(talentName, specializationName);
    }

    public static string FormatLabel(string talentName, string specializationName)
    {
        var normalizedTalentName = TalentCatalogText.NormalizeCatalogText(talentName);
        var normalizedSpecializationName = TalentCatalogText.NormalizeCatalogText(specializationName);

        return string.IsNullOrWhiteSpace(normalizedSpecializationName)
            ? normalizedTalentName
            : $"{normalizedTalentName} ({normalizedSpecializationName})";
    }
}

public readonly record struct ParsedTalentSelection(string TalentName, string? SpecializationName)
{
    public bool HasSpecialization => !string.IsNullOrWhiteSpace(SpecializationName);

    public int SpecializationModifier => HasSpecialization ? -2 : 0;

    public string DisplayName =>
        HasSpecialization
            ? TalentSelectionValue.FormatLabel(TalentName, SpecializationName!)
            : TalentName;
}