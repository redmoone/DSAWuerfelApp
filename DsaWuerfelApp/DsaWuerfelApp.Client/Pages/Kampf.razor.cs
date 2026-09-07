namespace DsaWuerfelApp.Client.Pages;

public partial class Kampf
{
    private readonly List<StatusBar> _statusBars = new()
    {
        new("LeP", "Lebensenergie", 31, 38, 82, "health"),
        new("AuP", "Ausdauer", 29, 35, 83, "stamina")
    };

    private readonly List<StatusSummaryItem> _statusSummary = new()
    {
        new("Wunden", "1 / 3", "Schmerz I"),
        new("RS", "4", "Kettenhemd"),
        new("BE", "1", "Leicht behindert")
    };

    private readonly List<StatusEffect> _statusEffects = new()
    {
        new("Armatrutz", "+2 RS, 6 KR", "buff"),
        new("Axxeleratus", "+4 INI, 3 KR", "buff")
    };

    private readonly List<WoundState> _wounds = new()
    {
        new("Wunde I", true),
        new("Wunde II", false),
        new("Wunde III", false)
    };

    private readonly List<WeaponState> _weaponStates = new()
    {
        new("Haupthand", "Langschwert", "1W6+4, WM 0/0"),
        new("Nebenhand", "Holzschild", "PA +2, Schild bereit")
    };

    private readonly List<string> _conditionTags = new()
    {
        "Heiltrank x3",
        "Freie Aktion offen",
        "Keine Schmerzstufe"
    };

    private readonly List<HeroAttribute> _attributes = new()
    {
        new("MU", "Mut", 14, "lion.svg"),
        new("KL", "Klugheit", 13, "owl.svg"),
        new("IN", "Intuition", 15, "eye.svg"),
        new("CH", "Charisma", 12, "mask.svg"),
        new("FF", "Fingerfertigkeit", 15, "hand.svg"),
        new("GE", "Gewandtheit", 15, "cat.svg"),
        new("KO", "Konstitution", 14, "shield.svg"),
        new("KK", "Koerperkraft", 13, "muscle.svg")
    };

    private readonly List<ManeuverCard> _maneuvers = new()
    {
        new(
            "Finte",
            "Angriffsmanoever",
            "+X auf AT",
            "Attacke gegen Parade",
            "Parade des Gegners sinkt",
            "Praeziser Druck auf die gegnerische Abwehr mit bewusst riskanter Fuehrung.",
            new[] { "Praezision", "Druck", "Nahkampf" },
            new[]
            {
                new ManeuverNote("Einsatz", "Gut gegen starke Verteidiger oder Schildkaempfer."),
                new ManeuverNote("Timing", "Vor allem sinnvoll, wenn Parade wichtiger als roher Schaden ist."),
                new ManeuverNote("Kombi", "Laesst sich gut mit hohem AT-Wert oder Initiativevorteil spielen.")
            }),
        new(
            "Wuchtschlag",
            "Angriffsmanoever",
            "+X auf AT",
            "Attacke gegen Parade",
            "Mehr TP bei Treffer",
            "Treffsicherheit wird gegen Wucht getauscht, ideal gegen offene Luecken.",
            new[] { "Schaden", "Kraft", "Ansage" },
            new[]
            {
                new ManeuverNote("Einsatz", "Sinnvoll gegen Ziele mit geringer Parade oder hoher Wundschwelle."),
                new ManeuverNote("Timing", "Vor allem dann gut, wenn du den Treffer halbwegs sicher hast."),
                new ManeuverNote("Kombi", "Profitiert von Situationsboni und vorbereiteter Ueberzahl.")
            }),
        new(
            "Meisterparade",
            "Abwehrmanoever",
            "Erschwerte Parade",
            "Parade",
            "Bessere Folgeposition",
            "Die Verteidigung wird aktiv gesetzt, um danach die Kontrolle zu gewinnen.",
            new[] { "Abwehr", "Tempo", "Reaktion" },
            new[]
            {
                new ManeuverNote("Einsatz", "Defensives Werkzeug gegen Einzelgegner mit hohem Druck."),
                new ManeuverNote("Timing", "Gut in der Runde, in der du die Initiative halten willst."),
                new ManeuverNote("Kombi", "Passt zu Schild und defensivem Kampfstil.")
            }),
        new(
            "Gezieltes Ausweichen",
            "Abwehrmanoever",
            "Freie Aktion oder Reaktion",
            "Ausweichen",
            "Linie verlassen",
            "Bewegung ersetzt Waffenbindung und schafft Distanz oder Winkelvorteil.",
            new[] { "Mobilitaet", "Abstand", "Initiative" },
            new[]
            {
                new ManeuverNote("Einsatz", "Wenn Position wichtiger ist als reine Waffenabwehr."),
                new ManeuverNote("Timing", "Stark bei Unterzahl oder wenn du aus der Bindung musst."),
                new ManeuverNote("Kombi", "Synergiert mit hoher GE, INI und freier Bahn.")
            }),
        new(
            "Befreiungsschlag",
            "Spezialmanoever",
            "Hohe Ansage",
            "Attacke gegen mehrere Gegner",
            "Raum schaffen",
            "Weiter Schlag gegen Bedraengung, angelehnt an die WdS-Option fuer enge Lagen.",
            new[] { "Flaeche", "Kontrolle", "Optional" },
            new[]
            {
                new ManeuverNote("Einsatz", "Wenn du gleichzeitig von mehreren Gegnern gebunden wirst."),
                new ManeuverNote("Timing", "Nicht fuer den Dauereinsatz, sondern fuer kritische Engstellen."),
                new ManeuverNote("Kombi", "Vorbereitete Initiative oder Platz im Ruecken helfen enorm.")
            }),
        new(
            "Entwaffnen",
            "Spezialmanoever",
            "Situativ",
            "Attacke gegen Waffenfuehrung",
            "Gegner verliert Druck",
            "Nicht auf Schaden, sondern auf den gegnerischen Waffenarm und dessen Kontrolle gezielt.",
            new[] { "Technik", "Kontrolle", "Optional" },
            new[]
            {
                new ManeuverNote("Einsatz", "Gegen bewaffnete Gegner mit gefaehrlicher Hauptwaffe."),
                new ManeuverNote("Timing", "Wenn Schaden nicht reicht oder die Lage sofort kippen muss."),
                new ManeuverNote("Kombi", "Mit Ueberzahl oder nach gelungener Parade besonders stark.")
            }),
        new(
            "Sturmangriff",
            "Angriffsmanoever",
            "Anlauf noetig",
            "Attacke",
            "Wucht aus Bewegung",
            "Bewegung und Treffermoment werden gebuendelt, braucht Raum und klare Linie.",
            new[] { "Bewegung", "Eroeffnung", "Optional" },
            new[]
            {
                new ManeuverNote("Einsatz", "Fuer den Kampfbeginn oder bei offener Distanz."),
                new ManeuverNote("Timing", "Nur sinnvoll, wenn Platz fuer Anlauf und Linie vorhanden sind."),
                new ManeuverNote("Kombi", "Hohe GS und fruehe Initiative machen das Manoever verlaesslicher.")
            })
    };

