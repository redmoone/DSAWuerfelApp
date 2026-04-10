using System.Globalization;

namespace DsaWuerfelApp.Shared;

public static class ProbeSelectionValue
{
    private const string SegmentDelimiter = "|||";

    public static string EncodeBase(ProbeSelectionKind kind, string probeName)
    {
        var normalizedProbeName = TalentCatalogText.NormalizeCatalogText(probeName);
        return string.IsNullOrWhiteSpace(normalizedProbeName)
            ? string.Empty
            : $"{FormatKind(kind)}{SegmentDelimiter}{normalizedProbeName}";
    }

    public static string EncodeOption(
        ProbeSelectionKind kind,
        string probeName,
        ProbeSelectionOptionKind optionKind,
        string optionName,
        int optionModifier = 0)
    {
        var normalizedProbeName = TalentCatalogText.NormalizeCatalogText(probeName);
        var normalizedOptionName = TalentCatalogText.NormalizeCatalogText(optionName);

        return string.IsNullOrWhiteSpace(normalizedProbeName) ||
               string.IsNullOrWhiteSpace(normalizedOptionName)
            ? EncodeBase(kind, probeName)
            : string.Join(
                SegmentDelimiter,
                FormatKind(kind),
                normalizedProbeName,
                FormatOptionKind(optionKind),
                normalizedOptionName,
                optionModifier.ToString(CultureInfo.InvariantCulture));
    }

    public static ParsedProbeSelection Parse(string? value)
    {
        var normalizedValue = TalentCatalogText.NormalizeCatalogText(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return ParsedProbeSelection.Empty;
        }

        var segments = normalizedValue.Split(SegmentDelimiter, StringSplitOptions.None);
        return segments.Length switch
        {
            1 => new ParsedProbeSelection(ProbeSelectionKind.Unknown, segments[0], null, ProbeSelectionOptionKind.None,
                0),
            2 when !TryParseKind(segments[0], out _) => ParseLegacyTalentSelection(segments),
            _ => ParseStructuredSelection(segments, normalizedValue)
        };
    }

    public static bool TryParseSpellOption(string? value, out ParsedSpellOptionSelection option)
    {
        var parsedSelection = Parse(value);
        if (parsedSelection.Kind == ProbeSelectionKind.Spell &&
            parsedSelection.HasOption &&
            parsedSelection.OptionKind is ProbeSelectionOptionKind.SpellModification
                or ProbeSelectionOptionKind.SpellVariant)
        {
            option = new ParsedSpellOptionSelection(
                parsedSelection.ProbeName,
                parsedSelection.OptionName!,
                parsedSelection.OptionKind,
                parsedSelection.OptionModifier);
            return true;
        }

        option = ParsedSpellOptionSelection.Empty;
        return false;
    }

    public static string FormatSpecializationLabel(string probeName, string specializationName)
    {
        var normalizedProbeName = TalentCatalogText.NormalizeCatalogText(probeName);
        var normalizedSpecializationName = TalentCatalogText.NormalizeCatalogText(specializationName);

        return string.IsNullOrWhiteSpace(normalizedSpecializationName)
            ? normalizedProbeName
            : $"{normalizedProbeName} ({normalizedSpecializationName})";
    }

    public static string FormatOptionLabel(
        string probeName,
        ProbeSelectionOptionKind optionKind,
        string optionName)
    {
        var normalizedProbeName = TalentCatalogText.NormalizeCatalogText(probeName);
        var normalizedOptionName = TalentCatalogText.NormalizeCatalogText(optionName);

        if (string.IsNullOrWhiteSpace(normalizedOptionName))
        {
            return normalizedProbeName;
        }

        return optionKind switch
        {
            ProbeSelectionOptionKind.Specialization => FormatSpecializationLabel(normalizedProbeName,
                normalizedOptionName),
            ProbeSelectionOptionKind.SpellModification => normalizedOptionName,
            ProbeSelectionOptionKind.SpellVariant => normalizedOptionName,
            _ => normalizedProbeName
        };
    }

    public static string FormatSelectionLabel(
        string probeName,
        ProbeSelectionOptionKind optionKind,
        string optionName)
    {
        var normalizedProbeName = TalentCatalogText.NormalizeCatalogText(probeName);
        var normalizedOptionName = TalentCatalogText.NormalizeCatalogText(optionName);

        if (string.IsNullOrWhiteSpace(normalizedOptionName))
        {
            return normalizedProbeName;
        }

        return optionKind switch
        {
            ProbeSelectionOptionKind.Specialization => FormatSpecializationLabel(normalizedProbeName,
                normalizedOptionName),
            ProbeSelectionOptionKind.SpellModification => $"{normalizedProbeName} ({normalizedOptionName})",
            ProbeSelectionOptionKind.SpellVariant => $"{normalizedProbeName} ({normalizedOptionName})",
            _ => normalizedProbeName
        };
    }

