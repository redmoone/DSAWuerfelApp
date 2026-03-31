namespace DsaWuerfelApp.Shared;

public static class DefaultProbeCatalog
{
    private static readonly ProbeSearchEntryDto[] Entries =
    [
        CreateSelectable("Klettern (MU/GE/KK)"),
        CreateSelectable("Körperbeherrschung (GE/GE/KO)"),
        CreateSelectable("Sinnenschärfe (KL/IN/IN)"),
        CreateSelectable("Überreden (MU/IN/CH)"),
        CreateSelectable("Verbergen (MU/IN/GE)")
    ];

    public static ProbeSearchEntryDto[] CreateEntries()
    {
        return [.. Entries];
    }

    private static ProbeSearchEntryDto CreateSelectable(string label)
    {
        return new ProbeSearchEntryDto(label, label, true, []);
    }
}