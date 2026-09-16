using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components.Combat;

public partial class CombatInitiativeOverview
{
    [Parameter] public bool IsSessionCombat { get; set; }
    [Parameter] public CombatSessionSnapshotDto? SessionCombat { get; set; }
    [Parameter] public string HeroName { get; set; } = "Kein aktiver Held";
    [Parameter] public int? CurrentInitiative { get; set; }
    [Parameter] public int? InitiativeBase { get; set; }
    [Parameter] public IReadOnlyList<CombatSessionParticipantDto> Participants { get; set; } = Array.Empty<CombatSessionParticipantDto>();
    [Parameter] public Func<CombatSessionParticipantDto, string>? TurnLabel { get; set; }
    [Parameter] public Func<CombatSessionParticipantDto, bool>? IsCurrentParticipant { get; set; }
    [Parameter] public Func<CombatSessionParticipantDto, string?>? ParticipantStatus { get; set; }
    [Parameter] public Func<CombatSessionParticipantDto, bool>? CanRollParticipantInitiative { get; set; }
    [Parameter] public EventCallback<CombatSessionParticipantDto> RollInitiativeRequested { get; set; }
    [Parameter] public Func<CombatSessionParticipantDto, bool>? CanRemoveOpponent { get; set; }
    [Parameter] public EventCallback<CombatSessionParticipantDto> RemoveOpponentRequested { get; set; }
    [Parameter] public bool ShowTargetSelection { get; set; }
    [Parameter] public string? SelectedTargetParticipantId { get; set; }
    [Parameter] public Func<CombatSessionParticipantDto, bool>? CanSelectTarget { get; set; }
    [Parameter] public EventCallback<string> TargetSelected { get; set; }

    private string GetTurnLabel(CombatSessionParticipantDto participant) =>
        TurnLabel?.Invoke(participant) ?? "Offen";

    private string? GetParticipantStatus(CombatSessionParticipantDto participant) =>
        ParticipantStatus?.Invoke(participant);

    private bool IsTargetSelectable(CombatSessionParticipantDto participant) =>
        ShowTargetSelection && (CanSelectTarget?.Invoke(participant) ?? false);

    private bool IsSelectedTarget(CombatSessionParticipantDto participant) =>
        IsTargetSelectable(participant) &&
        string.Equals(SelectedTargetParticipantId, participant.Id, StringComparison.Ordinal);

    private string GetTargetClass(CombatSessionParticipantDto participant) =>
        $"combat-initiative-overview-person combat-initiative-overview-target {(IsSelectedTarget(participant) ? "is-target" : string.Empty)}";

    private static string GetTurnClass(CombatSessionParticipantDto participant, string? label = null) =>
        (label ?? string.Empty) switch
        {
            "Jetzt" => "turn-now",
            "Als Nächstes" => "turn-next",
            "Wartet" => "turn-held",
            "Bereits gehandelt" => "turn-completed",
            "INI fehlt" => "turn-unknown",
            _ => participant.CurrentInitiative.HasValue ? "turn-open" : "turn-unknown"
        };

    private string GetTurnClass(CombatSessionParticipantDto participant) =>
        GetTurnClass(participant, GetTurnLabel(participant));

    private static string FormatInitiative(int? initiative) => initiative?.ToString() ?? "INI —";

    private static string GetAffiliation(CombatSessionParticipantDto participant) =>
        string.IsNullOrWhiteSpace(participant.Affiliation)
            ? participant.Kind == CombatParticipantKind.Hero ? "Held" : "Gegner"
            : participant.Affiliation;

    private static string GetParticipantName(
        CombatSessionSnapshotDto snapshot,
        string participantId) => snapshot.Participants.FirstOrDefault(participant =>
            string.Equals(participant.Id, participantId, StringComparison.Ordinal))?.Name ?? "Unbekannt";

    private static string GetExchangeStatusLabel(CombatExchangeStatus status) => status switch
    {
        CombatExchangeStatus.Declared => "AT-Wurf steht aus",
        CombatExchangeStatus.AttackOpen => "AT-Wurf läuft",
        CombatExchangeStatus.DefenseOpen => "Abwehr wählen",
        CombatExchangeStatus.Hit => "Trefferfolge offen",
        CombatExchangeStatus.DamageOpen => "TP-Wurf steht aus",
        _ => status.ToString()
    };

    private static string GetExchangePrompt(
        CombatSessionSnapshotDto snapshot,
        CombatAttackExchangeDto exchange) => exchange.Status switch
    {
        CombatExchangeStatus.Declared or CombatExchangeStatus.AttackOpen =>
            $"{GetParticipantName(snapshot, exchange.AttackerParticipantId)} würfelt die Attacke.",
        CombatExchangeStatus.DefenseOpen =>
            $"{GetParticipantName(snapshot, exchange.TargetParticipantId)} wählt die Abwehr.",
        CombatExchangeStatus.Hit or CombatExchangeStatus.DamageOpen =>
            $"Trefferfolge von {GetParticipantName(snapshot, exchange.AttackerParticipantId)} abschließen.",
        _ => exchange.RuleNote ?? string.Empty
    };

}
