using System.Globalization;
using System.Text.RegularExpressions;

using DsaWuerfelApp.Services.Application.Import;
using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed partial class RollCombatHandler(
    HeroContextReader heroContextReader,
    HeroCombatProfileReader heroCombatProfileReader,
    DiceService diceService)
{
    public async Task<CombatRollResultDto> HandleAsync(
        CombatRollRequestDto request,
        string userId,
        string playerName = "Unbekannt",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var hero = await heroContextReader.LoadContextAsync(
            request.HeroId,
            request.SessionId,
            userId,
            cancellationToken);
        if (hero is null)
        {
            throw Validation("Für den Kampfwurf ist kein aktiver Held ausgewählt.");
        }

        var ownerUserId = string.IsNullOrWhiteSpace(hero.OwnerUserId) ? userId : hero.OwnerUserId;
        var profile = await heroCombatProfileReader.ReadAsync(hero.Id, ownerUserId, cancellationToken);
        if (profile is null)
        {
            throw new RequestRejectedException(
                RequestRejectionReason.NotFound,
                "Für den ausgewählten Helden ist kein importiertes Kampfprofil verfügbar.");
        }

        var set = ResolveSet(profile, request.SetId);
        var resolvedPlayerName = string.IsNullOrWhiteSpace(playerName) ? "Unbekannt" : playerName.Trim();

        return request.Action switch
        {
            CombatActionKind.MeleeAttack or
            CombatActionKind.WeaponParry or
            CombatActionKind.ShieldParry or
            CombatActionKind.Dodge or
            CombatActionKind.RangedAttack => RollCheck(request, hero, set, resolvedPlayerName, userId),
            CombatActionKind.Damage => RollDamage(request, hero, set, resolvedPlayerName, userId),
            CombatActionKind.HitZone => RollHitZone(request, hero, set, resolvedPlayerName, userId),
            CombatActionKind.InitiativeHelper or
            CombatActionKind.WoundHelper or
            CombatActionKind.FumbleHelper => RollHelper(request, hero, resolvedPlayerName, userId),
            _ => throw Validation("Diese Kampfwurfart wird nicht unterstützt.")
        };
    }

    private CombatRollResultDto RollCheck(
        CombatRollRequestDto request,
        DsaWuerfelApp.Shared.Models.Hero hero,
        CombatSetVariantDto set,
        string playerName,
        string userId)
    {
        var weapon = ResolveWeapon(request, set);
        var baseValue = ResolveCheckValue(request.Action, set, weapon);
        if (!baseValue.HasValue)
        {
            throw Validation($"Für {GetActionLabel(request.Action)} ist im importierten Kampfset kein Zielwert vorhanden.");
        }

        var modifiers = request.Modifiers ?? [];
        var options = request.Options ?? new CombatRuleOptionsDto();
        var mainRoll = RollSingleD20();
        var preliminary = CombatRollRules.Evaluate(
            request.Action,
            baseValue,
            modifiers,
            mainRoll,
            unmodifiedBaseValue: baseValue,
            options: options);

        int? controlRoll = null;
        if (preliminary.Outcome == CombatOutcome.Unknown && preliminary.ControlTarget.HasValue)
        {
            controlRoll = RollSingleD20();
        }

        var evaluation = CombatRollRules.Evaluate(
            request.Action,
            baseValue,
            modifiers,
            mainRoll,
            controlRoll,
            unmodifiedBaseValue: baseValue,
            options);
        if (!evaluation.IsValid)
        {
            throw Validation(evaluation.ValidationMessage ?? "Kampfwurf konnte nicht ausgewertet werden.");
        }

        var rolls = controlRoll.HasValue
            ? new[] { new DiceRollDto(20, mainRoll), new DiceRollDto(20, controlRoll.Value) }
            : new[] { new DiceRollDto(20, mainRoll) };
        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            HeroId = hero.Id,
            Action = request.Action,
            ActionLabel = GetActionLabel(request.Action),
            WeaponName = weapon?.Name,
            ValuesSource = "Import",
            BaseValue = baseValue,
            UnmodifiedBaseValue = baseValue,
            EffectiveTarget = evaluation.EffectiveTarget,
            ControlTarget = evaluation.ControlTarget,
            Modifiers = modifiers.ToArray(),
            RuleOptions = options,
            Outcome = evaluation.Outcome,
            StatusLabel = evaluation.StatusLabel,
            LabeledRolls = controlRoll.HasValue
                ? [
                    new CombatLabeledRollDto("Hauptwurf", 20, mainRoll),
                    new CombatLabeledRollDto("Kontrollwurf", 20, controlRoll.Value)
                ]
                : [new CombatLabeledRollDto("Hauptwurf", 20, mainRoll)],
            RuleNotes = BuildRuleNotes(request, evaluation)
        };

        return CreateResult(
            request,
            hero,
            userId,
            playerName,
            snapshot,
            rolls,
            MapHistoryOutcome(evaluation.Outcome),
            BuildChecks(evaluation));
    }

    private CombatRollResultDto RollDamage(
        CombatRollRequestDto request,
        DsaWuerfelApp.Shared.Models.Hero hero,
        CombatSetVariantDto set,
        string playerName,
        string userId)
    {
        var weapon = ResolveWeapon(request, set)
                     ?? throw Validation("Für einen TP-Wurf muss eine importierte Waffe ausgewählt sein.");
        var damageText = weapon.CalculatedDamage ?? weapon.BaseDamage;
        var damage = ParseDamageExpression(damageText);
        var manualDamageModifier = request.DamageModifier;
        var isCritical = request.Damage?.IsCritical == true;
        var rolls = diceService.RollDice([new DiceRollGroupDto(damage.DiceSides, damage.DiceCount)]);
        var calculation = CombatRollRules.CalculateDamage(
            rolls.Sum(roll => roll.Value),
            damage.WeaponBonus,
            0,
            1,
            manualDamageModifier,
            isCritical);
        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            HeroId = hero.Id,
            Action = CombatActionKind.Damage,
            ActionLabel = GetActionLabel(CombatActionKind.Damage),
            WeaponName = weapon.Name,
            ValuesSource = "Import",
            Modifiers = request.Modifiers ?? [],
            RuleOptions = request.Options ?? new CombatRuleOptionsDto(),
            Outcome = CombatOutcome.Neutral,
            StatusLabel = "TP-Wurf · kein Treffer angewendet",
            LabeledRolls = rolls.Select(roll => new CombatLabeledRollDto("TP-Würfel", roll.Sides, roll.Value)).ToArray(),
            Damage = new CombatDamageSnapshotDto(
                calculation.DiceTotal,
                calculation.WeaponBonus,
                calculation.PreMultiplierModifier,
                calculation.Multiplier,
                calculation.PostMultiplierModifier,
                calculation.Total,
                calculation.IsCritical),
            RuleNotes = BuildRuleNotes(request, null)
                .Append($"Importierter Schaden: {damageText ?? "unbekannt"}")
                .ToArray()
        };

        return CreateResult(
            request,
            hero,
            userId,
            playerName,
            snapshot,
            rolls,
            RollHistoryOutcome.None,
            []);
    }

    private CombatRollResultDto RollHitZone(
        CombatRollRequestDto request,
        DsaWuerfelApp.Shared.Models.Hero hero,
        CombatSetVariantDto set,
        string playerName,
        string userId)
    {
        var d20 = RollSingleD20();
        var mapped = MapHitZone(d20, request.Zone);
        var armorRating = GetArmorValue(set, mapped.ArmorZone);
        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            HeroId = hero.Id,
            Action = CombatActionKind.HitZone,
            ActionLabel = GetActionLabel(CombatActionKind.HitZone),
            ValuesSource = "Regeltabelle",
            RuleOptions = request.Options ?? new CombatRuleOptionsDto(),
            Outcome = CombatOutcome.Neutral,
            StatusLabel = "Trefferzone gewürfelt",
            LabeledRolls = [new CombatLabeledRollDto("Trefferzonenwurf", 20, d20)],
            Zone = new CombatZoneSnapshotDto(d20, mapped.ArmorZone, mapped.WoundZone, request.Zone?.Facing ?? CombatFacing.Front, armorRating),
            RuleNotes = BuildRuleNotes(request, null)
        };

        return CreateResult(
            request,
            hero,
            userId,
            playerName,
            snapshot,
            [new DiceRollDto(20, d20)],
            RollHistoryOutcome.None,
            []);
    }

    private CombatRollResultDto RollHelper(
        CombatRollRequestDto request,
        DsaWuerfelApp.Shared.Models.Hero hero,
        string playerName,
        string userId)
    {
        var helper = request.Helper ?? new CombatHelperRollRequestDto();
        ValidateDice(helper.DiceCount, helper.DiceSides);
        var purpose = string.IsNullOrWhiteSpace(helper.Purpose)
            ? GetActionLabel(request.Action)
            : helper.Purpose.Trim();
        var rolls = diceService.RollDice([new DiceRollGroupDto(helper.DiceSides, helper.DiceCount)]);
        var snapshot = new CombatRollSnapshotDto
        {
            EntryId = Guid.NewGuid(),
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            HeroId = hero.Id,
            Action = request.Action,
            ActionLabel = GetActionLabel(request.Action),
            ValuesSource = "Regelhilfe",
            RuleOptions = request.Options ?? new CombatRuleOptionsDto(),
            Outcome = CombatOutcome.Neutral,
            StatusLabel = $"Hilfswurf · {purpose}",
            LabeledRolls = rolls.Select(roll => new CombatLabeledRollDto(purpose, roll.Sides, roll.Value)).ToArray(),
            RuleNotes = BuildRuleNotes(request, null).Append($"Zweck: {purpose}").ToArray()
        };

        return CreateResult(request, hero, userId, playerName, snapshot, rolls, RollHistoryOutcome.None, []);
    }

    private CombatRollResultDto CreateResult(
        CombatRollRequestDto request,
        DsaWuerfelApp.Shared.Models.Hero hero,
        string userId,
        string playerName,
        CombatRollSnapshotDto snapshot,
        IReadOnlyList<DiceRollDto> rolls,
        RollHistoryOutcome historyOutcome,
        RollHistoryCheckDto[] checks)
    {
        var timestamp = DateTime.UtcNow;
        var rollArray = rolls.ToArray();
        var equation = DiceResultFactory.CreateEquation(rollArray, 0);
        var historyContext = new RollHistoryContextDto(
            RollHistoryKind.Combat,
            GetHistoryDisplayName(snapshot),
            historyOutcome,
            null,
            checks,
            new RollHistorySnapshotDto
            {
                HeroName = hero.Name,
                Combat = snapshot
            });
        var historyEntry = DiceResultFactory.CreateHistoryEntry(playerName, timestamp, equation, historyContext);

        return new CombatRollResultDto(
            snapshot.EntryId,
            request.RequestId,
            request.SessionId,
            userId,
            playerName,
            hero.Name,
            snapshot.Outcome,
            snapshot,
            rollArray,
            historyEntry);
    }

    private int RollSingleD20() => diceService.RollDice([new DiceRollGroupDto(20, 1)])[0].Value;

    private static CombatSetVariantDto ResolveSet(CombatProfileDto profile, string? setId)
    {
        var set = string.IsNullOrWhiteSpace(setId)
            ? profile.Sets.FirstOrDefault(current => current.IsInUse) ?? profile.Sets.FirstOrDefault(current => current.IsDefault)
            : profile.Sets.FirstOrDefault(current => string.Equals(current.Id, setId, StringComparison.Ordinal));
        return set ?? throw Validation("Das ausgewählte Kampfset ist im Import nicht vorhanden.");
    }

    private static CombatWeaponDto? ResolveWeapon(CombatRollRequestDto request, CombatSetVariantDto set)
    {
        if (request.Action == CombatActionKind.Dodge)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.WeaponId))
        {
            throw Validation("Für diese Kampfaktion muss eine Waffe oder Abwehr ausgewählt sein.");
        }

        var weapon = set.Weapons.FirstOrDefault(current =>
            string.Equals(current.Id, request.WeaponId, StringComparison.Ordinal));
        if (weapon is null)
        {
            throw Validation("Die ausgewählte Waffe gehört nicht zum Kampfset.");
        }

        var valid = request.Action switch
        {
            CombatActionKind.MeleeAttack or CombatActionKind.WeaponParry or CombatActionKind.Damage =>
                weapon.Category == CombatWeaponCategory.Melee,
            CombatActionKind.ShieldParry => weapon.Category == CombatWeaponCategory.Shield,
            CombatActionKind.RangedAttack => weapon.Category == CombatWeaponCategory.Ranged,
            _ => true
        };
        if (!valid)
        {
            throw Validation("Waffe und Kampfaktion passen nicht zusammen.");
        }

        return weapon;
    }

    private static int? ResolveCheckValue(
        CombatActionKind action,
        CombatSetVariantDto set,
        CombatWeaponDto? weapon) => action switch
    {
        CombatActionKind.MeleeAttack => weapon?.Attack,
        CombatActionKind.WeaponParry => weapon?.Parry,
        CombatActionKind.ShieldParry => weapon?.Parry,
        CombatActionKind.Dodge => set.Dodge,
        CombatActionKind.RangedAttack => weapon?.RangedValue,
        _ => null
    };

    private static DamageExpression ParseDamageExpression(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Validation("Für diese Waffe ist keine berechnete TP-Formel importiert.");
        }

        var match = DamagePattern().Match(value.Replace(" ", string.Empty));
        if (!match.Success ||
            !int.TryParse(match.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
            !int.TryParse(match.Groups["sides"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sides))
        {
            throw Validation($"Die importierte TP-Formel „{value}“ konnte nicht ausgewertet werden.");
        }

        var modifier = 0;
        if (match.Groups["modifier"].Success)
        {
            modifier = int.Parse(match.Groups["modifier"].Value, CultureInfo.InvariantCulture);
        }

        ValidateDice(count, sides);
        return new DamageExpression(count, sides, modifier);
    }

    private static RollHistoryCheckDto[] BuildChecks(CombatRollEvaluationDto evaluation)
    {
        var checks = new List<RollHistoryCheckDto>
        {
            new(
                GetMainCheckName(evaluation.Action),
                evaluation.MainRoll,
                evaluation.EffectiveTarget ?? 0,
                Math.Max(0, evaluation.MainRoll - (evaluation.EffectiveTarget ?? 0)),
                evaluation.IsSuccessful ? RollHistoryCheckState.WithinTarget : RollHistoryCheckState.Failed)
        };

        if (evaluation.ControlRoll is { } controlRoll && evaluation.ControlTarget is { } controlTarget)
        {
            var succeeded = evaluation.Outcome is CombatOutcome.Critical or CombatOutcome.Lucky or
                CombatOutcome.Success or CombatOutcome.FumbleAvoided;
            checks.Add(new RollHistoryCheckDto(
                "Kontrolle",
                controlRoll,
                controlTarget,
                Math.Max(0, controlRoll - controlTarget),
                succeeded ? RollHistoryCheckState.WithinTarget : RollHistoryCheckState.Failed));
        }

        return checks.ToArray();
    }

    private static string[] BuildRuleNotes(CombatRollRequestDto request, CombatRollEvaluationDto? evaluation)
    {
        var notes = new List<string>();
        if (evaluation?.RequiresDefenseDecision == true)
        {
            notes.Add("Die AT ist gelungen und öffnet die Abwehrentscheidung; sie wendet keinen Treffer an.");
        }

        if (request.Modifiers is { Length: > 0 })
        {
            notes.Add("Situative Modifikatoren sind im Snapshot einzeln gespeichert.");
        }

        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            notes.Add($"Manuelle Notiz: {request.Note.Trim()}");
        }

        return notes.ToArray();
    }

    private static RollHistoryOutcome MapHistoryOutcome(CombatOutcome outcome) => outcome switch
    {
        CombatOutcome.Success => RollHistoryOutcome.Success,
        CombatOutcome.Lucky or CombatOutcome.Critical => RollHistoryOutcome.CriticalSuccess,
        CombatOutcome.Fumble => RollHistoryOutcome.Fumble,
        CombatOutcome.Failure or CombatOutcome.FumbleAvoided => RollHistoryOutcome.Failure,
        _ => RollHistoryOutcome.None
    };

    private static string GetHistoryDisplayName(CombatRollSnapshotDto snapshot)
    {
        var action = snapshot.ActionLabel ?? GetActionLabel(snapshot.Action);
        return string.IsNullOrWhiteSpace(snapshot.WeaponName)
            ? action
            : $"{action} · {snapshot.WeaponName.Trim()}";
    }

    private static string GetMainCheckName(CombatActionKind action) => action switch
    {
        CombatActionKind.MeleeAttack => "AT",
        CombatActionKind.WeaponParry or CombatActionKind.ShieldParry => "PA",
        CombatActionKind.Dodge => "AW",
        CombatActionKind.RangedAttack => "FK",
        _ => "Wurf"
    };

    private static string GetActionLabel(CombatActionKind action) => action switch
    {
        CombatActionKind.MeleeAttack => "Attacke",
        CombatActionKind.WeaponParry => "Waffenparade",
        CombatActionKind.ShieldParry => "Schildparade",
        CombatActionKind.Dodge => "Ausweichen",
        CombatActionKind.RangedAttack => "Fernkampf",
        CombatActionKind.Damage => "Trefferpunkte",
        CombatActionKind.HitZone => "Trefferzone",
        CombatActionKind.InitiativeHelper => "INI-Hilfswurf",
        CombatActionKind.WoundHelper => "Wund-Hilfswurf",
        CombatActionKind.FumbleHelper => "Patzer-Hilfswurf",
        _ => "Kampfwurf"
    };

    private static (CombatArmorZone ArmorZone, CombatWoundZone WoundZone) MapHitZone(
        int value,
        CombatZoneRollRequestDto? request)
    {
        var facing = request?.Facing ?? CombatFacing.Front;
        var shieldArm = request?.ShieldArm is CombatArmorZone.LeftArm or CombatArmorZone.RightArm
            ? request.ShieldArm
            : CombatArmorZone.LeftArm;
        var swordArm = request?.SwordArm is CombatArmorZone.LeftArm or CombatArmorZone.RightArm
            ? request.SwordArm
            : CombatArmorZone.RightArm;
        var armor = value switch
        {
            <= 6 => value % 2 == 1 ? CombatArmorZone.LeftLeg : CombatArmorZone.RightLeg,
            <= 8 => CombatArmorZone.Abdomen,
            <= 14 => value % 2 == 1 ? shieldArm : swordArm,
            <= 18 => facing == CombatFacing.Front ? CombatArmorZone.Chest : CombatArmorZone.Back,
            _ => CombatArmorZone.Head
        };
        var wound = armor switch
        {
            CombatArmorZone.Head => CombatWoundZone.Head,
            CombatArmorZone.Chest or CombatArmorZone.Back => CombatWoundZone.Torso,
            CombatArmorZone.Abdomen => CombatWoundZone.Abdomen,
            CombatArmorZone.LeftArm => CombatWoundZone.LeftArm,
            CombatArmorZone.RightArm => CombatWoundZone.RightArm,
            CombatArmorZone.LeftLeg => CombatWoundZone.LeftLeg,
            _ => CombatWoundZone.RightLeg
        };
        return (armor, wound);
    }

    private static int? GetArmorValue(CombatSetVariantDto set, CombatArmorZone zone)
    {
        var armor = set.ArmorZones;
        return zone switch
        {
            CombatArmorZone.Head => armor?.Head,
            CombatArmorZone.Chest => armor?.Chest,
            CombatArmorZone.Back => armor?.Back,
            CombatArmorZone.Abdomen => armor?.Abdomen,
            CombatArmorZone.LeftArm => armor?.LeftArm,
            CombatArmorZone.RightArm => armor?.RightArm,
            CombatArmorZone.LeftLeg => armor?.LeftLeg,
            CombatArmorZone.RightLeg => armor?.RightLeg,
            _ => null
        };
    }

    private static void ValidateRequest(CombatRollRequestDto request)
    {
        if (request.RequestId == Guid.Empty)
        {
            throw Validation("Jeder Kampfwurf braucht eine RequestId.");
        }

        if (!Enum.IsDefined(request.Action))
        {
            throw Validation("Unbekannte Kampfwurfart.");
        }

        foreach (var modifier in request.Modifiers ?? [])
        {
            if (modifier is null || string.IsNullOrWhiteSpace(modifier.Label))
            {
                throw Validation("Modifikatoren brauchen eine Bezeichnung.");
            }

            if (modifier.Value is < -999 or > 999)
            {
                throw Validation($"Modifikator {modifier.Label} liegt außerhalb des zulässigen Bereichs.");
            }
        }

        if (request.DamageModifier is < -999 or > 999)
        {
            throw Validation("Der TP-Modifikator liegt außerhalb des zulässigen Bereichs.");
        }
    }

    private static void ValidateDice(int count, int sides)
    {
        if (count is < 1 or > DiceService.MaxDicePerGroup)
        {
            throw Validation($"Es sind höchstens {DiceService.MaxDicePerGroup} Würfel erlaubt.");
        }

        if (sides is < DiceService.MinSides or > DiceService.MaxSides)
        {
            throw Validation("Die Würfelgröße ist nicht zulässig.");
        }
    }

    private static RequestRejectedException Validation(string message) =>
        new(RequestRejectionReason.Validation, message);

    [GeneratedRegex(@"(?<count>\d+)[Ww](?<sides>\d*)(?<modifier>[+-]\d+)?")]
    private static partial Regex DamagePattern();

    private sealed record DamageExpression(int DiceCount, int DiceSides, int WeaponBonus);
}
