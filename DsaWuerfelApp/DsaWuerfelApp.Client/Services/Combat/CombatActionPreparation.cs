using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

/// <summary>
/// Builds combat requests from an explicit session snapshot and participant
/// selection.  It has no knowledge of Razor, browser storage or transport.
/// </summary>
public static class CombatActionPreparation
{
    public static CombatActionPreparationResult PrepareAttack(
        CombatSessionSnapshotDto snapshot,
        CombatSessionParticipantDto attacker,
        CombatSessionParticipantDto target,
        CombatSessionActionDto? action,
        CombatActionKind actionKind,
        string? setId,
        string? weaponId,
        string? weaponName,
        int modifier,
        CombatFacing facing,
        CombatRuntimeStateDto? runtimeState,
        string? exchangeId,
        string? note)
    {
        if (string.IsNullOrWhiteSpace(snapshot.SessionId))
        {
            return Failure("Die Session ist für den Angriff nicht verfügbar.");
        }

        if (actionKind is not (CombatActionKind.MeleeAttack or CombatActionKind.RangedAttack))
        {
            return Failure("Die ausgewählte Aktion ist kein Angriff.");
        }

        if (action is not null && !string.Equals(action.ParticipantId, attacker.Id, StringComparison.Ordinal))
        {
            return Failure("Die vorbereitete Aktion gehört nicht zum handelnden Teilnehmer.");
        }

        if (!CombatTargetRules.IsValidTarget(attacker, target))
        {
            return Failure("Das ausgewählte Ziel ist für diesen Angriff nicht verfügbar.");
        }

        return Success(new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = snapshot.SessionId,
            ParticipantId = attacker.Id,
            TargetParticipantId = target.Id,
            ActionId = action?.Id,
            ExpectedRevision = snapshot.Revision,
            HeroId = attacker.Kind == CombatParticipantKind.Hero ? attacker.HeroId : null,
            SetId = setId,
            ExchangeId = exchangeId,
            Action = actionKind,
            WeaponId = weaponId,
            WeaponName = weaponName,
            RuntimeState = runtimeState ?? attacker.RuntimeState,
            Modifiers = BuildModifiers(modifier),
            Options = DefaultOptions,
            Facing = facing,
            Note = NormalizeNote(note)
        });
    }

    public static CombatActionPreparationResult PrepareDefense(
        CombatSessionSnapshotDto snapshot,
        CombatAttackExchangeDto exchange,
        CombatSessionParticipantDto defender,
        CombatActionKind action,
        string? setId,
        string? weaponId,
        string? weaponName,
        int modifier,
        CombatRuntimeStateDto? runtimeState,
        string? note)
    {
        if (string.IsNullOrWhiteSpace(snapshot.SessionId) || string.IsNullOrWhiteSpace(exchange.ExchangeId))
        {
            return Failure("Der offene Angriffsaustausch ist nicht verfügbar.");
        }

        if (action is not (CombatActionKind.WeaponParry or CombatActionKind.ShieldParry or CombatActionKind.Dodge))
        {
            return Failure("Die ausgewählte Aktion ist keine Abwehr.");
        }

        if (!string.Equals(exchange.TargetParticipantId, defender.Id, StringComparison.Ordinal))
        {
            return Failure("Der Abwehrer gehört nicht zum offenen Angriffsaustausch.");
        }

        if (!(exchange.AllowedDefenseActions ?? []).Contains(action))
        {
            return Failure("Diese Abwehr ist im offenen Angriffsaustausch nicht erlaubt.");
        }

        return Success(new CombatRollRequestDto
        {
            RequestId = Guid.NewGuid(),
            SessionId = snapshot.SessionId,
            ParticipantId = defender.Id,
            TargetParticipantId = exchange.AttackerParticipantId,
            ActionId = exchange.ActionId,
            ExpectedRevision = snapshot.Revision,
            HeroId = defender.Kind == CombatParticipantKind.Hero ? defender.HeroId : null,
            SetId = defender.Kind == CombatParticipantKind.Hero ? setId : null,
            ExchangeId = exchange.ExchangeId,
            Action = action,
            WeaponId = weaponId,
            WeaponName = weaponName,
            RuntimeState = runtimeState ?? defender.RuntimeState,
            Modifiers = BuildModifiers(modifier),
            Options = DefaultOptions,
            Facing = exchange.Facing,
            Note = NormalizeNote(note)
        });
    }

    private static readonly CombatRuleOptionsDto DefaultOptions =
        new(SpecialResultsEnabled: true, LowLePEnabled: true);

    private static CombatModifierDto[] BuildModifiers(int modifier) => modifier == 0
        ? []
        : [new CombatModifierDto("Situativ", modifier, "Kampfseite")];

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static CombatActionPreparationResult Success(CombatRollRequestDto request) =>
        new(request, null);

    private static CombatActionPreparationResult Failure(string message) =>
        new(null, message);
}

public sealed record CombatActionPreparationResult(
    CombatRollRequestDto? Request,
    string? Error)
{
    public bool IsSuccess => Request is not null;
}
