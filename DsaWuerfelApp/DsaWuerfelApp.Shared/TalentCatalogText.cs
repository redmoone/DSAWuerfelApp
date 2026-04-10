using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DsaWuerfelApp.Shared;

public static partial class TalentCatalogText
{
    public static string CanonicalizeName(string? value)
    {
        return CanonicalizeText(RemoveSpecializationSuffix(value));
    }

    public static string CanonicalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizeCatalogText(value);
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

    public static bool TryFindBestNameMatch<TEntry>(
        IReadOnlyDictionary<string, TEntry> entries,
        string? lookupName,
        out string matchedName,
        out TEntry entry)
    {
        if (TryFindBestNameMatch(entries, static existingEntry => existingEntry.Key, lookupName, out var matchedEntry))
        {
            matchedName = matchedEntry.Key;
            entry = matchedEntry.Value;
            return true;
        }

        matchedName = string.Empty;
        entry = default!;
        return false;
    }

    public static bool TryFindBestNameMatch<TEntry>(
        IEnumerable<TEntry> entries,
        Func<TEntry, string?> nameSelector,
        string? lookupName,
        out TEntry entry)
    {
        var lookupVariants = BuildNameMatchVariants(RemoveSpecializationSuffix(lookupName));
        if (lookupVariants.Count == 0)
        {
            entry = default!;
            return false;
        }

        var normalizedLookupName = NormalizeCatalogText(RemoveSpecializationSuffix(lookupName));
        var bestScore = 0;
        var hasAmbiguousBestMatch = false;
        var bestEntry = default(TEntry);

        foreach (var existingEntry in entries)
        {
            var candidateName = nameSelector(existingEntry);
            var score = CalculateNameMatchScore(normalizedLookupName, lookupVariants, candidateName);
            if (score <= 0)
            {
                continue;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestEntry = existingEntry;
                hasAmbiguousBestMatch = false;
                continue;
            }

            if (score == bestScore)
            {
                hasAmbiguousBestMatch = true;
            }
        }

        if (bestScore > 0 && !hasAmbiguousBestMatch)
        {
            entry = bestEntry!;
            return true;
        }

        entry = default!;
        return false;
    }

    public static string NormalizeCatalogText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim()
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¤", "ÃƒÆ’Ã‚Â¤", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾", "ÃƒÆ’Ã¢â‚¬Å¾", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¶", "ÃƒÆ’Ã‚Â¶", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“", "ÃƒÆ’Ã¢â‚¬â€œ", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¼", "ÃƒÆ’Ã‚Â¼", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€¦Ã¢â‚¬Å“", "ÃƒÆ’Ã…â€œ", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€¦Ã‚Â¸", "ÃƒÆ’Ã…Â¸", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©", "ÃƒÆ’Ã‚Â©", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¨", "ÃƒÆ’Ã‚Â¨", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡", "ÃƒÆ’Ã‚Â¡", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³", "ÃƒÆ’Ã‚Â³", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¤", "Ã¤", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã¢â‚¬Å¾", "Ã„", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¶", "Ã¶", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã¢â‚¬â€œ", "Ã–", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¼", "Ã¼", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã…â€œ", "Ãœ", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã…Â¸", "ÃŸ", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â©", "Ã©", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¨", "Ã¨", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¡", "Ã¡", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â³", "Ã³", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ", "-", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â", "-", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¾", "\"", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã¢â‚¬Å“", "\"", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢", "'", StringComparison.Ordinal)
            .Replace("ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“", "-", StringComparison.Ordinal)
            .Replace("ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â", "-", StringComparison.Ordinal)
            .Replace("ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¾", "\"", StringComparison.Ordinal)
            .Replace("ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ", "\"", StringComparison.Ordinal)
            .Replace("ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢", "'", StringComparison.Ordinal);

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
        return SplitCommaSeparatedValues(value)
            .Select(RemoveSpecializationSuffix)
            .Select(NormalizeCatalogText)
            .Where(alternative => !string.IsNullOrWhiteSpace(alternative))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static string[] ParseSpecializations(string? value)
    {
        return SplitCommaSeparatedValues(value)
            .Select(NormalizeCatalogText)
            .Where(specialization => !string.IsNullOrWhiteSpace(specialization))
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
        return value.Contains("\uFFFD", StringComparison.Ordinal) || value.Contains("Ã¯Â¿Â½", StringComparison.Ordinal);
    }

