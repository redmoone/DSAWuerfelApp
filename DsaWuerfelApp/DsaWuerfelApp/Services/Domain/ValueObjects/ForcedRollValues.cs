using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class ForcedRollValues
{
    private readonly int[] _values;

    private ForcedRollValues(int[] values)
    {
        _values = values;
    }

    public int Count => _values.Length;

    public int SingleValue => _values.Length == 1
        ? _values[0]
        : throw new InvalidOperationException("Es wurde genau ein erzwungener Wurf erwartet.");

    public static ForcedRollValues? CreateOptional(string? value, int expectedCount)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var values = value
            .Split([',', ';', '/', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseValue)
            .ToArray();

        if (values.Length != expectedCount)
        {
            throw new ArgumentException(expectedCount == 1
                ? "Für die direkte Probe auf eine schlechte Eigenschaft ist genau 1 Testwurf nötig."
                : $"Testwürfe müssen genau {expectedCount} Werte enthalten.");
        }

        return new ForcedRollValues(values);
    }

    public DiceRollDto[] ToDiceRolls(int sides)
    {
        return _values
            .Select(value => new DiceRollDto(sides, value))
            .ToArray();
    }

    public int[] ToArray()
    {
        return [.. _values];
    }

    private static int ParseValue(string value)
    {
        if (!int.TryParse(value, out var parsedValue) || parsedValue is < 1 or > 20)
        {
            throw new ArgumentException("Testwürfe müssen Zahlen von 1 bis 20 sein.");
        }

        return parsedValue;
    }
}