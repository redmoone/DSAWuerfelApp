using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DsaWuerfelApp.Client.Components;

public partial class SessionTree
{
    private readonly HashSet<string> _expandedSessionIds = new(StringComparer.Ordinal);
    private string _editingName = string.Empty;
    private string _editingPlayerName = string.Empty;
    private string? _editingPlayerSessionId;
    private string? _editingPlayerUserId;
    private string? _editingSessionId;

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

    protected override void OnParametersSet()
    {
        if (!string.IsNullOrWhiteSpace(ActiveSessionId))
        {
            _expandedSessionIds.Add(ActiveSessionId);
        }

        _expandedSessionIds.RemoveWhere(sessionId =>
            Sessions.All(session => !string.Equals(session.SessionId, sessionId, StringComparison.Ordinal)));

        if (!string.IsNullOrWhiteSpace(_editingSessionId) &&
            Sessions.All(session => !string.Equals(session.SessionId, _editingSessionId, StringComparison.Ordinal)))
        {
            CancelRename();
        }

        if (!string.IsNullOrWhiteSpace(_editingPlayerSessionId) &&
            Sessions.All(session =>
                !string.Equals(session.SessionId, _editingPlayerSessionId, StringComparison.Ordinal) ||
                session.Players.All(player =>
                    !string.Equals(player.UserId, _editingPlayerUserId, StringComparison.Ordinal))))
        {
            CancelPlayerRename();
        }
    }

    private void ToggleExpanded(string sessionId)
    {
        if (!_expandedSessionIds.Add(sessionId))
        {
            _expandedSessionIds.Remove(sessionId);
        }
    }

    private bool CanManage(SessionSummaryDto session)
    {
        var currentUserId = AuthState.Current.User?.Id;
        return !string.IsNullOrWhiteSpace(currentUserId) &&
               string.Equals(session.MasterUserId, currentUserId, StringComparison.Ordinal);
    }

    private void BeginRename(SessionSummaryDto session)
    {
        _editingSessionId = session.SessionId;
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
        if (string.IsNullOrWhiteSpace(_editingName))
        {
            await JSRuntime.InvokeVoidAsync("alert", "Bitte einen Sessionnamen eingeben.");
            return;
        }

        try
        {
            await SessionState.RenameSessionAsync(sessionId, _editingName.Trim());
            CancelRename();
        }
        catch (InvalidOperationException exception)
        {
            await JSRuntime.InvokeVoidAsync("alert", exception.Message);
        }
    }

    private async Task SavePlayerRenameAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(_editingPlayerName))
        {
            await JSRuntime.InvokeVoidAsync("alert", "Bitte einen Spielernamen eingeben.");
            return;
        }

        try
        {
            await SessionState.RenamePlayerAsync(sessionId, _editingPlayerName.Trim());
            CancelPlayerRename();
        }
        catch (InvalidOperationException exception)
        {
            await JSRuntime.InvokeVoidAsync("alert", exception.Message);
        }
    }

    private async Task DeleteAsync(SessionSummaryDto session)
    {
        var confirmed = await JSRuntime.InvokeAsync<bool>(
            "confirm",
            $"Session '{session.Name}' wirklich löschen?");

        if (!confirmed)
        {
            return;
        }

        try
        {
            await SessionState.DeleteSessionAsync(session.SessionId);
        }
        catch (InvalidOperationException exception)
        {
            await JSRuntime.InvokeVoidAsync("alert", exception.Message);
        }
    }

    private async Task OpenAsync(string sessionId)
    {
        try
        {
            await SessionState.OpenSessionAsync(sessionId);
            NavigationManager.NavigateTo("/wuerfel");
        }
        catch (InvalidOperationException exception)
        {
            await JSRuntime.InvokeVoidAsync("alert", exception.Message);
        }
    }

    private async Task LeaveAsync(SessionSummaryDto session)
    {
        var isCurrentUserMaster = CanManage(session);
        var confirmationText = isCurrentUserMaster && session.Players.Length <= 1
            ? $"Session '{session.Name}' wirklich verlassen? Die Session wird dabei gelöscht."
            : isCurrentUserMaster
                ? $"Session '{session.Name}' wirklich verlassen? Die Meisterrolle geht dabei an einen anderen Spieler."
                : $"Session '{session.Name}' wirklich verlassen?";

        var confirmed = await JSRuntime.InvokeAsync<bool>("confirm", confirmationText);
        if (!confirmed)
        {
            return;
        }

        var wasActive = string.Equals(session.SessionId, ActiveSessionId, StringComparison.Ordinal);

        try
        {
            await SessionState.LeaveSessionAsync(session.SessionId);

            if (wasActive &&
                string.Equals(new Uri(NavigationManager.Uri).AbsolutePath, "/wuerfel",
                    StringComparison.OrdinalIgnoreCase))
            {
                NavigationManager.NavigateTo("/");
            }
        }
        catch (InvalidOperationException exception)
        {
            await JSRuntime.InvokeVoidAsync("alert", exception.Message);
        }
    }

    private async Task CopyJoinCodeAsync(string joinCode)
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", joinCode);
        }
        catch (JSException)
        {
            await JSRuntime.InvokeVoidAsync("alert", "Der Session-Code konnte nicht kopiert werden.");
        }
    }
}