    private static int CalculateNameMatchScore(
        string normalizedLookupName,
        IReadOnlyList<NameMatchVariant> lookupVariants,
        string? candidateName)
    {
        var normalizedCandidateName = NormalizeCatalogText(RemoveSpecializationSuffix(candidateName));
        if (string.IsNullOrWhiteSpace(normalizedCandidateName))
        {
            return 0;
        }

        if (string.Equals(normalizedLookupName, normalizedCandidateName, StringComparison.Ordinal))
        {
            return 400_000 + normalizedCandidateName.Length;
        }

        var candidateVariants = BuildNameMatchVariants(normalizedCandidateName);
        if (candidateVariants.Count == 0)
        {
            return 0;
        }

        var bestScore = 0;

        foreach (var lookupVariant in lookupVariants)
        {
            foreach (var candidateVariant in candidateVariants)
            {
                if (!string.Equals(lookupVariant.Canonical, candidateVariant.Canonical, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!lookupVariant.IsFull && !candidateVariant.IsFull)
                {
                    continue;
                }

                var matchTier = lookupVariant.IsFull == candidateVariant.IsFull
                    ? lookupVariant.IsFull
                        ? 300_000
                        : 0
                    : 200_000;
                var specificity = (lookupVariant.TokenCount * 100) + lookupVariant.Canonical.Length;
                var score = matchTier + specificity;
                if (score > bestScore)
                {
                    bestScore = score;
                }
            }
        }

        return bestScore;
    }

    private static IReadOnlyList<NameMatchVariant> BuildNameMatchVariants(string? value)
    {
        var tokens = TokenizeName(value);
        if (tokens.Count == 0)
        {
            return [];
        }

        var variants = new List<NameMatchVariant>(tokens.Count);
        var seenCanonicals = new HashSet<string>(StringComparer.Ordinal);

        for (var tokenCount = tokens.Count; tokenCount >= 1; tokenCount--)
        {
            var canonical = string.Concat(tokens.Take(tokenCount));
            if (string.IsNullOrWhiteSpace(canonical) || !seenCanonicals.Add(canonical))
            {
                continue;
            }

            variants.Add(new NameMatchVariant(canonical, tokenCount == tokens.Count, tokenCount));
        }

        return variants;
    }

    private static IReadOnlyList<string> TokenizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var normalized = NormalizeCatalogText(value);
        normalized = WordBoundaryUndPattern().Replace(normalized, "/");

        var decomposed = normalized.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var tokens = new List<string>();

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (builder.Length == 0)
            {
                continue;
            }

            tokens.Add(builder.ToString());
            builder.Clear();
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return tokens;
    }

    private static IEnumerable<string> SplitCommaSeparatedValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var builder = new StringBuilder(value.Length);
        var parenthesisDepth = 0;

        foreach (var character in value)
        {
            switch (character)
            {
                case '(':
                    parenthesisDepth++;
                    break;
                case ')' when parenthesisDepth > 0:
                    parenthesisDepth--;
                    break;
                case ',' when parenthesisDepth == 0:
                    var entry = builder.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(entry))
                    {
                        yield return entry;
                    }

                    builder.Clear();
                    continue;
            }

            builder.Append(character);
        }

        var trailingEntry = builder.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(trailingEntry))
        {
            yield return trailingEntry;
        }
    }

    [GeneratedRegex(@"\bund\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WordBoundaryUndPattern();

    private readonly record struct NameMatchVariant(string Canonical, bool IsFull, int TokenCount);
}
