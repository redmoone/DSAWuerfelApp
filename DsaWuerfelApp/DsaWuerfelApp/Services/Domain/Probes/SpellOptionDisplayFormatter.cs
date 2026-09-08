namespace DsaWuerfelApp.Services;

internal static class SpellOptionDisplayFormatter
{
    public static string RemoveSubcaseLine(string? displayText)
    {
        if (string.IsNullOrWhiteSpace(displayText))
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            displayText
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.StartsWith("Unterfall:", StringComparison.OrdinalIgnoreCase)));
    }

    public static string GetManualNote(SpellOptionEntry option)
    {
        return !option.IsSelectionEnabled
            ? "Manuelle Pruefung erforderlich."
            : option.RequiresManualCalculation
                ? "Manuell ergaenzen."
                : string.Empty;
    }
}
