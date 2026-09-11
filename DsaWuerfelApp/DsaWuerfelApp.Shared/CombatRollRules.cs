namespace DsaWuerfelApp.Shared;

public static class CombatRollRules
{
    public static int? ResolveEffectiveTarget(
        int? baseValue,
        IReadOnlyList<CombatModifierDto>? modifiers)
    {
        if (!baseValue.HasValue)
        {
            return null;
        }

        return baseValue.Value + (modifiers?.Sum(modifier => modifier.Value) ?? 0);
    }

    public static CombatRollEvaluationDto Evaluate(
        CombatActionKind action,
        int? baseValue,
        IReadOnlyList<CombatModifierDto>? modifiers,
        int mainRoll,
        int? controlRoll = null,
        int? unmodifiedBaseValue = null,
        CombatRuleOptionsDto? options = null)
    {
        options ??= new CombatRuleOptionsDto();

        if (mainRoll is < 1 or > 20)
        {
            return Invalid(action, baseValue, mainRoll, "Der Hauptwurf muss zwischen 1 und 20 liegen.");
        }

        if (controlRoll is < 1 or > 20)
        {
            return Invalid(action, baseValue, mainRoll, "Der Kontrollwurf muss zwischen 1 und 20 liegen.");
        }

        var target = ResolveEffectiveTarget(baseValue, modifiers);
        if (!target.HasValue)
        {
            return Invalid(action, baseValue, mainRoll, "Für diesen Kampfwurf fehlt ein Zielwert.");
        }

        if (!IsCheckAction(action))
        {
            return Invalid(action, baseValue, mainRoll, "Diese Aktion ist kein AT-, PA-, Ausweich- oder FK-Wurf.");
        }

        if (!options.SpecialResultsEnabled)
        {
            var ordinarySuccess = mainRoll != 20 && mainRoll <= target.Value;
            return new CombatRollEvaluationDto(
                true,
                null,
                action,
                baseValue,
                target,
                null,
                mainRoll,
                null,
                ordinarySuccess ? CombatOutcome.Success : CombatOutcome.Failure,
                ordinarySuccess,
                false,
                false,
                IsAttack(action) && ordinarySuccess,
                ordinarySuccess ? "Gelungen" : "Misslungen");
        }

        if (mainRoll == 20)
        {
            var controlTarget = action == CombatActionKind.RangedAttack
                ? unmodifiedBaseValue ?? target.Value
                : target.Value;
            if (!controlRoll.HasValue)
            {
                return PendingControl(action, baseValue, target.Value, controlTarget, mainRoll,
                    "Kontrollwurf für Patzer ausstehend.");
            }

            var avoided = controlRoll.Value <= controlTarget;
            return new CombatRollEvaluationDto(
                true,
                null,
                action,
                baseValue,
                target,
                controlTarget,
                mainRoll,
                controlRoll,
                avoided ? CombatOutcome.FumbleAvoided : CombatOutcome.Fumble,
                false,
                false,
                !avoided,
                false,
                avoided ? "Misslungen · Patzer abgewendet" : "PATZER · Kontrollwurf misslungen");
        }

        if (mainRoll == 1)
        {
            if (action == CombatActionKind.RangedAttack && !RangedLuckyRollIsAllowed(baseValue, modifiers))
            {
                return new CombatRollEvaluationDto(
                    true,
                    null,
                    action,
                    baseValue,
                    target,
                    null,
                    mainRoll,
                    null,
                    CombatOutcome.Failure,
                    false,
                    false,
                    false,
                    false,
                    "Misslungen · FK-Glücksbedingung nicht erfüllt");
            }

            if (!controlRoll.HasValue)
            {
                return PendingControl(action, baseValue, target.Value, target.Value, mainRoll,
                    "Kontrollwurf für Glückswurf ausstehend.");
            }

            var confirmed = controlRoll.Value <= target.Value;
            var parry = action is CombatActionKind.WeaponParry or CombatActionKind.ShieldParry;
            var critical = !parry && confirmed;
            var outcome = parry
                ? confirmed ? CombatOutcome.Lucky : CombatOutcome.Success
                : critical ? CombatOutcome.Critical : CombatOutcome.Lucky;
            var label = action switch
            {
                CombatActionKind.MeleeAttack => critical ? "Kritische AT" : "Glückliche AT",
                CombatActionKind.WeaponParry or CombatActionKind.ShieldParry =>
                    confirmed ? "Glückliche PA" : "Gelungene PA",
                CombatActionKind.RangedAttack => critical ? "Kritischer FK" : "Glücklicher FK",
                _ => critical ? "Kritischer Wurf" : "Glücklicher Wurf"
            };

            return new CombatRollEvaluationDto(
                true,
                null,
                action,
                baseValue,
                target,
                target,
                mainRoll,
                controlRoll,
                outcome,
                true,
                critical,
                false,
                IsAttack(action),
                label);
        }

        var success = mainRoll <= target.Value;
        return new CombatRollEvaluationDto(
            true,
            null,
            action,
            baseValue,
            target,
            null,
            mainRoll,
            null,
            success ? CombatOutcome.Success : CombatOutcome.Failure,
            success,
            false,
            false,
            IsAttack(action) && success,
            success ? IsAttack(action) ? "Attacke gelungen" : "Gelungen" : "Misslungen");
    }

    public static CombatDamageCalculationDto CalculateDamage(
        int diceTotal,
        int weaponBonus,
        int preMultiplierModifier,
        int multiplier,
        int postMultiplierModifier,
        bool isCritical = false)
    {
        if (diceTotal < 0 || multiplier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(diceTotal), "Schadensbestandteile müssen gültig sein.");
        }

        var total = Math.Max(0,
            (diceTotal + weaponBonus + preMultiplierModifier) * multiplier + postMultiplierModifier);
        return new CombatDamageCalculationDto(
            diceTotal,
            weaponBonus,
            preMultiplierModifier,
            multiplier,
            postMultiplierModifier,
            total,
            isCritical);
    }

    private static bool IsCheckAction(CombatActionKind action) => action is
        CombatActionKind.MeleeAttack or
        CombatActionKind.WeaponParry or
        CombatActionKind.ShieldParry or
        CombatActionKind.Dodge or
        CombatActionKind.RangedAttack;

    private static bool IsAttack(CombatActionKind action) => action is
        CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack;

    private static bool RangedLuckyRollIsAllowed(
        int? baseValue,
        IReadOnlyList<CombatModifierDto>? modifiers)
    {
        if (!baseValue.HasValue)
        {
            return false;
        }

        var penalty = modifiers?.Where(modifier => modifier.Value < 0)
            .Sum(modifier => -modifier.Value) ?? 0;
        return penalty <= baseValue.Value;
    }

    private static CombatRollEvaluationDto PendingControl(
        CombatActionKind action,
        int? baseValue,
        int target,
        int controlTarget,
        int mainRoll,
        string message) => new(
        false,
        message,
        action,
        baseValue,
        target,
        controlTarget,
        mainRoll,
        null,
        CombatOutcome.Unknown,
        false,
        false,
        false,
        false,
        message);

    private static CombatRollEvaluationDto Invalid(
        CombatActionKind action,
        int? baseValue,
        int mainRoll,
        string message) => new(
        false,
        message,
        action,
        baseValue,
        null,
        null,
        mainRoll,
        null,
        CombatOutcome.Unknown,
        false,
        false,
        false,
        false,
        "Nicht ausgewertet");
}
