using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class AttributeSelection
{
    private readonly string[] _values;

    private AttributeSelection(string[] values)
    {
        _values = values;
    }

    public int Count => _values.Length;

    public string Label => string.Join('/', _values);

    public static AttributeSelection Create(IEnumerable<string> values)
    {
        var attributes = values
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute))
            .Select(attribute => attribute.Trim().ToUpperInvariant())
            .ToArray();

        if (attributes.Length == 0 || attributes.Length > 3)
        {
            throw new InvalidOperationException("Es muessen zwischen 1 und 3 Eigenschaften ausgewaehlt werden.");
        }

        return new AttributeSelection(attributes);
    }

    public int[] ResolveValues(IReadOnlyDictionary<string, int>? attributeValues)
    {
        return _values
            .Select(attribute =>
            {
                if (attributeValues is not null && attributeValues.TryGetValue(attribute, out var value))
                {
                    return value;
                }

                return HeroAttributeCatalog.TryGetDefaultValue(attribute, out var fallbackValue)
                    ? fallbackValue
                    : throw new InvalidOperationException($"Die Eigenschaft '{attribute}' ist nicht bekannt.");
            })
            .ToArray();
    }

    public string[] ToArray()
    {
        return [.. _values];
    }
}