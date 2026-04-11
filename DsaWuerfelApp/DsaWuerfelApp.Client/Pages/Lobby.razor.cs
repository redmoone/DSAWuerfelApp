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
    private CancellationTokenSource? _magicLinkCooldownCancellation;
    private DateTimeOffset? _magicLinkCooldownEndsAtUtc;
    private string _sessionName = "";
    private string _userName = "";

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
    private int ActivePlayerCount => ActiveSession?.Players.Length ?? 0;
    private int ActiveOnlineCount => ActiveSession?.Players.Count(player => player.IsOnline) ?? 0;
    private string? ResolvedPlayerName => ResolvePlayerName();
    private string NormalizedJoinCode => _joinCode.Trim().ToUpperInvariant();
    private bool CanJoinSession => !string.IsNullOrWhiteSpace(ResolvedPlayerName) && !string.IsNullOrWhiteSpace(NormalizedJoinCode);
    private bool CanCreateSession => !string.IsNullOrWhiteSpace(ResolvedPlayerName);

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

    private string ActiveSessionTitle => ActiveSession?.Name ?? "Keine Runde offen";

    private string ActiveSessionMeta => ActiveSession is null
        ? "Wähle oder öffne eine Session im Board"
        : $"{ActiveOnlineCount} von {ActivePlayerCount} Spielern online";

    public void Dispose()
    {
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
        if (!await EnsureAuthenticatedAsync())
        {
            return;
        }

        var playerName = ResolvePlayerName();
        if (string.IsNullOrWhiteSpace(playerName))
        {
            _error = "Bitte Namen eingeben";
            return;
        }

        if (string.IsNullOrWhiteSpace(NormalizedJoinCode))
        {
            _error = "Bitte Session-Code eingeben";
            return;
        }

        _error = string.Empty;
        try
        {
            var success = await SessionState.JoinSessionAsync(NormalizedJoinCode, playerName);
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

        var playerName = ResolvePlayerName();
        if (string.IsNullOrWhiteSpace(playerName))
        {
            _error = "Bitte Namen eingeben";
            return;
        }

        _error = string.Empty;
        var sessionName = string.IsNullOrWhiteSpace(_sessionName) ? BuildDefaultSessionName() : _sessionName.Trim();

        try
        {
            await SessionState.CreateSessionAsync(playerName, sessionName);
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

        if (IsMagicLinkCooldownActive)
        {
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
        _isSigningOut = true;
        _error = string.Empty;
        _authMessage = string.Empty;
        StopMagicLinkCooldown();

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
        if (!string.IsNullOrWhiteSpace(ActiveSessionPlayerName))
        {
            _userName = ActiveSessionPlayerName;
        }
        else if (string.IsNullOrWhiteSpace(_userName) && !string.IsNullOrWhiteSpace(CurrentUser?.DisplayName))
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
        if (string.Equals(AuthStatus, "invalid", StringComparison.OrdinalIgnoreCase))
        {
            _error = "Magic Link ist ungültig oder abgelaufen. Bitte einen neuen Link anfordern.";
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
        _ = InvokeAsync(() =>
        {
            ApplyAuthenticatedDefaults();
            StateHasChanged();
        });
    }

    private void GoToWuerfel()
    {
        Nav.NavigateTo("/wuerfel");
    }

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
