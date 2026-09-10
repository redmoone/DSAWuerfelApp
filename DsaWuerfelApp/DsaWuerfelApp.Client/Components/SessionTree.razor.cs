using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DsaWuerfelApp.Client.Components;

public partial class SessionTree
{
    private readonly HashSet<string> _expandedSessionIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionFeedback> _feedbackBySession = new(StringComparer.Ordinal);
    private readonly string _componentInstanceId = Guid.NewGuid().ToString("N");
    private string _editingName = string.Empty;
    private string _editingPlayerName = string.Empty;
    private string? _editingPlayerSessionId;
    private string? _editingPlayerUserId;
    private string? _editingSessionId;
    private string? _lastObservedActiveSessionId;
    private string? _busySessionId;
    private string? _pendingSessionId;
    private PendingSessionAction? _pendingAction;
    private bool _hasObservedActiveSession;
    private bool _pendingAutoExpandActiveSession;
    private bool _disposed;

    [Inject] public AuthState AuthState { get; set; } = null!;
    [Inject] public SessionState SessionState { get; set; } = null!;
    [Inject] public IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] public NavigationManager NavigationManager { get; set; } = null!;

    [Parameter] public IReadOnlyList<SessionSummaryDto> Sessions { get; set; } = Array.Empty<SessionSummaryDto>();
    [Parameter] public string? ActiveSessionId { get; set; }
    [Parameter] public string EmptyText { get; set; } = "Noch keine Sessions sichtbar.";
    [Parameter] public bool ShowJoinCode { get; set; } = true;
    [Parameter] public bool ShowCopyButton { get; set; }
    [Parameter] public bool ShowManagement { get; set; } = true;
    [Parameter] public bool ShowPlayerEditing { get; set; } = true;

    private bool HasBusyOperation => _busySessionId is not null;

    private bool IsBusy(string sessionId)
        => string.Equals(_busySessionId, sessionId, StringComparison.Ordinal);

    protected override void OnParametersSet()
    {
        var activeSessionChanged = !_hasObservedActiveSession ||
                                   !string.Equals(_lastObservedActiveSessionId, ActiveSessionId, StringComparison.Ordinal);
        if (activeSessionChanged)
        {
            _hasObservedActiveSession = true;
            _lastObservedActiveSessionId = ActiveSessionId;
            _pendingAutoExpandActiveSession = !string.IsNullOrWhiteSpace(ActiveSessionId);
        }

        if (_pendingAutoExpandActiveSession &&
            !string.IsNullOrWhiteSpace(ActiveSessionId) &&
            Sessions.Any(session => string.Equals(session.SessionId, ActiveSessionId, StringComparison.Ordinal)))
        {
            _expandedSessionIds.Add(ActiveSessionId);
            _pendingAutoExpandActiveSession = false;
        }

        _expandedSessionIds.RemoveWhere(sessionId =>
            Sessions.All(session => !string.Equals(session.SessionId, sessionId, StringComparison.Ordinal)));

        foreach (var sessionId in _feedbackBySession.Keys
                     .Where(sessionId => Sessions.All(session => !string.Equals(session.SessionId, sessionId, StringComparison.Ordinal)))
                     .ToArray())
        {
            _feedbackBySession.Remove(sessionId);
        }

        if (_editingSessionId is not null)
        {
            var editingSession = FindSession(_editingSessionId);
            if (editingSession is null || !ShowManagement || !CanManage(editingSession))
            {
                CancelRename();
            }
        }

        if (_editingPlayerSessionId is not null)
        {
            var editingSession = FindSession(_editingPlayerSessionId);
            var editingPlayer = editingSession?.Players.FirstOrDefault(player =>
                string.Equals(player.UserId, _editingPlayerUserId, StringComparison.Ordinal));
            if (editingPlayer is null || !CanEditPlayer(editingPlayer))
            {
                CancelPlayerRename();
            }
        }

        if (_pendingSessionId is not null)
        {
            var pendingSession = FindSession(_pendingSessionId);
            if (pendingSession is null ||
                (_pendingAction == PendingSessionAction.Delete && (!ShowManagement || !CanManage(pendingSession))))
            {
                CancelPendingAction();
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void ToggleExpanded(string sessionId)
    {
        if (!_expandedSessionIds.Add(sessionId))
        {
            _expandedSessionIds.Remove(sessionId);
        }
    }

    private void EnsureExpanded(string sessionId)
    {
        _expandedSessionIds.Add(sessionId);
    }

    private SessionSummaryDto? FindSession(string sessionId)
        => Sessions.FirstOrDefault(session => string.Equals(session.SessionId, sessionId, StringComparison.Ordinal));

    private bool CanManage(SessionSummaryDto session)
    {
        var currentUserId = AuthState.Current.User?.Id;
        return !string.IsNullOrWhiteSpace(currentUserId) &&
               string.Equals(session.MasterUserId, currentUserId, StringComparison.Ordinal);
    }

    private void BeginRename(string sessionId)
    {
        if (HasBusyOperation)
        {
            return;
        }

        var session = FindSession(sessionId);
        if (session is null || !ShowManagement || !CanManage(session))
        {
            return;
        }

        EnsureExpanded(sessionId);
        if (string.Equals(_editingSessionId, sessionId, StringComparison.Ordinal))
        {
            return;
        }

        CancelPlayerRename();
        CancelPendingAction();
        ClearFeedback(sessionId);
        _editingSessionId = sessionId;
        _editingName = session.Name;
    }

    private void CancelRename()
    {
        _editingSessionId = null;
        _editingName = string.Empty;
    }

    private bool CanEditPlayer(SessionPlayerDto player)
    {
        var currentUserId = AuthState.Current.User?.Id;
        return ShowPlayerEditing &&
               !string.IsNullOrWhiteSpace(currentUserId) &&
               string.Equals(player.UserId, currentUserId, StringComparison.Ordinal);
    }

    private void BeginPlayerRename(string sessionId, SessionPlayerDto player)
    {
        if (HasBusyOperation || !CanEditPlayer(player))
        {
            return;
        }

        var session = FindSession(sessionId);
        if (session is null || session.Players.All(candidate => !string.Equals(candidate.UserId, player.UserId, StringComparison.Ordinal)))
        {
            return;
        }

        EnsureExpanded(sessionId);
        if (string.Equals(_editingPlayerSessionId, sessionId, StringComparison.Ordinal) &&
            string.Equals(_editingPlayerUserId, player.UserId, StringComparison.Ordinal))
        {
            return;
        }

        CancelRename();
        CancelPendingAction();
        ClearFeedback(sessionId);
        _editingPlayerSessionId = sessionId;
        _editingPlayerUserId = player.UserId;
        _editingPlayerName = player.Name;
    }

    private void CancelPlayerRename()
    {
        _editingPlayerSessionId = null;
        _editingPlayerUserId = null;
        _editingPlayerName = string.Empty;
    }

    private async Task SaveRenameAsync(string sessionId)
    {
        var sessionName = _editingName.Trim();
        if (string.IsNullOrWhiteSpace(sessionName))
        {
            SetFeedback(sessionId, "Bitte einen Sessionnamen eingeben.", isError: true);
            return;
        }

        var session = FindSession(sessionId);
        if (session is null || !ShowManagement || !CanManage(session))
        {
            CancelRename();
            return;
        }

        var userId = AuthState.Current.User?.Id;
        if (!TryBeginOperation(sessionId))
        {
            return;
        }

        try
        {
            await SessionState.RenameSessionAsync(sessionId, sessionName);
            if (!IsCurrentUser(userId))
            {
                return;
            }

            CancelRename();
            SetFeedback(sessionId, "Session umbenannt.", isError: false);
        }
        catch (InvalidOperationException exception)
        {
            if (IsCurrentUser(userId))
            {
                SetFeedback(sessionId, exception.Message, isError: true);
            }
        }
        finally
        {
            EndOperation(sessionId);
        }
    }

    private async Task SavePlayerRenameAsync(string sessionId)
    {
        var playerName = _editingPlayerName.Trim();
        if (string.IsNullOrWhiteSpace(playerName))
        {
            SetFeedback(sessionId, "Bitte einen Spielernamen eingeben.", isError: true);
            return;
        }

        var session = FindSession(sessionId);
        var currentUserId = AuthState.Current.User?.Id;
        if (session is null || string.IsNullOrWhiteSpace(currentUserId) ||
            session.Players.All(player => !string.Equals(player.UserId, currentUserId, StringComparison.Ordinal)))
        {
            CancelPlayerRename();
            return;
        }

        if (!TryBeginOperation(sessionId))
        {
            return;
        }

        try
        {
            await SessionState.RenamePlayerAsync(sessionId, playerName);
            if (!IsCurrentUser(currentUserId))
            {
                return;
            }

            CancelPlayerRename();
            SetFeedback(sessionId, "Spielername geändert.", isError: false);
        }
        catch (InvalidOperationException exception)
        {
            if (IsCurrentUser(currentUserId))
            {
                SetFeedback(sessionId, exception.Message, isError: true);
            }
        }
        finally
        {
            EndOperation(sessionId);
        }
    }

    private void BeginDelete(string sessionId)
    {
        if (HasBusyOperation)
        {
            return;
        }

        var session = FindSession(sessionId);
        if (session is null || !ShowManagement || !CanManage(session))
        {
            return;
        }

        CancelRename();
        CancelPlayerRename();
        ClearFeedback(sessionId);
        EnsureExpanded(sessionId);
        _pendingSessionId = sessionId;
        _pendingAction = PendingSessionAction.Delete;
    }

    private void BeginLeave(string sessionId)
    {
        if (HasBusyOperation)
        {
            return;
        }

        var session = FindSession(sessionId);
        if (session is null)
        {
            return;
        }

        CancelRename();
        CancelPlayerRename();
        ClearFeedback(sessionId);
        EnsureExpanded(sessionId);
        _pendingSessionId = sessionId;
        _pendingAction = PendingSessionAction.Leave;
    }

    private void CancelPendingAction()
    {
        _pendingSessionId = null;
        _pendingAction = null;
    }

    private async Task ConfirmPendingActionAsync()
    {
        if (_pendingSessionId is null || _pendingAction is null)
        {
            return;
        }

        var sessionId = _pendingSessionId;
        var action = _pendingAction.Value;
        var session = FindSession(sessionId);
        if (session is null || (action == PendingSessionAction.Delete && (!ShowManagement || !CanManage(session))))
        {
            CancelPendingAction();
            return;
        }

        var userId = AuthState.Current.User?.Id;
        if (!TryBeginOperation(sessionId))
        {
            return;
        }

        var wasActive = string.Equals(sessionId, ActiveSessionId, StringComparison.Ordinal);
        try
        {
            if (action == PendingSessionAction.Delete)
            {
                await SessionState.DeleteSessionAsync(sessionId);
            }
            else
            {
                await SessionState.LeaveSessionAsync(sessionId);
            }

            if (!IsCurrentUser(userId))
            {
                return;
            }

            CancelPendingAction();
            if (wasActive &&
                string.Equals(new Uri(NavigationManager.Uri).AbsolutePath, "/wuerfel", StringComparison.OrdinalIgnoreCase))
            {
                NavigationManager.NavigateTo("/");
            }
        }
        catch (InvalidOperationException exception)
        {
            if (IsCurrentUser(userId))
            {
                SetFeedback(sessionId, exception.Message, isError: true);
            }
        }
        finally
        {
            EndOperation(sessionId);
        }
    }

    private async Task OpenAsync(string sessionId)
    {
        var session = FindSession(sessionId);
        if (session is null || HasBusyOperation)
        {
            return;
        }

        var userId = AuthState.Current.User?.Id;
        if (string.Equals(sessionId, ActiveSessionId, StringComparison.Ordinal))
        {
            if (IsCurrentUser(userId))
            {
                NavigationManager.NavigateTo("/wuerfel");
            }

            return;
        }

        if (!TryBeginOperation(sessionId))
        {
            return;
        }

        try
        {
            await SessionState.OpenSessionAsync(sessionId);
            if (IsCurrentUser(userId))
            {
                NavigationManager.NavigateTo("/wuerfel");
            }
        }
        catch (InvalidOperationException exception)
        {
            if (IsCurrentUser(userId))
            {
                SetFeedback(sessionId, exception.Message, isError: true);
            }
        }
        finally
        {
            EndOperation(sessionId);
        }
    }

    private async Task CopyJoinCodeAsync(string sessionId)
    {
        var session = FindSession(sessionId);
        if (session is null || HasBusyOperation)
        {
            return;
        }

        var userId = AuthState.Current.User?.Id;
        if (!TryBeginOperation(sessionId))
        {
            return;
        }

        try
        {
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", session.JoinCode);
            if (IsCurrentUser(userId))
            {
                SetFeedback(sessionId, "Code kopiert.", isError: false);
            }
        }
        catch (JSException)
        {
            if (IsCurrentUser(userId))
            {
                SetFeedback(sessionId, $"Der Code {session.JoinCode} konnte nicht kopiert werden. Bitte manuell kopieren.", isError: true);
            }
        }
        finally
        {
            EndOperation(sessionId);
        }
    }

    private bool TryBeginOperation(string sessionId)
    {
        if (_disposed || HasBusyOperation)
        {
            return false;
        }

        _busySessionId = sessionId;
        return true;
    }

    private void EndOperation(string sessionId)
    {
        if (string.Equals(_busySessionId, sessionId, StringComparison.Ordinal))
        {
            _busySessionId = null;
        }
    }

    private bool IsCurrentUser(string? userId)
        => !_disposed &&
           AuthState.Current.IsAuthenticated &&
           !string.IsNullOrWhiteSpace(userId) &&
           string.Equals(userId, AuthState.Current.User?.Id, StringComparison.Ordinal);

    private SessionFeedback? GetFeedback(string sessionId)
        => _feedbackBySession.GetValueOrDefault(sessionId);

    private void SetFeedback(string sessionId, string message, bool isError)
    {
        if (_disposed)
        {
            return;
        }

        _feedbackBySession[sessionId] = new SessionFeedback(message, isError);
    }

    private void ClearFeedback(string sessionId)
    {
        _feedbackBySession.Remove(sessionId);
    }

    private PendingSessionAction? GetPendingAction(string sessionId)
        => string.Equals(_pendingSessionId, sessionId, StringComparison.Ordinal) ? _pendingAction : null;

    private string GetPendingMessage(SessionSummaryDto session, PendingSessionAction action)
        => action == PendingSessionAction.Delete
            ? $"Session '{session.Name}' wirklich löschen? Alle Sessiondaten werden entfernt."
            : CanManage(session) && session.Players.Length <= 1
                ? $"Session '{session.Name}' wirklich verlassen? Die Session wird dabei gelöscht."
                : CanManage(session)
                    ? $"Session '{session.Name}' wirklich verlassen? Die Meisterrolle geht dabei an einen anderen Spieler."
                    : $"Session '{session.Name}' wirklich verlassen?";

    private string GetSessionNodeId(string sessionId)
        => $"{_componentInstanceId}-session-{SanitizeId(sessionId)}";

    private string GetDetailsId(string sessionId)
        => $"{GetSessionNodeId(sessionId)}-details";

    private string GetManagementId(string sessionId)
        => $"{GetSessionNodeId(sessionId)}-management";

    private string GetSessionEditorId(string sessionId)
        => $"{GetSessionNodeId(sessionId)}-editor";

    private string GetPlayerEditorId(string sessionId, string userId)
        => $"{GetSessionNodeId(sessionId)}-player-{SanitizeId(userId)}";

    private static string SanitizeId(string value)
        => string.Concat(value.Select(character => char.IsLetterOrDigit(character) ? character : '-'));

    private enum PendingSessionAction
    {
        Leave,
        Delete
    }

    private sealed record SessionFeedback(string Message, bool IsError);
}
