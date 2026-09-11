namespace DsaWuerfelApp.Shared;

public static class CombatRuntimeModifierRules
{
    private const string GeneralWoundSource = "WdS S. 83";
    private const string ZoneWoundSource = "WdS S. 107–110";

    public static CombatRuntimeModifierResult Resolve(
        CombatProfileDto? profile,
        CombatSetVariantDto? set,
        CombatRuntimeStateDto? state,
        CombatActionKind action,
        CombatRuleOptionsDto? options = null)
    {
        if (profile is null || set is null || state is null || !state.IsStarted)
        {
            return Empty();
        }

        options ??= new CombatRuleOptionsDto();
        var modifiers = new List<CombatModifierDto>();
        var notes = new List<string>();
        var wounds = NormalizeWounds(state.Wounds, notes);
        var knownWounds = wounds.Values.Where(value => value.HasValue).Sum(value => value ?? 0);
        var hasApplicableCheck = IsAttackParryOrRanged(action);
        var hasAttackOrParry = IsAttackOrParry(action);

        if (set.UsesZonalArmor)
        {
            AddZonalWoundModifiers(wounds, action, hasAttackOrParry, modifiers, notes);
        }
        else if (hasApplicableCheck && knownWounds > 0)
        {
            var penalty = -2 * knownWounds;
            modifiers.Add(new CombatModifierDto("Wunden", penalty, GeneralWoundSource));
            notes.Add($"{GeneralWoundSource}: {knownWounds} erfasste Wunde(n) senken diesen Wurf um {Math.Abs(penalty)}.");
        }

        if (options.LowLePEnabled)
        {
            AddLowLePModifier(profile.Resources.LeP, state.CurrentLeP, action, modifiers, notes);
            AddLowAuPModifier(profile.Resources.AuP, state.CurrentAuP, action, modifiers, notes);
        }

        if (wounds.Values.Any(value => !value.HasValue))
        {
            notes.Add(knownWounds == 0
                ? "Wundstand nicht erfasst; es wurde kein unbekannter Wundabzug als 0 erfunden."
                : "Wundstand nicht erfasst für alle Zonen; bekannte Wunden wurden berücksichtigt, unbekannte nicht als 0 erfunden.");
        }

        return new CombatRuntimeModifierResult(modifiers.ToArray(), notes.ToArray());
    }

    public static CombatRuntimeInitiativeResult ResolveInitiative(
        CombatProfileDto? profile,
        CombatSetVariantDto? set,
        CombatRuntimeStateDto? state)
    {
        if (profile is null || set is null || state is null)
        {
            return new CombatRuntimeInitiativeResult(0,
                ["Laufender Kampfzustand für INI nicht übertragen; automatische INI-Abzüge bleiben unbekannt."]);
        }

        var notes = new List<string>();
        var wounds = NormalizeWounds(state.Wounds, notes);
        var modifier = set.UsesZonalArmor
            ? ResolveZonalInitiativeModifier(wounds, notes)
            : ResolveGeneralInitiativeModifier(wounds, notes);

        if (!profile.Resources.AuP.HasValue || !state.CurrentAuP.HasValue || profile.Resources.AuP <= 0)
        {
            notes.Add("WdS S. 83: AuP oder AuP-Maximum nicht erfasst; der INI-Abzug durch niedrige AuP bleibt unbekannt.");
        }
        else
        {
            var auPPenalty = GetLowAuPPenalty(state.CurrentAuP.Value, profile.Resources.AuP.Value);
            if (auPPenalty > 0)
            {
                modifier -= auPPenalty;
                notes.Add($"WdS S. 83: AuP {state.CurrentAuP}/{profile.Resources.AuP} · INI {FormatSigned(-auPPenalty)}.");
            }

            if (state.CurrentAuP == 0)
            {
                notes.Add("WdS S. 83: AuP 0 bedeutet Kampfunfähigkeit; als einzige Aktion/Reaktion bleibt Atem holen.");
            }
        }

        if (wounds.Values.Any(value => !value.HasValue))
        {
            notes.Add("Wundstand nicht erfasst; bekannte Wunden wurden berücksichtigt, unbekannte nicht als 0 erfunden.");
        }

        return new CombatRuntimeInitiativeResult(modifier, notes.ToArray());
    }

    private static int ResolveGeneralInitiativeModifier(
        IReadOnlyDictionary<CombatWoundZone, int?> wounds,
        ICollection<string> notes)
    {
        var knownWounds = wounds.Values.Where(value => value.HasValue).Sum(value => value ?? 0);
        if (knownWounds == 0)
        {
            return 0;
        }

        var modifier = -2 * knownWounds;
        notes.Add($"{GeneralWoundSource}: {knownWounds} erfasste Wunde(n) senken die INI um {Math.Abs(modifier)}.");
        return modifier;
    }

