using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Pages;

public partial class Lobby : IDisposable
{
    private string _authMessage = "";
    private string _email = "";
    private string _error = "";
    private bool _isSendingMagicLink;
    private bool _isSigningOut;
    private string _joinCode = "";
    private string _sessionName = "";
    private string _userName = "";
    private SessionMode CurrentMode { get; set; } = SessionMode.Join;

    [Inject] public AuthState AuthState { get; set; } = null!;
    [Inject] public IAuthApiClient AuthApi { get; set; } = null!;
    [Inject] public GameClient Game { get; set; } = null!;
    [Inject] public SessionState SessionState { get; set; } = null!;
    [Inject] public NavigationManager Nav { get; set; } = null!;

    private bool IsAuthenticated => AuthState.Current.IsAuthenticated;
    private AuthUserDto? CurrentUser => AuthState.Current.User;

    public void Dispose()
    {
        AuthState.Changed -= HandleAuthChanged;
        SessionState.Changed -= HandleSessionStateChanged;
    }

    protected override async Task OnInitializedAsync()
    {
        AuthState.Changed += HandleAuthChanged;
        SessionState.Changed += HandleSessionStateChanged;
        ApplyAuthQueryFeedback();
        await AuthState.EnsureLoadedAsync();
        await SessionState.EnsureLoadedAsync();
        ApplyAuthenticatedDefaults();
        await EnsureGameConnectionAsync();
    }

    private async Task Join()
    {
        if (!await EnsureAuthenticatedAsync())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_userName))
        {
            _error = "Bitte Namen eingeben";
            return;
        }

        if (string.IsNullOrWhiteSpace(_joinCode))
        {
            _error = "Bitte Session-Code eingeben";
            return;
        }

        _error = string.Empty;
        try
        {
            var success = await SessionState.JoinSessionAsync(_joinCode.ToUpper(), _userName.Trim());
            if (success)
            {
                Nav.NavigateTo("/wuerfel");
            }
            else
            {
                _error = "Session nicht gefunden oder Code falsch.";
            }
        }
        catch (InvalidOperationException exception)
        {
            _error = exception.Message;
        }
    }

    private async Task Create()
    {
        if (!await EnsureAuthenticatedAsync())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_userName))
        {
            _error = "Bitte Namen eingeben";
            return;
        }

        _error = string.Empty;
        var sessionName = string.IsNullOrWhiteSpace(_sessionName) ? BuildDefaultSessionName() : _sessionName.Trim();

        try
        {
            await SessionState.CreateSessionAsync(_userName.Trim(), sessionName);
            Nav.NavigateTo("/wuerfel");
        }
        catch (InvalidOperationException exception)
        {
            _error = exception.Message;
        }
    }

    private async Task RequestMagicLink()
    {
        if (string.IsNullOrWhiteSpace(_email))
        {
            _error = "Bitte Email eingeben";
            return;
        }

        _isSendingMagicLink = true;
        _error = string.Empty;
        _authMessage = string.Empty;

        try
        {
            var result = await AuthApi.RequestMagicLinkAsync(new MagicLinkRequestDto(_email, "/"));
            _authMessage = result.Message;
        }
        catch (InvalidOperationException exception)
        {
            _error = exception.Message;
        }
        finally
        {
            _isSendingMagicLink = false;
        }
    }

    private async Task Logout()
    {
        _isSigningOut = true;
        _error = string.Empty;
        _authMessage = string.Empty;

        try
        {
            await Game.DisconnectAsync();
            await AuthState.LogoutAsync();
            _userName = string.Empty;
            _joinCode = string.Empty;
            _sessionName = string.Empty;
            CurrentMode = SessionMode.Join;
        }
        finally
        {
            _isSigningOut = false;
        }
    }

    private void SetMode(SessionMode mode)
    {
        CurrentMode = mode;
        _error = string.Empty;

        if (mode == SessionMode.Create && string.IsNullOrWhiteSpace(_sessionName))
        {
            _sessionName = BuildDefaultSessionName();
        }
    }

    private string GetModeClass(SessionMode mode)
    {
        return CurrentMode == mode ? "active" : string.Empty;
    }

    private async Task<bool> EnsureAuthenticatedAsync()
    {
        if (!IsAuthenticated)
        {
            _error = "Bitte zuerst per Magic Link anmelden.";
            return false;
        }

        return await EnsureGameConnectionAsync();
    }

    private async Task<bool> EnsureGameConnectionAsync()
    {
        if (IsAuthenticated)
        {
            try
            {
                await Game.StartAsync();
                return true;
            }
            catch
            {
                _error = "Gamesession-Verbindung konnte nicht aufgebaut werden.";
                return false;
            }
        }

        await Game.DisconnectAsync();
        return false;
    }

    private void ApplyAuthenticatedDefaults()
    {
        if (string.IsNullOrWhiteSpace(_userName) && !string.IsNullOrWhiteSpace(CurrentUser?.DisplayName))
        {
            _userName = CurrentUser.DisplayName;
        }

        if (string.IsNullOrWhiteSpace(_sessionName) && !string.IsNullOrWhiteSpace(_userName))
        {
            _sessionName = BuildDefaultSessionName();
        }
    }

    private void ApplyAuthQueryFeedback()
    {
        var authStatus = GetQueryParameterValue("auth");
        if (string.IsNullOrWhiteSpace(authStatus))
        {
            return;
        }

        if (string.Equals(authStatus, "invalid", StringComparison.OrdinalIgnoreCase))
        {
            _error = "Magic Link ist ungueltig oder abgelaufen. Bitte einen neuen Link anfordern.";
        }
    }

    private void HandleAuthChanged()
    {
        _ = InvokeAsync(async () =>
        {
            ApplyAuthenticatedDefaults();
            await SessionState.RefreshAsync();
            await EnsureGameConnectionAsync();
            StateHasChanged();
        });
    }

    private void HandleSessionStateChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private string BuildDefaultSessionName()
    {
        var baseName = string.IsNullOrWhiteSpace(_userName)
            ? CurrentUser?.DisplayName
            : _userName;

        return string.IsNullOrWhiteSpace(baseName)
            ? "Neue Runde"
            : $"{baseName.Trim()}s Runde";
    }

    private string? GetQueryParameterValue(string key)
    {
        var query = new Uri(Nav.Uri).Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var segments = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex < 0)
            {
                if (string.Equals(segment, key, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                continue;
            }

            var currentKey = Uri.UnescapeDataString(segment[..separatorIndex]);
            if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.UnescapeDataString(segment[(separatorIndex + 1)..]);
        }

        return null;
    }

    private enum SessionMode
    {
        Join,
        Create
    }
}