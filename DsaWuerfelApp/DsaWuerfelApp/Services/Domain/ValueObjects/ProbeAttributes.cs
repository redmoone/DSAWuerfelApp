namespace DsaWuerfelApp.Services;

public sealed class ProbeAttributes
{
    private readonly string[] _values;

    private ProbeAttributes(string[] values)
    {
        _values = values;
    }

    public int Count => _values.Length;

    public string Label => string.Join('/', _values);

    public static ProbeAttributes Create(string? value)
    {
        var attributes = Split(value);
        if (attributes.Length != 3)
        {
            throw new InvalidOperationException(
                "Fuer die ausgewaehlte Probe ist keine vollstaendige Talentprobe hinterlegt.");
        }

        return new ProbeAttributes(attributes);
    }

    public static ProbeAttributes? TryCreate(string? value)
    {
        var attributes = Split(value);
        return attributes.Length == 3 ? new ProbeAttributes(attributes) : null;
    }

    public int[] ResolveValues(IReadOnlyDictionary<string, int> attributeValues)
    {
        return _values
            .Select(attribute => attributeValues.TryGetValue(attribute, out var value)
                ? value
                : throw new InvalidOperationException(
                    $"Die Eigenschaft '{attribute}' ist fuer den Held nicht vorhanden."))
            .ToArray();
    }

    public string[] ToArray()
    {
        return [.. _values];
    }

    private static string[] Split(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(attribute => attribute.ToUpperInvariant())
            .ToArray();
    }
}