    private static int ResolveZonalInitiativeModifier(
        IReadOnlyDictionary<CombatWoundZone, int?> wounds,
        ICollection<string> notes)
    {
        var modifier = 0;
        modifier += AddZonalInitiativePenalty(wounds, CombatWoundZone.Head, "Kopfwunden", -2, notes);
        modifier += AddZonalInitiativePenalty(wounds, CombatWoundZone.Abdomen, "Bauchwunden", -1, notes);
        modifier += AddZonalInitiativePenalty(wounds, CombatWoundZone.LeftLeg, "Wunden linkes Bein", -2, notes);
        modifier += AddZonalInitiativePenalty(wounds, CombatWoundZone.RightLeg, "Wunden rechtes Bein", -2, notes);

        if (GetKnownWoundCount(wounds, CombatWoundZone.Head) > 0)
        {
            notes.Add($"{ZoneWoundSource}: Kopfwunden verursachen zusätzlich einen nicht gespeicherten 2W6-INI-Verlust; bitte am Tisch würfeln.");
        }

        return modifier;
    }

    private static int AddZonalInitiativePenalty(
        IReadOnlyDictionary<CombatWoundZone, int?> wounds,
        CombatWoundZone zone,
        string label,
        int penaltyPerWound,
        ICollection<string> notes)
    {
        var affectedWounds = Math.Min(GetKnownWoundCount(wounds, zone), 2);
        if (affectedWounds == 0)
        {
            return 0;
        }

        var modifier = penaltyPerWound * affectedWounds;
        notes.Add($"{ZoneWoundSource}: {label} {affectedWounds} · INI {FormatSigned(modifier)}.");
        return modifier;
    }

    private static Dictionary<CombatWoundZone, int?> NormalizeWounds(
        IReadOnlyDictionary<CombatWoundZone, int?>? source,
        ICollection<string> notes)
    {
        var wounds = new Dictionary<CombatWoundZone, int?>();
        foreach (var zone in Enum.GetValues<CombatWoundZone>())
        {
            if (source is null || !source.TryGetValue(zone, out var value))
            {
                wounds[zone] = null;
                continue;
            }

            if (value is < 0 or > 3)
            {
                notes.Add($"Wundstand {GetZoneLabel(zone)} liegt außerhalb von 0 bis 3 und wurde nicht verrechnet.");
                wounds[zone] = null;
                continue;
            }

            wounds[zone] = value;
        }

        return wounds;
    }

    private static void AddZonalWoundModifiers(
        IReadOnlyDictionary<CombatWoundZone, int?> wounds,
        CombatActionKind action,
        bool hasAttackOrParry,
        ICollection<CombatModifierDto> modifiers,
        ICollection<string> notes)
    {
        if (!hasAttackOrParry)
        {
            return;
        }

        AddZonePenalty(wounds, CombatWoundZone.Torso, "Brustwunden", -1, action, modifiers, notes);
        AddZonePenalty(wounds, CombatWoundZone.Abdomen, "Bauchwunden", -1, action, modifiers, notes);
        AddZonePenalty(wounds, CombatWoundZone.LeftLeg, "Wunden linkes Bein", -2, action, modifiers, notes);
        AddZonePenalty(wounds, CombatWoundZone.RightLeg, "Wunden rechtes Bein", -2, action, modifiers, notes);

        var leftArm = GetKnownWoundCount(wounds, CombatWoundZone.LeftArm);
        var rightArm = GetKnownWoundCount(wounds, CombatWoundZone.RightArm);
        if (leftArm > 0 || rightArm > 0)
        {
            notes.Add($"{ZoneWoundSource}: Armwunden wirken mit −2 je Wunde auf AT/PA des betroffenen Arms; der Waffenarm ist im Import nicht festgelegt.");
        }

        AddThirdWoundNotes(wounds, notes);
    }

    private static void AddZonePenalty(
        IReadOnlyDictionary<CombatWoundZone, int?> wounds,
        CombatWoundZone zone,
        string label,
        int penaltyPerWound,
        CombatActionKind action,
        ICollection<CombatModifierDto> modifiers,
        ICollection<string> notes)
    {
        var count = GetKnownWoundCount(wounds, zone);
        var affectedWounds = Math.Min(count, 2);
        if (affectedWounds == 0)
        {
            return;
        }

        var penalty = penaltyPerWound * affectedWounds;
        modifiers.Add(new CombatModifierDto(label, penalty, ZoneWoundSource));
        notes.Add($"{ZoneWoundSource}: {GetZoneLabel(zone)} {affectedWounds} · {GetAffectedCheckLabel(action)} {FormatSigned(penalty)}.");
    }

    private static void AddThirdWoundNotes(
        IReadOnlyDictionary<CombatWoundZone, int?> wounds,
        ICollection<string> notes)
    {
        foreach (var pair in wounds.Where(pair => pair.Value == 3))
        {
            var consequence = pair.Key switch
            {
                CombatWoundZone.Head or CombatWoundZone.Torso or CombatWoundZone.Abdomen => "Bewusstlosigkeit/Blutverlust",
                CombatWoundZone.LeftArm or CombatWoundZone.RightArm => "Arm handlungsunfähig",
                CombatWoundZone.LeftLeg or CombatWoundZone.RightLeg => "Sturz/Kampfunfähigkeit",
                _ => "zusätzliche Wundfolge"
            };
            notes.Add($"{ZoneWoundSource}: dritte Wunde {GetZoneLabel(pair.Key)} · {consequence} am Tisch bestätigen.");
        }
    }

