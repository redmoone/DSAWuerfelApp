using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class CombatEnemyProfileAdapter(CombatEnemyCatalogStore catalogStore)
{
    public CombatEnemyAdapterResultDto Resolve(CombatEnemySelectionDto selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (!catalogStore.TryGetEntry(selection.EnemyId, out var enemy))
        {
            return Invalid($"Der Gegner '{selection.EnemyId}' ist im DSA-4.1-Katalog nicht vorhanden.");
        }

        var variantResult = ResolveVariant(enemy, selection.VariantId);
        if (!variantResult.IsValid)
        {
            return Invalid(variantResult.Message!);
        }

        var variant = variantResult.Variant;
        var combat = variant?.Combat ?? enemy.Combat;
        if (combat is null)
        {
            return Invalid($"Für '{enemy.Name}' wurde keine konkrete Erfahrungsvariante ausgewählt.");
        }

        var attackResult = ResolveAttack(enemy, selection.AttackId);
        if (!attackResult.IsValid)
        {
            return Invalid(attackResult.Message!);
        }

        var equipmentResult = ResolveEquipment(enemy, selection);
        if (!equipmentResult.IsValid)
        {
            return Invalid(equipmentResult.Message!);
        }

        var lePResult = ResolveLeP(combat.Resources.LeP, selection.LeP);
        var selectedAttack = attackResult.Attack;
        var attack = combat.AttackValue ?? selectedAttack?.Attack;
        var rangedValue = combat.RangedValue ?? selectedAttack?.RangedValue;
        var isMasterfulEvasion = string.Equals(
            combat.Defense.Mode,
            "masterfulEvasion",
            StringComparison.OrdinalIgnoreCase);
        var selectedSpecialRuleRefs = (variant?.SpecialRuleRefs ?? [])
            .Concat(selectedAttack?.SpecialRuleRefs ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var hasUnresolvedAttack = enemy.Attacks.Length > 1 && selectedAttack is null && !combat.AttackValue.HasValue;
        var hasUnresolvedLeP = !lePResult.IsResolved;
        var hasUnresolvedEquipment = equipmentResult.RequiresValues;
        var isCombatReady = enemy.CombatReady &&
                            !hasUnresolvedAttack &&
                            !hasUnresolvedLeP &&
                            !hasUnresolvedEquipment;
        if (!enemy.CombatReady &&
            enemy.Readiness == "sourceDependent" &&
            !hasUnresolvedAttack &&
            !hasUnresolvedLeP &&
            !hasUnresolvedEquipment)
        {
            isCombatReady = true;
        }

        var readiness = ResolveReadiness(
            enemy,
            variant,
            equipmentResult,
            hasUnresolvedAttack,
            hasUnresolvedLeP,
            hasUnresolvedEquipment,
            isCombatReady);
        var profile = new CombatEnemyResolvedProfileDto
        {
            CatalogEnemyId = enemy.Id,
            Name = enemy.Name,
            Kind = enemy.Kind,
            CatalogCombatReady = enemy.CombatReady,
            CombatReady = isCombatReady,
            Readiness = readiness,
            VariantId = variant?.Id,
            VariantLabel = variant?.Label,
            AttackId = selectedAttack?.Id,
            WeaponSelection = selection.WeaponOption,
            ArmorSelection = selection.ArmorOption,
            LeP = lePResult.Value,
            LePSourceMin = lePResult.SourceRange?.Min,
            LePSourceMax = lePResult.SourceRange?.Max,
            AuP = combat.Resources.AuP.Initial ?? combat.Resources.AuP.Maximum,
            AuPTracking = combat.Resources.AuP.Tracking,
            InitiativeNotation = combat.Initiative.Notation,
            InitiativeBase = combat.Initiative.Base,
            InitiativeDiceCount = combat.Initiative.Dice.Count,
            InitiativeDiceSides = combat.Initiative.Dice.Sides,
            DefenseMode = combat.Defense.Mode,
            DefenseSourceNotation = combat.Defense.SourceNotation,
            Attack = attack,
            Parry = isMasterfulEvasion ? null : combat.Defense.ParryValue,
            Dodge = isMasterfulEvasion ? combat.Defense.ParryValue : null,
            RangedValue = rangedValue,
            MovementGs = combat.Movement.Gs,
            Mr = combat.Resistances.Mr,
            MrRaw = combat.Resistances.MrRaw,
            WoundThreshold = combat.Wounds.Threshold.Value,
            Armor = enemy.Armor,
            Attacks = enemy.Attacks,
            SourceRefs = enemy.SourceRefs,
            SpecialRules = enemy.SpecialRules,
            SpecialRuleRefs = selectedSpecialRuleRefs,
            SourceNotes = BuildSourceNotes(enemy, selectedAttack)
        };

        return new CombatEnemyAdapterResultDto(
            true,
            isCombatReady ? null : BuildReadinessMessage(profile),
            profile);
    }

    private static (bool IsValid, string? Message, CombatEnemyCombatVariantDto? Variant) ResolveVariant(
        CombatEnemyCatalogEntryDto enemy,
        string? variantId)
    {
        if (enemy.CombatVariants.Length == 0)
        {
            return (true, null, null);
        }

        if (string.IsNullOrWhiteSpace(variantId))
        {
            return (false, $"Für '{enemy.Name}' muss zuerst eine Erfahrungsvariante gewählt werden.", null);
        }

        var variant = enemy.CombatVariants.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, variantId.Trim(), StringComparison.Ordinal));
        return variant is null
            ? (false, $"Die Erfahrungsvariante '{variantId}' gehört nicht zu '{enemy.Name}'.", null)
            : (true, null, variant);
    }

    private static (bool IsValid, string? Message, CombatEnemyAttackDto? Attack) ResolveAttack(
        CombatEnemyCatalogEntryDto enemy,
        string? attackId)
    {
        if (enemy.Attacks.Length == 0)
        {
            return (true, null, null);
        }

        if (enemy.Attacks.Length == 1 && string.IsNullOrWhiteSpace(attackId))
        {
            return (true, null, enemy.Attacks[0]);
        }

        if (string.IsNullOrWhiteSpace(attackId))
        {
            return (true, null, null);
        }

        var attack = enemy.Attacks.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, attackId.Trim(), StringComparison.Ordinal));
        return attack is null
            ? (false, $"Der Angriff '{attackId}' gehört nicht zu '{enemy.Name}'.", null)
            : (true, null, attack);
    }

    private static (bool IsValid, string? Message, bool RequiresValues) ResolveEquipment(
        CombatEnemyCatalogEntryDto enemy,
        CombatEnemySelectionDto selection)
    {
        if (enemy.Readiness != "requiresEquipmentSelection")
        {
            return (true, null, false);
        }

        if (enemy.Equipment is null || !enemy.Equipment.SelectionRequired)
        {
            return (false, $"Für '{enemy.Name}' fehlt die definierte Ausrüstungsgrenze.", false);
        }

        if (string.IsNullOrWhiteSpace(selection.WeaponOption) ||
            !enemy.Equipment.WeaponOptions.Contains(selection.WeaponOption, StringComparer.Ordinal))
        {
            return (false, $"Für '{enemy.Name}' muss eine gültige Waffe aus dem Katalog gewählt werden.", false);
        }

        if (string.IsNullOrWhiteSpace(selection.ArmorOption) ||
            !enemy.Equipment.ArmorOptions.Contains(selection.ArmorOption, StringComparer.Ordinal))
        {
            return (false, $"Für '{enemy.Name}' muss eine gültige Rüstung aus dem Katalog gewählt werden.", false);
        }

        return (true, null, true);
    }

    private static (bool IsResolved, int? Value, CombatEnemyRangeDto? SourceRange) ResolveLeP(
        CombatEnemyResourceDto resource,
        int? selectedValue)
    {
        if (resource.SourceRange is not { } sourceRange)
        {
            return (true, resource.Initial ?? resource.Maximum, null);
        }

        if (selectedValue is not { } value)
        {
            return (false, null, sourceRange);
        }

        if (sourceRange.Min is not { } min || sourceRange.Max is not { } max || value < min || value > max)
        {
            return (false, null, sourceRange);
        }

        return (true, value, sourceRange);
    }

    private static string ResolveReadiness(
        CombatEnemyCatalogEntryDto enemy,
        CombatEnemyCombatVariantDto? variant,
        (bool IsValid, string? Message, bool RequiresValues) equipment,
        bool unresolvedAttack,
        bool unresolvedLeP,
        bool unresolvedEquipment,
        bool isCombatReady)
    {
        if (isCombatReady)
        {
            return "ready";
        }

        if (unresolvedEquipment)
        {
            return "requiresEquipmentValues";
        }

        if (unresolvedAttack)
        {
            return "requiresAttackSelection";
        }

        if (unresolvedLeP)
        {
            return "sourceDependent";
        }

        return variant is null ? enemy.Readiness : "requiresSelection";
    }

    private static string BuildReadinessMessage(CombatEnemyResolvedProfileDto profile)
    {
        return profile.Readiness switch
        {
            "requiresEquipmentValues" =>
                "Waffe und Rüstung sind ausgewählt, aber der Katalog enthält dafür keine TP-/RS-Werte. Keine Werte wurden erfunden.",
            "requiresAttackSelection" =>
                "Für diesen Gegner muss zuerst ein konkreter Angriff ausgewählt werden.",
            "sourceDependent" when profile.LePSourceMin.HasValue =>
                $"LeP muss als Meisterentscheidung zwischen {profile.LePSourceMin} und {profile.LePSourceMax} festgelegt werden.",
            _ => "Das Gegnerprofil ist für den Kampfeinsatz noch nicht vollständig ausgewählt."
        };
    }

    private static string[] BuildSourceNotes(
        CombatEnemyCatalogEntryDto enemy,
        CombatEnemyAttackDto? attack)
    {
        return enemy.GmNotes
            .Concat(enemy.Behavior?.SourceNotes ?? [])
            .Concat(enemy.SpecialRules
                .Select(rule => rule.SourceNotation)
                .Where(note => !string.IsNullOrWhiteSpace(note))
                .Select(note => note!))
            .Concat(attack?.SourceNotation is { } sourceNotation
                ? [sourceNotation]
                : [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static CombatEnemyAdapterResultDto Invalid(string message) =>
        new(false, message, null);
}
