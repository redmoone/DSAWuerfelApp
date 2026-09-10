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
    private bool _isSubmittingSession;
    private bool _isSigningOut;
    private bool _isUserNameDirty;
    private string _joinCode = "";
    private CancellationTokenSource? _magicLinkCooldownCancellation;
    private DateTimeOffset? _magicLinkCooldownEndsAtUtc;
    private string _sessionName = "";
    private string _userName = "";
    private string? _lastAppliedActiveSessionId;
    private string? _lastAppliedUserId;
    private bool _hasAppliedUserContext;
    private bool _disposed;

    [SupplyParameterFromQuery(Name = "auth")]
    private string? AuthStatus { get; set; }

    private SessionMode CurrentMode { get; set; } = SessionMode.Join;

    [Inject] public AuthState AuthState { get; set; } = null!;
    [Inject] public IAuthApiClient AuthApi { get; set; } = null!;
    [Inject] public GameClient Game { get; set; } = null!;
    [Inject] public SessionState SessionState { get; set; } = null!;
    [Inject] public NavigationManager Nav { get; set; } = null!;

    private bool IsAuthenticated => AuthState.Current.IsAuthenticated;
    private AuthUserDto? CurrentUser => AuthState.Current.User;
    private SessionDetailsDto? ActiveSession => SessionState.ActiveSession;
    private int SessionCount => SessionState.Sessions.Count;
    private string? ResolvedPlayerName => ResolvePlayerName();
    private string NormalizedJoinCode => _joinCode.Trim().ToUpperInvariant();
    private bool CanJoinSession => IsAuthenticated &&
                                    !_isSubmittingSession &&
                                    !string.IsNullOrWhiteSpace(ResolvedPlayerName) &&
                                    !string.IsNullOrWhiteSpace(NormalizedJoinCode);
    private bool CanCreateSession => IsAuthenticated &&
                                     !_isSubmittingSession &&
                                     !string.IsNullOrWhiteSpace(ResolvedPlayerName);

    private int MagicLinkCooldownSecondsRemaining => _magicLinkCooldownEndsAtUtc is null
        ? 0
        : Math.Max(0, (int)Math.Ceiling((_magicLinkCooldownEndsAtUtc.Value - DateTimeOffset.UtcNow).TotalSeconds));

    private bool IsMagicLinkCooldownActive => MagicLinkCooldownSecondsRemaining > 0;

    private bool IsMagicLinkRequestDisabled =>
        _isSendingMagicLink || IsMagicLinkCooldownActive || string.IsNullOrWhiteSpace(_email);

    private string MagicLinkRequestButtonText => _isSendingMagicLink
        ? "Link wird gesendet..."
        : IsMagicLinkCooldownActive
            ? $"Erneut in {MagicLinkCooldownSecondsRemaining}s"
            : "Magic Link senden";

    private string MagicLinkCooldownText =>
        $"Nächster Magic Link in {MagicLinkCooldownSecondsRemaining} {(MagicLinkCooldownSecondsRemaining == 1 ? "Sekunde" : "Sekunden")} verfügbar.";

    private string? ActiveSessionPlayerName => CurrentUser is null
        ? null
        : ActiveSession?.Players
            .FirstOrDefault(player => string.Equals(player.UserId, CurrentUser.Id, StringComparison.Ordinal))?.Name;

    public void Dispose()
    {
        _disposed = true;
        StopMagicLinkCooldown();
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
        if (_isSubmittingSession)
        {
            return;
        }

        _isSubmittingSession = true;
        var requestUserId = CurrentUser?.Id;
        var playerName = ResolvePlayerName();
        var joinCode = NormalizedJoinCode;
        _error = string.Empty;

        try
        {
            if (!await EnsureAuthenticatedAsync() || !IsCurrentSubmitContext(requestUserId))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(playerName))
            {
                _error = "Bitte Namen eingeben";
                return;
            }

            if (string.IsNullOrWhiteSpace(joinCode))
            {
                _error = "Bitte Session-Code eingeben";
                return;
            }

            var success = await SessionState.JoinSessionAsync(joinCode, playerName);
            if (!IsCurrentSubmitContext(requestUserId))
            {
                return;
            }

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
            if (IsCurrentSubmitContext(requestUserId))
            {
                _error = exception.Message;
            }
        }
        finally
        {
            _isSubmittingSession = false;
        }
    }

    private async Task Create()
    {
        if (_isSubmittingSession)
        {
            return;
        }

        _isSubmittingSession = true;
        var requestUserId = CurrentUser?.Id;
        var playerName = ResolvePlayerName();
        var sessionName = string.IsNullOrWhiteSpace(_sessionName) ? BuildDefaultSessionName() : _sessionName.Trim();
        _error = string.Empty;

        try
        {
            if (!await EnsureAuthenticatedAsync() || !IsCurrentSubmitContext(requestUserId))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(playerName))
            {
                _error = "Bitte Namen eingeben";
                return;
            }

            await SessionState.CreateSessionAsync(playerName, sessionName);
            if (IsCurrentSubmitContext(requestUserId))
            {
                Nav.NavigateTo("/wuerfel");
            }
        }
        catch (InvalidOperationException exception)
        {
            if (IsCurrentSubmitContext(requestUserId))
            {
                _error = exception.Message;
            }
        }
        finally
        {
            _isSubmittingSession = false;
        }
    }

    private async Task RequestMagicLink()
    {
        if (_isSendingMagicLink || IsMagicLinkCooldownActive)
        {
            return;
        }

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
            SetMagicLinkCooldown(result.CooldownSecondsRemaining);
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
        if (_isSigningOut)
        {
            return;
        }

        _isSigningOut = true;
        _error = string.Empty;
        _authMessage = string.Empty;
        StopMagicLinkCooldown();

        try
        {
            await Game.DisconnectAsync();
            await AuthState.LogoutAsync();
            _userName = string.Empty;
            _isUserNameDirty = false;
            _joinCode = string.Empty;
            _sessionName = string.Empty;
            _lastAppliedUserId = null;
            _lastAppliedActiveSessionId = null;
            _hasAppliedUserContext = false;
            CurrentMode = SessionMode.Join;
        }
        finally
        {
            _isSigningOut = false;
        }
    }

    private void SetMode(SessionMode mode)
    {
        if (_isSubmittingSession)
        {
            return;
        }

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
        if (_disposed)
        {
            return false;
        }

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
        if (!IsAuthenticated || CurrentUser is null)
        {
            _userName = string.Empty;
            _isUserNameDirty = false;
            _lastAppliedUserId = null;
            _lastAppliedActiveSessionId = null;
            _hasAppliedUserContext = false;
            return;
        }

        var userId = CurrentUser.Id;
        var activeSessionId = SessionState.ActiveSessionId;
        var userChanged = !_hasAppliedUserContext ||
                          !string.Equals(_lastAppliedUserId, userId, StringComparison.Ordinal);
        var sessionChanged = !_hasAppliedUserContext ||
                             !string.Equals(_lastAppliedActiveSessionId, activeSessionId, StringComparison.Ordinal);

        if (userChanged)
        {
            _userName = string.Empty;
            _isUserNameDirty = false;
            _joinCode = string.Empty;
            _sessionName = string.Empty;
        }

        if (!_isUserNameDirty && (userChanged || sessionChanged || string.IsNullOrWhiteSpace(_userName)))
        {
            _userName = ActiveSessionPlayerName ?? CurrentUser.DisplayName ?? CurrentUser.Email ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(_sessionName) && !string.IsNullOrWhiteSpace(_userName))
        {
            _sessionName = BuildDefaultSessionName();
        }

        _lastAppliedUserId = userId;
        _lastAppliedActiveSessionId = activeSessionId;
        _hasAppliedUserContext = true;
    }

    private void HandleUserNameInput(ChangeEventArgs args)
    {
        _userName = args.Value?.ToString() ?? string.Empty;
        _isUserNameDirty = true;
    }

    private void ApplyAuthQueryFeedback()
    {
        if (string.Equals(AuthStatus, "invalid", StringComparison.OrdinalIgnoreCase))
        {
            _error = "Magic Link ist ungültig oder abgelaufen. Bitte einen neuen Link anfordern.";
        }
    }

    private void HandleAuthChanged()
    {
        if (_disposed)
        {
            return;
        }

        _ = InvokeAsync(async () =>
        {
            if (_disposed)
            {
                return;
            }

            ApplyAuthenticatedDefaults();
            await SessionState.RefreshAsync();
            await EnsureGameConnectionAsync();
            if (!_disposed)
            {
                StateHasChanged();
            }
        });
    }

    private void HandleSessionStateChanged()
    {
        if (_disposed)
        {
            return;
        }

        _ = InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            ApplyAuthenticatedDefaults();
            StateHasChanged();
        });
    }

    private bool IsCurrentSubmitContext(string? userId)
        => !_disposed &&
           IsAuthenticated &&
           !string.IsNullOrWhiteSpace(userId) &&
           string.Equals(userId, CurrentUser?.Id, StringComparison.Ordinal);

    private string BuildDefaultSessionName()
    {
        var baseName = ResolvePlayerName();

        return string.IsNullOrWhiteSpace(baseName)
            ? "Neue Runde"
            : $"{baseName.Trim()}s Runde";
    }

    private string? ResolvePlayerName()
    {
        if (!string.IsNullOrWhiteSpace(_userName))
        {
            return _userName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(CurrentUser?.DisplayName))
        {
            return CurrentUser.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(CurrentUser?.Email))
        {
            return CurrentUser.Email.Trim();
        }

        return null;
    }

    private void SetMagicLinkCooldown(int cooldownSeconds)
    {
        StopMagicLinkCooldown(clearEndTime: false);

        if (cooldownSeconds <= 0)
        {
            _magicLinkCooldownEndsAtUtc = null;
            return;
        }

        _magicLinkCooldownEndsAtUtc = DateTimeOffset.UtcNow.AddSeconds(cooldownSeconds);
        _magicLinkCooldownCancellation = new CancellationTokenSource();
        _ = RunMagicLinkCooldownAsync(_magicLinkCooldownCancellation.Token);
    }

    private async Task RunMagicLinkCooldownAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (_magicLinkCooldownEndsAtUtc is not null &&
                   MagicLinkCooldownSecondsRemaining > 0 &&
                   await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            _magicLinkCooldownEndsAtUtc = null;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void StopMagicLinkCooldown(bool clearEndTime = true)
    {
        _magicLinkCooldownCancellation?.Cancel();
        _magicLinkCooldownCancellation?.Dispose();
        _magicLinkCooldownCancellation = null;

        if (clearEndTime)
        {
            _magicLinkCooldownEndsAtUtc = null;
        }
    }

    private enum SessionMode
    {
        Join,
        Create
    }
}
