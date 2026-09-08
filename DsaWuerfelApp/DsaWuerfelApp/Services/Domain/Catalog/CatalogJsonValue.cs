using System.Globalization;
using System.Text;
using System.Text.Json;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

internal static class CatalogJsonValue
{
    public static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var expectedName = CanonicalizePropertyName(propertyName);
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(
                        CanonicalizePropertyName(property.Name),
                        expectedName,
                        StringComparison.Ordinal))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public static string ReadText(JsonElement parent, string propertyName)
    {
        return TryGetProperty(parent, propertyName, out var value)
            ? ReadText(value)
            : string.Empty;
    }

    public static string ReadText(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => NormalizeDisplayText(value.GetString()),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "ja",
            JsonValueKind.False => "nein",
            JsonValueKind.Array or JsonValueKind.Object => FormatValue(value),
            _ => string.Empty
        };
    }

    public static int? ReadInt(JsonElement parent, string propertyName)
    {
        return TryGetProperty(parent, propertyName, out var value) ? ReadInt(value) : null;
    }

    public static int? ReadInt(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    public static bool? ReadBool(JsonElement parent, string propertyName)
    {
        if (!TryGetProperty(parent, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    public static IReadOnlyList<JsonElement> ReadArray(JsonElement parent, string propertyName)
    {
        return TryGetProperty(parent, propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : [];
    }

    public static string FormatValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => NormalizeDisplayText(value.GetString()),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "ja",
            JsonValueKind.False => "nein",
            JsonValueKind.Array => string.Join(", ", value.EnumerateArray()
                .Select(FormatValue)
                .Where(text => !string.IsNullOrWhiteSpace(text))),
            JsonValueKind.Object => string.Join(
                ", ",
                value.EnumerateObject()
                    .Select(property =>
                        $"{NormalizeDisplayText(property.Name)}: {FormatValue(property.Value)}")
                    .Where(text => !string.IsNullOrWhiteSpace(text))),
            _ => string.Empty
        };
    }

    public static string NormalizeDisplayText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = DecodeMojibake(value.Trim());
        normalized = TalentCatalogText.NormalizeCatalogText(normalized);
        return DecodeMojibake(normalized).Trim();
    }

    public static string CanonicalizePropertyName(string value)
    {
        return TalentCatalogText.CanonicalizeText(DecodeMojibake(value));
    }

    private static string DecodeMojibake(string value)
    {
        var current = value;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            string candidate;
            try
            {
                candidate = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(current));
            }
            catch (EncoderFallbackException)
            {
                break;
            }

            if (MojibakeScore(candidate) >= MojibakeScore(current))
            {
                break;
            }

            current = candidate;
        }

        return current;
    }

    private static int MojibakeScore(string value)
    {
        return value.Count(character => character is 'Ã' or 'Â' or 'â' or 'ð' or '\uFFFD');
    }
}
