using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class CombatSessionState : IDisposable
{
    private readonly AuthState _authState;
    private readonly GameClient _gameClient;
    private readonly SessionState _sessionState;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _disposed;
    private bool _loaded;

    public CombatSessionState(AuthState authState, GameClient gameClient, SessionState sessionState)
    {
        _authState = authState;
        _gameClient = gameClient;
        _sessionState = sessionState;
        _gameClient.OnCombatSessionStateReceived += HandleStateReceived;
        _gameClient.SessionChanged += HandleContextChanged;
        _sessionState.ActiveSessionChanged += HandleContextChanged;
        _authState.Changed += HandleAuthChanged;
    }

    public CombatSessionSnapshotDto? Current { get; private set; }
    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public bool CanUndo => Current is { UndoAvailable: true, LastMutationUserId: not null } snapshot &&
                           string.Equals(snapshot.LastMutationUserId, _authState.Current.User?.Id,
                               StringComparison.Ordinal);

    public event Action? Changed;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        var sessionId = _sessionState.ActiveSessionId;
        if (!_authState.Current.IsAuthenticated || string.IsNullOrWhiteSpace(sessionId))
        {
            Clear();
            return;
        }

        if (_loaded && string.Equals(Current?.SessionId, sessionId, StringComparison.Ordinal))
        {
            return;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_loaded && string.Equals(Current?.SessionId, sessionId, StringComparison.Ordinal))
            {
                return;
            }

            IsLoading = true;
            Error = null;
            Notify();
            await _gameClient.StartAsync();
            var snapshot = await _gameClient.GetCombatSessionState(sessionId);
            if (string.Equals(_sessionState.ActiveSessionId, sessionId, StringComparison.Ordinal))
            {
                ApplySnapshot(snapshot);
            }
        }
        catch (Exception exception) when (!_disposed)
        {
            Error = exception.Message;
            _loaded = false;
        }
        finally
        {
            IsLoading = false;
            Notify();
            _loadLock.Release();
        }
    }

    public async Task<CombatSessionMutationResultDto> MutateAsync(
        CombatSessionMutationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        var sessionId = _sessionState.ActiveSessionId;
        if (string.IsNullOrWhiteSpace(sessionId) || Current is null)
        {
            throw new InvalidOperationException("Für die Kampfverwaltung ist keine aktive Session geladen.");
        }

        var prepared = request with
        {
            RequestId = request.RequestId == Guid.Empty ? Guid.NewGuid() : request.RequestId,
            SessionId = sessionId,
            ExpectedRevision = request.ExpectedRevision ?? Current.Revision
        };
        var result = await _gameClient.MutateCombatSession(prepared);
        if (string.Equals(result.Snapshot.SessionId, sessionId, StringComparison.Ordinal))
        {
            ApplySnapshot(result.Snapshot);
            Error = result.Stale ? result.Message : null;
            Notify();
        }

        return result;
    }

    public Task<CombatSessionMutationResultDto> RollInitiativeAsync(
        string? participantId = null,
        Guid? heroId = null,
        CombatRuntimeStateDto? runtimeState = null,
        string? setId = null,
        int initiativeCorrection = 0,
        CancellationToken cancellationToken = default,
        long? expectedRevision = null) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.RollInitiative,
            ParticipantId = participantId,
            HeroId = heroId,
            RuntimeState = runtimeState,
            SetId = setId,
            InitiativeCorrection = initiativeCorrection,
            ExpectedRevision = expectedRevision
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> SetInitiativeAsync(
        int initiative,
        string? participantId = null,
        Guid? heroId = null,
        CombatRuntimeStateDto? runtimeState = null,
        string? setId = null,
        CancellationToken cancellationToken = default,
        long? expectedRevision = null) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.SetInitiative,
            Initiative = initiative,
            ParticipantId = participantId,
            HeroId = heroId,
            RuntimeState = runtimeState,
            SetId = setId,
            ExpectedRevision = expectedRevision
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> SyncRuntimeStateAsync(
        CombatRuntimeStateDto runtimeState,
        string? participantId = null,
        Guid? heroId = null,
        string? setId = null,
        CancellationToken cancellationToken = default,
        long? expectedRevision = null) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.SyncRuntimeState,
            RuntimeState = runtimeState,
            ParticipantId = participantId,
            HeroId = heroId,
            SetId = setId,
            ExpectedRevision = expectedRevision
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> DeclareAttackAsync(
        string participantId,
        string targetParticipantId,
        string actionId,
        CombatActionKind action,
        string? exchangeId = null,
        string? setId = null,
        string? weaponId = null,
        string? weaponName = null,
        int? phaseInitiative = null,
        Guid? heroId = null,
        CombatFacing facing = CombatFacing.Front,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.DeclareAttack,
            ParticipantId = participantId,
            TargetParticipantId = targetParticipantId,
            ActionId = actionId,
            ActionKind = action,
            ExchangeId = string.IsNullOrWhiteSpace(exchangeId) ? Guid.NewGuid().ToString("N") : exchangeId,
            SetId = setId,
            WeaponId = weaponId,
            WeaponName = weaponName,
            PhaseInitiative = phaseInitiative,
            HeroId = heroId,
            Facing = facing
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> CompleteActionAsync(
        string? actionId = null,
        string? participantId = null,
        Guid? heroId = null,
        CombatRuntimeStateDto? runtimeState = null,
        string? setId = null,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.CompleteAction,
            ActionId = actionId,
            ParticipantId = participantId,
            HeroId = heroId,
            RuntimeState = runtimeState,
            SetId = setId
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> ConsumeReactionAsync(
        string? participantId = null,
        Guid? heroId = null,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.ConsumeReaction,
            ParticipantId = participantId,
            HeroId = heroId
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> HoldActionAsync(
        string? actionId = null,
        string? participantId = null,
        Guid? heroId = null,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.HoldAction,
            ActionId = actionId,
            ParticipantId = participantId,
            HeroId = heroId
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> ExecuteHeldActionAsync(
        string? actionId = null,
        string? participantId = null,
        Guid? heroId = null,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.ExecuteHeldAction,
            ActionId = actionId,
            ParticipantId = participantId,
            HeroId = heroId
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> AddOpponentAsync(
        string name,
        int? initiativeBase,
        int? initiative,
        string? affiliation,
        CombatOpponentProfileDto? opponentProfile = null,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = name,
            InitiativeBase = initiativeBase,
            Initiative = initiative,
            Affiliation = affiliation,
            OpponentProfile = opponentProfile
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> AddCatalogEnemyAsync(
        CombatEnemySelectionDto selection,
        string? name = null,
        int? initiative = null,
        string? affiliation = null,
        int initiativeCorrection = 0,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = name,
            Initiative = initiative,
            InitiativeCorrection = initiativeCorrection,
            Affiliation = affiliation,
            EnemySelection = selection
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> RemoveOpponentAsync(
        string participantId,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.RemoveOpponent,
            ParticipantId = participantId
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> NewRoundAsync(
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.NewRound
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> UndoAsync(
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.Undo
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> SetAnnouncementAsync(
        string announcement,
        string? participantId = null,
        Guid? heroId = null,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.SetAnnouncement,
            Announcement = announcement,
            ParticipantId = participantId,
            HeroId = heroId
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> OrientAsync(
        bool hasAttention,
        string? participantId = null,
        Guid? heroId = null,
        int? orientationRelief = null,
        bool orientationUninterrupted = true,
        CombatRuntimeStateDto? runtimeState = null,
        string? setId = null,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.Orient,
            HasAttention = hasAttention,
            OrientationRelief = orientationRelief,
            OrientationUninterrupted = orientationUninterrupted,
            ParticipantId = participantId,
            HeroId = heroId,
            RuntimeState = runtimeState,
            SetId = setId
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> ResolveOrientationAsync(
        string actionId,
        string? participantId = null,
        Guid? heroId = null,
        CombatRuntimeStateDto? runtimeState = null,
        string? setId = null,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.ResolveOrientation,
            ActionId = actionId,
            ParticipantId = participantId,
            HeroId = heroId,
            RuntimeState = runtimeState,
            SetId = setId
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> AddActionAsync(
        string label,
        int actionCost,
        int? phaseInitiative,
        bool isReaction,
        CombatActionKind? actionKind = null,
        string? participantId = null,
        Guid? heroId = null,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.AddAction,
            Label = label,
            ActionCost = actionCost,
            PhaseInitiative = phaseInitiative,
            IsReaction = isReaction,
            ActionKind = actionKind,
            ParticipantId = participantId,
            HeroId = heroId
        }, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gameClient.OnCombatSessionStateReceived -= HandleStateReceived;
        _gameClient.SessionChanged -= HandleContextChanged;
        _sessionState.ActiveSessionChanged -= HandleContextChanged;
        _authState.Changed -= HandleAuthChanged;
        _loadLock.Dispose();
    }

    private void HandleStateReceived(CombatSessionSnapshotDto snapshot)
    {
        if (!string.Equals(snapshot.SessionId, _sessionState.ActiveSessionId, StringComparison.Ordinal))
        {
            return;
        }

        if (!ApplySnapshot(snapshot))
        {
            return;
        }

        Error = null;
        Notify();
    }

    private bool ApplySnapshot(CombatSessionSnapshotDto snapshot)
    {
        if (Current is not null && snapshot.Revision < Current.Revision)
        {
            return false;
        }

        Current = snapshot;
        _loaded = true;
        return true;
    }

    private void HandleContextChanged() => _ = EnsureLoadedAsync();

    private void HandleAuthChanged()
    {
        if (!_authState.Current.IsAuthenticated)
        {
            Clear();
            return;
        }

        _ = EnsureLoadedAsync();
    }

    private void Clear()
    {
        if (Current is null && !_loaded && Error is null)
        {
            return;
        }

        Current = null;
        Error = null;
        _loaded = false;
        Notify();
    }

    private void Notify() => Changed?.Invoke();
}
