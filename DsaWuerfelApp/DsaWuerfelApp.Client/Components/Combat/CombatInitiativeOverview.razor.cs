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
    [Parameter] public Func<CombatSessionParticipantDto, bool>? CanManageParticipantControls { get; set; }
    [Parameter] public Func<CombatSessionParticipantDto, IReadOnlyList<CombatSessionActionDto>>? ParticipantActions { get; set; }
    [Parameter] public Func<CombatSessionActionDto, bool>? IsOrientationAction { get; set; }
    [Parameter] public EventCallback<CombatSessionParticipantDto> ConsumeReactionRequested { get; set; }
    [Parameter] public EventCallback<CombatSessionActionDto> CompleteActionRequested { get; set; }
    [Parameter] public EventCallback<CombatSessionActionDto> HoldActionRequested { get; set; }
    [Parameter] public EventCallback<CombatSessionActionDto> ExecuteHeldActionRequested { get; set; }
    [Parameter] public EventCallback<CombatSessionActionDto> ResolveOrientationRequested { get; set; }

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

    private IReadOnlyList<CombatSessionActionDto> GetVisibleActions(CombatSessionParticipantDto participant) =>
        ParticipantActions?.Invoke(participant)
            .Where(action => action.State != CombatActionEntryState.Completed)
            .Take(2)
            .ToArray() ?? Array.Empty<CombatSessionActionDto>();

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
}
