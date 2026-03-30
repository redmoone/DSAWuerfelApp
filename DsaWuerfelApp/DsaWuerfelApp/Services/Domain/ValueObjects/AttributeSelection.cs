namespace DsaWuerfelApp.Services;

public sealed class AttributeSelection
{
    private static readonly IReadOnlyDictionary<string, int> DefaultValues = new Dictionary<string, int>
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

                return DefaultValues.TryGetValue(attribute, out var fallbackValue)
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