    private readonly List<QuickAction> _quickActions = new()
    {
        new("Attacke", "Offensiver Standardwurf oder angesagtes Manoever."),
        new("Parade", "Defensive Reaktion gegen den naechsten Angriff."),
        new("Ausweichen", "Position retten und aus der Linie gehen.")
    };

    private readonly List<InitiativeEntry> _initiativeEntries = new()
    {
        new("1", "Alrik vom Blautann", "Spielerheld", 12, true),
        new("2", "Kultist mit Speer", "Nahkampf links", 11, false),
        new("3", "Kultist mit Dolch", "Nahkampf rechts", 9, false),
        new("4", "Bogenschuetze", "Hintere Reihe", 7, false)
    };

    private readonly List<HistoryEntry> _historyEntries = new()
    {
        new("Runde 1", "Alrik", "Finte", "Finte angesagt, Gegner haelt Parade knapp.", "attack"),
        new("Runde 2", "Kultist", "Angriff", "Speerangriff trifft nicht, Parade bleibt stabil.", "neutral"),
        new("Runde 2", "Alrik", "Parade", "Meisterparade setzt die bessere Folgeposition.", "defense"),
        new("Runde 3", "Alrik", "Wahl offen", "Wuchtschlag oder Ausweichen sind beide plausible Folgeoptionen.", "neutral")
    };

    private int Modifier { get; set; }
    private bool _isSearchOpen;

    public string SearchTerm { get; set; } = string.Empty;
    public string SelectedManeuverName { get; set; } = "Finte";
    public string SelectedAttributeCode { get; set; } = "MU";
    public string ActiveActionLabel { get; set; } = "Attacke";

    private IEnumerable<ManeuverCard> FilteredManeuvers =>
        (string.IsNullOrWhiteSpace(SearchTerm) ? _maneuvers : _maneuvers.Where(maneuver =>
            maneuver.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
            || maneuver.Category.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
            || maneuver.Keywords.Any(keyword => keyword.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))))
        .Take(6);

    private ManeuverCard ActiveManeuver =>
        _maneuvers.FirstOrDefault(maneuver => maneuver.Name == SelectedManeuverName) ?? _maneuvers[0];

    private HeroAttribute ActiveAttribute =>
        _attributes.FirstOrDefault(attribute => attribute.Code == SelectedAttributeCode) ?? _attributes[0];

    private string FormattedModifier => Modifier > 0 ? $"+{Modifier}" : Modifier.ToString();

    private async Task HandleSearchBlur()
    {
        await Task.Delay(140);
        _isSearchOpen = false;
    }

    private void SelectManeuverFromSearch(string maneuverName)
    {
        SelectedManeuverName = maneuverName;
        SearchTerm = maneuverName;
        _isSearchOpen = false;
    }

    private void HandleModifierChanged(int value)
    {
        Modifier = Math.Clamp(value, -8, 8);
    }

    private void SelectAttribute(string attributeCode)
    {
        SelectedAttributeCode = attributeCode;
    }

    private void SelectAction(string actionLabel)
    {
        ActiveActionLabel = actionLabel;
    }

    private sealed record StatusBar(string Code, string Label, int Current, int Maximum, int Percent, string ToneClass);

    private sealed record StatusSummaryItem(string Label, string Value, string Meta);

    private sealed record StatusEffect(string Name, string Detail, string ToneClass);

    private sealed record CombatFact(string Label, string Value, string Meta);

    private sealed record WoundState(string Label, bool IsMarked);

    private sealed record WeaponState(string Slot, string Name, string Stats);

    private sealed record HeroAttribute(string Code, string Label, int Value, string IconPath);

    private sealed record ManeuverNote(string Label, string Text);

    private sealed record ManeuverCard(
        string Name,
        string Category,
        string Announcement,
        string Check,
        string Impact,
        string Summary,
        string[] Keywords,
        ManeuverNote[] Notes);

    private sealed record QuickAction(string Label, string Description);

    private sealed record InitiativeEntry(string Rank, string Name, string Meta, int Value, bool IsActive);

    private sealed record HistoryEntry(string Round, string Actor, string Action, string Text, string ToneClass);
}
