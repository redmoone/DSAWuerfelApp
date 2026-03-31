using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DsaWuerfelApp.Shared;

public static partial class TalentCatalogText
{
    public static string CanonicalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizeCatalogText(RemoveSpecializationSuffix(value));
        normalized = WordBoundaryUndPattern().Replace(normalized, "/");

        var decomposed = normalized.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    public static string NormalizeCatalogText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim()
            .Replace("ÃƒÆ’Ã‚Â¤", "ÃƒÂ¤", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã¢â‚¬Å¾", "Ãƒâ€ž", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¶", "ÃƒÂ¶", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã¢â‚¬â€œ", "Ãƒâ€“", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¼", "ÃƒÂ¼", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã…â€œ", "ÃƒÅ“", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã…Â¸", "ÃƒÅ¸", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â©", "ÃƒÂ©", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¨", "ÃƒÂ¨", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¡", "ÃƒÂ¡", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â³", "ÃƒÂ³", StringComparison.Ordinal)
            .Replace("ÃƒÂ¤", "ä", StringComparison.Ordinal)
            .Replace("Ãƒâ€ž", "Ä", StringComparison.Ordinal)
            .Replace("ÃƒÂ¶", "ö", StringComparison.Ordinal)
            .Replace("Ãƒâ€“", "Ö", StringComparison.Ordinal)
            .Replace("ÃƒÂ¼", "ü", StringComparison.Ordinal)
            .Replace("ÃƒÅ“", "Ü", StringComparison.Ordinal)
            .Replace("ÃƒÅ¸", "ß", StringComparison.Ordinal)
            .Replace("ÃƒÂ©", "é", StringComparison.Ordinal)
            .Replace("ÃƒÂ¨", "è", StringComparison.Ordinal)
            .Replace("ÃƒÂ¡", "á", StringComparison.Ordinal)
            .Replace("ÃƒÂ³", "ó", StringComparison.Ordinal)
            .Replace("ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“", "-", StringComparison.Ordinal)
            .Replace("ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â", "-", StringComparison.Ordinal)
            .Replace("ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾", "\"", StringComparison.Ordinal)
            .Replace("ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ", "\"", StringComparison.Ordinal)
            .Replace("ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢", "'", StringComparison.Ordinal)
            .Replace("Ã¢â‚¬â€œ", "-", StringComparison.Ordinal)
            .Replace("Ã¢â‚¬â€", "-", StringComparison.Ordinal)
            .Replace("Ã¢â‚¬Å¾", "\"", StringComparison.Ordinal)
            .Replace("Ã¢â‚¬Å“", "\"", StringComparison.Ordinal)
            .Replace("Ã¢â‚¬â„¢", "'", StringComparison.Ordinal);

        if (ContainsReplacementCharacter(normalized))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(normalized)).Trim();
                if (!ContainsReplacementCharacter(decoded))
                {
                    normalized = decoded;
                }
            }
            catch
            {
            }
        }

        return normalized;
    }

    public static string RemoveSpecializationSuffix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizeCatalogText(value);
        var parenthesisIndex = normalized.IndexOf('(');

        return parenthesisIndex >= 0
            ? normalized[..parenthesisIndex].Trim()
            : normalized;
    }

    public static string NormalizeProbe(string? value)
    {
        return NormalizeCatalogText(value)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    public static string[] ParseAlternatives(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(RemoveSpecializationSuffix)
            .Select(NormalizeCatalogText)
            .Where(alternative => !string.IsNullOrWhiteSpace(alternative))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static string NormalizeAttributeProbe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join('/',
            value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.ToUpperInvariant()));
    }

    private static bool ContainsReplacementCharacter(string value)
    {
        return value.Contains("\uFFFD", StringComparison.Ordinal) || value.Contains("ï¿½", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\bund\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WordBoundaryUndPattern();
}