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
                Current = snapshot;
                _loaded = true;
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
            Current = result.Snapshot;
            Error = result.Stale ? result.Message : null;
            _loaded = true;
            Notify();
        }

        return result;
    }

    public Task<CombatSessionMutationResultDto> RollInitiativeAsync(
        string? participantId = null,
        Guid? heroId = null,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.RollInitiative,
            ParticipantId = participantId,
            HeroId = heroId
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> SetInitiativeAsync(
        int initiative,
        string? participantId = null,
        Guid? heroId = null,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.SetInitiative,
            Initiative = initiative,
            ParticipantId = participantId,
            HeroId = heroId
        }, cancellationToken);

    public Task<CombatSessionMutationResultDto> CompleteActionAsync(
        string? actionId = null,
        string? participantId = null,
        Guid? heroId = null,
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.CompleteAction,
            ActionId = actionId,
            ParticipantId = participantId,
            HeroId = heroId
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
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.AddOpponent,
            Name = name,
            InitiativeBase = initiativeBase,
            Initiative = initiative,
            Affiliation = affiliation
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
        CancellationToken cancellationToken = default) => MutateAsync(new CombatSessionMutationRequestDto
        {
            Kind = CombatSessionMutationKind.Orient,
            HasAttention = hasAttention,
            ParticipantId = participantId,
            HeroId = heroId
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

        Current = snapshot;
        _loaded = true;
        Error = null;
        Notify();
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
