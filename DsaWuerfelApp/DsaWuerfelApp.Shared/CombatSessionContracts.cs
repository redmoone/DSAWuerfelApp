namespace DsaWuerfelApp.Shared;

public enum CombatParticipantKind
{
    Hero,
    Opponent
}

public sealed record CombatOpponentProfileDto(
    int? Attack,
    int? Parry,
    int? Dodge,
    int? ArmorRating,
    int? LeP,
    int? WoundThreshold = null)
{
    public CombatEnemyResolvedProfileDto? CatalogProfile { get; init; }

    public bool HasBasicCombatValues =>
        Attack.HasValue &&
        (Parry.HasValue || Dodge.HasValue) &&
        ArmorRating.HasValue &&
        LeP.HasValue;
}

public sealed record CombatActionBudgetDto
{
    public int NormalActionsRemaining { get; init; } = 1;
    public int ReactionsRemaining { get; init; } = 1;
    public bool FreeActionAvailable { get; init; } = true;
    public string? HeldActionId { get; init; }
    public int? HeldActionRound { get; init; }

    public bool HasNormalAction => NormalActionsRemaining > 0;
    public bool HasReaction => ReactionsRemaining > 0;

    public CombatActionBudgetDto ConsumeNormalAction(int cost = 1) => this with
    {
        NormalActionsRemaining = Math.Max(0, NormalActionsRemaining - Math.Max(1, cost)),
        HeldActionId = null,
        HeldActionRound = null
    };

    public CombatActionBudgetDto ConsumeReaction() => this with
    {
        ReactionsRemaining = Math.Max(0, ReactionsRemaining - 1)
    };

    public CombatActionBudgetDto ResetForRound() => this with
    {
        NormalActionsRemaining = 1,
        ReactionsRemaining = 1,
        FreeActionAvailable = true,
        HeldActionId = null,
        HeldActionRound = null
    };
}

public enum CombatActionEntryState
{
    Open,
    Held,
    Completed
}

public enum CombatSessionMutationKind
{
    RollInitiative,
    SetInitiative,
    SyncRuntimeState,
    DeclareAttack,
    CompleteAction,
    ConsumeReaction,
    HoldAction,
    ExecuteHeldAction,
    AddAction,
    AddOpponent,
    NewRound,
    Undo,
    SetAnnouncement,
    Orient,
    ResolveOrientation,
    RemoveOpponent
}

public sealed record CombatSessionParticipantDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public CombatParticipantKind Kind { get; init; }
    public Guid? HeroId { get; init; }
    public string? OwnerUserId { get; init; }
    public string? Affiliation { get; init; }
    public CombatOpponentProfileDto? OpponentProfile { get; init; }
    public CombatActionBudgetDto? ActionBudget { get; init; }
    public int? InitiativeBase { get; init; }
    public string? InitiativeSetId { get; init; }
    public int? StartRoll { get; init; }
    public int InitiativeDiceCount { get; init; } = 1;
    public int InitiativeCorrection { get; init; }
    public int InitiativeRuntimeModifier { get; init; }
    public string[] InitiativeRuntimeNotes { get; init; } = [];
    public CombatRuntimeStateDto? RuntimeState { get; init; }
    public int RecoverableInitiativeLoss { get; init; }
    public int? CurrentInitiative { get; init; }
    public bool IsOnline { get; init; }
    public bool ActionAvailable { get; init; }
    public bool ReactionAvailable { get; init; }
    public string? Announcement { get; init; }
    public bool IsOriented { get; init; }
}

public sealed record CombatSessionActionDto
{
    public string Id { get; init; } = string.Empty;
    public string ParticipantId { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public CombatActionKind? ActionKind { get; init; }
    public int Round { get; init; }
    public int? PhaseInitiative { get; init; }
    public int ActionCost { get; init; } = 1;
    public bool IsReaction { get; init; }
    public bool IsAdditional { get; init; }
    public bool RequiresCheck { get; init; }
    public int OrientationRelief { get; init; }
    public bool OrientationUninterrupted { get; init; } = true;
    public CombatActionEntryState State { get; init; } = CombatActionEntryState.Open;
    public string? Announcement { get; init; }
    public bool IsCatchUp { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed record CombatSessionSnapshotDto
{
    public string SessionId { get; init; } = string.Empty;
    public long Revision { get; init; }
    public int Round { get; init; } = 1;
    public bool IsStarted { get; init; }
    public bool SpecialResultsEnabled { get; init; } = true;
    public bool AnnouncementEnabled { get; init; }
    public string? CurrentActionId { get; init; }
    public string[] CurrentActionIds { get; init; } = [];
    public CombatSessionParticipantDto[] Participants { get; init; } = [];
    public CombatSessionActionDto[] Actions { get; init; } = [];
    public Guid? LastMutationId { get; init; }
    public string? LastMutationDescription { get; init; }
    public string? LastMutationUserId { get; init; }
    public bool UndoAvailable { get; init; }
    public CombatAttackExchangeDto? ActiveExchange { get; init; }
}

public sealed record CombatSessionMutationRequestDto
{
    public Guid RequestId { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public long? ExpectedRevision { get; init; }
    public CombatSessionMutationKind Kind { get; init; }
    public string? ParticipantId { get; init; }
    public string? TargetParticipantId { get; init; }
    public string? ExchangeId { get; init; }
    public string? ActionId { get; init; }
    public string? Name { get; init; }
    public Guid? HeroId { get; init; }
    public string? SetId { get; init; }
    public string? WeaponId { get; init; }
    public string? WeaponName { get; init; }
    public string? Affiliation { get; init; }
    public CombatOpponentProfileDto? OpponentProfile { get; init; }
    public CombatEnemySelectionDto? EnemySelection { get; init; }
    public int? InitiativeBase { get; init; }
    public int? Initiative { get; init; }
    public int InitiativeCorrection { get; init; }
    public string? Label { get; init; }
    public CombatActionKind? ActionKind { get; init; }
    public int ActionCost { get; init; } = 1;
    public int? PhaseInitiative { get; init; }
    public bool IsReaction { get; init; }
    public bool HasAttention { get; init; }
    public int? OrientationRelief { get; init; }
    public bool OrientationUninterrupted { get; init; } = true;
    public string? Announcement { get; init; }
    public CombatRuntimeStateDto? RuntimeState { get; init; }
}

public sealed record CombatSessionMutationResultDto(
    Guid RequestId,
    bool Applied,
    bool AlreadyApplied,
    bool Stale,
    string Message,
    CombatSessionSnapshotDto Snapshot)
{
    public DiceRollDto[] Rolls { get; init; } = [];
    public RollHistoryEntryDto? HistoryEntry { get; init; }
}