    private static void AddLowLePModifier(
        int? maximum,
        int? current,
        CombatActionKind action,
        ICollection<CombatModifierDto> modifiers,
        ICollection<string> notes)
    {
        if (!current.HasValue || !maximum.HasValue || maximum <= 0)
        {
            if (!current.HasValue || !maximum.HasValue)
            {
                notes.Add("WdS S. 83: LeP oder LeP-Maximum nicht erfasst; niedriger-LeP-Abzug bleibt unbekannt.");
            }

            return;
        }

        var penalty = GetLowLePPenalty(current.Value, maximum.Value);
        if (penalty == 0)
        {
            return;
        }

        if (IsAttackParryOrRanged(action))
        {
            modifiers.Add(new CombatModifierDto("Niedrige LeP", -penalty, "WdS S. 83"));
            notes.Add($"WdS S. 83: LeP {current}/{maximum} · {GetAffectedCheckLabel(action)} {FormatSigned(-penalty)}.");
        }

        if (current <= 5)
        {
            notes.Add("WdS S. 83: LeP 5 oder weniger bedeutet Kampfunfähigkeit; die Tischentscheidung bleibt sichtbar.");
        }
    }

    private static void AddLowAuPModifier(
        int? maximum,
        int? current,
        CombatActionKind action,
        ICollection<CombatModifierDto> modifiers,
        ICollection<string> notes)
    {
        if (!current.HasValue || !maximum.HasValue || maximum <= 0)
        {
            if (!current.HasValue || !maximum.HasValue)
            {
                notes.Add("WdS S. 83: AuP oder AuP-Maximum nicht erfasst; niedriger-AuP-Abzug bleibt unbekannt.");
            }

            return;
        }

        var penalty = GetLowAuPPenalty(current.Value, maximum.Value);
        if (penalty == 0)
        {
            return;
        }

        if (IsAttackOrParry(action))
        {
            modifiers.Add(new CombatModifierDto("Niedrige AuP", -penalty, "WdS S. 83"));
            notes.Add($"WdS S. 83: AuP {current}/{maximum} · {GetAffectedCheckLabel(action)} {FormatSigned(-penalty)}; INI sinkt ebenfalls um {penalty}.");
        }

        if (current == 0)
        {
            notes.Add("WdS S. 83: AuP 0 bedeutet Kampfunfähigkeit; als einzige Aktion/Reaktion bleibt Atem holen.");
        }
    }

    public static int GetLowLePPenalty(int current, int maximum)
    {
        if (maximum <= 0)
        {
            return 0;
        }

        if (current * 4 < maximum)
        {
            return 3;
        }

        if (current * 3 < maximum)
        {
            return 2;
        }

        return current * 2 < maximum ? 1 : 0;
    }

    public static int GetLowAuPPenalty(int current, int maximum)
    {
        if (maximum <= 0)
        {
            return 0;
        }

        if (current * 4 < maximum)
        {
            return 2;
        }

        return current * 3 < maximum ? 1 : 0;
    }

    private static bool IsAttackParryOrRanged(CombatActionKind action) => action is
        CombatActionKind.MeleeAttack or
        CombatActionKind.WeaponParry or
        CombatActionKind.ShieldParry or
        CombatActionKind.RangedAttack;

    private static bool IsAttackOrParry(CombatActionKind action) => action is
        CombatActionKind.MeleeAttack or
        CombatActionKind.WeaponParry or
        CombatActionKind.ShieldParry;

    private static int GetKnownWoundCount(
        IReadOnlyDictionary<CombatWoundZone, int?> wounds,
        CombatWoundZone zone) => wounds.TryGetValue(zone, out var value) && value.HasValue
        ? value.Value
        : 0;

    private static string GetAffectedCheckLabel(CombatActionKind action) => action switch
    {
        CombatActionKind.MeleeAttack => "AT",
        CombatActionKind.WeaponParry or CombatActionKind.ShieldParry => "PA",
        CombatActionKind.RangedAttack => "FK",
        _ => "Kampfprobe"
    };

    private static string GetZoneLabel(CombatWoundZone zone) => zone switch
    {
        CombatWoundZone.Head => "Kopf",
        CombatWoundZone.Torso => "Brust/Rücken",
        CombatWoundZone.Abdomen => "Bauch",
        CombatWoundZone.LeftArm => "linker Arm",
        CombatWoundZone.RightArm => "rechter Arm",
        CombatWoundZone.LeftLeg => "linkes Bein",
        CombatWoundZone.RightLeg => "rechtes Bein",
        _ => zone.ToString()
    };

    private static string FormatSigned(int value) => value > 0 ? $"+{value}" : value.ToString();

    private static CombatRuntimeModifierResult Empty() => new([], []);
}