    private static ParsedProbeSelection ParseLegacyTalentSelection(string[] segments)
    {
        var probeName = segments[0].Trim();
        var specializationName = segments[1].Trim();

        return string.IsNullOrWhiteSpace(probeName) || string.IsNullOrWhiteSpace(specializationName)
            ? new ParsedProbeSelection(ProbeSelectionKind.Unknown, string.Join(SegmentDelimiter, segments), null,
                ProbeSelectionOptionKind.None, 0)
            : new ParsedProbeSelection(
                ProbeSelectionKind.Talent,
                probeName,
                specializationName,
                ProbeSelectionOptionKind.Specialization,
                -2);
    }

    private static ParsedProbeSelection ParseStructuredSelection(string[] segments, string normalizedValue)
    {
        if (!TryParseKind(segments[0], out var kind))
        {
            return new ParsedProbeSelection(ProbeSelectionKind.Unknown, normalizedValue, null,
                ProbeSelectionOptionKind.None, 0);
        }

        var probeName = segments.ElementAtOrDefault(1)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(probeName))
        {
            return ParsedProbeSelection.Empty;
        }

        if (segments.Length < 4 || !TryParseOptionKind(segments[2], out var optionKind))
        {
            return new ParsedProbeSelection(kind, probeName, null, ProbeSelectionOptionKind.None, 0);
        }

        var optionName = segments[3].Trim();
        if (string.IsNullOrWhiteSpace(optionName))
        {
            return new ParsedProbeSelection(kind, probeName, null, ProbeSelectionOptionKind.None, 0);
        }

        var optionModifier = int.TryParse(
            segments.ElementAtOrDefault(4),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsedModifier)
            ? parsedModifier
            : 0;

        return new ParsedProbeSelection(kind, probeName, optionName, optionKind, optionModifier);
    }

    private static string FormatKind(ProbeSelectionKind kind)
    {
        return kind switch
        {
            ProbeSelectionKind.Talent => "talent",
            ProbeSelectionKind.Spell => "spell",
            _ => "unknown"
        };
    }

    private static string FormatOptionKind(ProbeSelectionOptionKind optionKind)
    {
        return optionKind switch
        {
            ProbeSelectionOptionKind.Specialization => "specialization",
            ProbeSelectionOptionKind.SpellModification => "spell-modification",
            ProbeSelectionOptionKind.SpellVariant => "spell-variant",
            _ => "none"
        };
    }

    private static bool TryParseKind(string? value, out ProbeSelectionKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "talent":
                kind = ProbeSelectionKind.Talent;
                return true;
            case "spell":
                kind = ProbeSelectionKind.Spell;
                return true;
            default:
                kind = ProbeSelectionKind.Unknown;
                return false;
        }
    }

    private static bool TryParseOptionKind(string? value, out ProbeSelectionOptionKind optionKind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "specialization":
                optionKind = ProbeSelectionOptionKind.Specialization;
                return true;
            case "spell-modification":
                optionKind = ProbeSelectionOptionKind.SpellModification;
                return true;
            case "spell-variant":
                optionKind = ProbeSelectionOptionKind.SpellVariant;
                return true;
            default:
                optionKind = ProbeSelectionOptionKind.None;
                return false;
        }
    }
}

public enum ProbeSelectionKind
{
    Unknown,
    Talent,
    Spell
}

public enum ProbeSelectionOptionKind
{
    None,
    Specialization,
    SpellModification,
    SpellVariant
}

public readonly record struct ParsedProbeSelection(
    ProbeSelectionKind Kind,
    string ProbeName,
    string? OptionName,
    ProbeSelectionOptionKind OptionKind,
    int OptionModifier)
{
    public static ParsedProbeSelection Empty { get; } =
        new(ProbeSelectionKind.Unknown, string.Empty, null, ProbeSelectionOptionKind.None, 0);

    public bool HasOption => !string.IsNullOrWhiteSpace(OptionName) && OptionKind != ProbeSelectionOptionKind.None;

    public string DisplayName =>
        HasOption
            ? ProbeSelectionValue.FormatSelectionLabel(ProbeName, OptionKind, OptionName!)
            : ProbeName;
}

public readonly record struct ParsedSpellOptionSelection(
    string ProbeName,
    string OptionName,
    ProbeSelectionOptionKind OptionKind,
    int OptionModifier)
{
    public static ParsedSpellOptionSelection Empty { get; } =
        new(string.Empty, string.Empty, ProbeSelectionOptionKind.None, 0);

    public bool IsEmpty => string.IsNullOrWhiteSpace(ProbeName) || string.IsNullOrWhiteSpace(OptionName) ||
                           OptionKind == ProbeSelectionOptionKind.None;
}