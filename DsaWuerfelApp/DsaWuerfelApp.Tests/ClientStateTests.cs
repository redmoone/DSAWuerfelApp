using System.Reflection;
using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DsaWuerfelApp.Tests;

public class ClientStateTests
{
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Call = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!, args);
        public static T Make<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
        {
            var instance = Create<T, Proxy>(); ((Proxy)(object)instance).Call = call; return instance;
        }
    }
    private sealed class Navigation : NavigationManager { public Navigation() => Initialize("http://localhost/", "http://localhost/"); }
    private sealed class Js : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
    private static TaskCompletionSource<DicePageContextDto> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static DicePageContextDto Context(string name) => new(null, name, [], [], [], "", false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Latest_context_wins_and_old_error_is_ignored(bool failOld)
    {
        await using var game = new GameClient(new Navigation());
        var auth = new AuthState(null!);
        using var session = new SessionState(null!, auth, game, new Js());
        using var active = new ActiveHeroState(null!, auth);
        var state = new WuerfelState();
        var first = Pending(); var second = Pending(); var count = 0;
        var api = Proxy.Make<IWuerfelApiClient>((_, _) => ++count == 1 ? first.Task : second.Task);
        using var context = new WuerfelContextService(state, active, session, api, new WuerfelUiOperationRunner(state), auth);
        var a = context.LoadContextAsync();
        var b = context.LoadContextAsync(forceRefresh: true);
        second.SetResult(Context("B")); await b;
        Assert.True(state.Current.IsBusy);
        if (failOld) first.SetException(new IOException("old error")); else first.SetResult(Context("A"));
        await a;
        Assert.Equal("B", state.Current.ActiveHeroName);
        Assert.Null(state.Current.ErrorMessage);
        Assert.False(state.Current.IsBusy);
    }

    [Fact]
    public async Task Display_updates_preserve_form_but_force_refresh_reloads_same_hero()
    {
        await using var game = new GameClient(new Navigation());
        var auth = new AuthState(null!);
        using var session = new SessionState(null!, auth, game, new Js());
        using var active = new ActiveHeroState(null!, auth);
        var state = new WuerfelState(); var calls = 0;
        var api = Proxy.Make<IWuerfelApiClient>((_, _) => { calls++; return Task.FromResult(Context("Hero")); });
        using var context = new WuerfelContextService(state, active, session, api, new WuerfelUiOperationRunner(state), auth);
        var id = Guid.NewGuid();
        var a = new SessionPlayerDto("a", "A", false, true, id, "Hero");
        var b = new SessionPlayerDto("b", "B", false, true, Guid.NewGuid(), "Hero2");
        await context.LoadContextAsync([a, b], true);
        state.SetModifier(7);
        var before = calls;
        await context.LoadContextAsync([b with { Name = "Renamed", IsOnline = false }, a], true);
        Assert.Equal(before, calls); Assert.Equal(7, state.Current.Modifier);
        await context.LoadContextAsync([a, b], true, forceRefresh: true);
        Assert.True(calls > before);
    }

    [Fact]
    public async Task Disposed_context_cannot_apply_pending_response()
    {
        await using var game = new GameClient(new Navigation());
        var auth = new AuthState(null!);
        using var session = new SessionState(null!, auth, game, new Js());
        using var active = new ActiveHeroState(null!, auth);
        var state = new WuerfelState(); var pending = Pending();
        using var context = new WuerfelContextService(state, active, session,
            Proxy.Make<IWuerfelApiClient>((_, _) => pending.Task), new WuerfelUiOperationRunner(state), auth);
        var load = context.LoadContextAsync(); context.Dispose(); pending.SetResult(Context("old")); await load;
        Assert.NotEqual("old", state.Current.ActiveHeroName);
    }

    private static Task LoadSession(SessionState state, string id) => (Task)typeof(SessionState)
        .GetMethod("LoadActiveSessionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(state, [id, CancellationToken.None])!;
    private static void Select(GameClient game, string id) => typeof(GameClient).GetProperty(nameof(GameClient.CurrentSessionId))!.SetValue(game, id);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Session_load_ignores_older_reply_or_error(bool failOld)
    {
        await using var game = new GameClient(new Navigation());
        var auth = new AuthState(Proxy.Make<IAuthApiClient>((_, _) => Task.FromResult(new AuthSessionDto(true, new("u", "u@example.test", "U")))));
        await auth.RefreshAsync();
        var a = new TaskCompletionSource<SessionDetailsDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var b = new TaskCompletionSource<SessionDetailsDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new SessionState(Proxy.Make<ISessionApiClient>((_, args) => (string)args![0]! == "a" ? a.Task : b.Task), auth, game, new Js());
        Select(game, "a"); var first = LoadSession(session, "a");
        Select(game, "b"); var second = LoadSession(session, "b");
        b.SetResult(new("b", "B", "code", "u", [], [])); await second;
        if (failOld) a.SetException(new IOException("stale")); else a.SetResult(new("a", "A", "code", "u", [], []));
        await first;
        Assert.Equal("b", session.ActiveSession?.SessionId);
    }

    [Fact]
    public async Task Logout_invalidates_pending_session_load()
    {
        await using var game = new GameClient(new Navigation());
        var auth = new AuthState(Proxy.Make<IAuthApiClient>((m, _) => m.Name == "LogoutAsync" ? Task.CompletedTask : Task.FromResult(new AuthSessionDto(true, new("u", "u@example.test", "U")))));
        await auth.RefreshAsync();
        var pending = new TaskCompletionSource<SessionDetailsDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new SessionState(Proxy.Make<ISessionApiClient>((_, _) => pending.Task), auth, game, new Js());
        Select(game, "a"); var load = LoadSession(session, "a");
        await auth.LogoutAsync();
        pending.SetResult(new("a", "A", "code", "u", [], [])); await load;
        Assert.Null(session.ActiveSession); Assert.Null(game.CurrentSessionId);
    }

    [Fact]
    public async Task Disconnected_selected_session_never_falls_back_to_api()
    {
        await using var game = new GameClient(new Navigation());
        Select(game, "session");
        var apiCalls = 0; var hubCalls = 0;
        var dispatcher = new WuerfelRollCommandDispatcher([new ApiWuerfelRollDispatchStrategy(null!, game), new SessionWuerfelRollDispatchStrategy(game)]);
        var command = new WuerfelRollCommand<string>(_ => { hubCalls++; return Task.CompletedTask; }, (_, _) => { apiCalls++; return Task.FromResult("roll"); }, _ => {});
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(command));
        Assert.Equal(0, apiCalls); Assert.Equal(0, hubCalls);
        game.ClearActiveSession(); await dispatcher.DispatchAsync(command);
        Assert.Equal(1, apiCalls);
    }

    private sealed class DelayedStorage : IJSRuntime
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Removed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Stored { get; private set; }
        public async ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            if (identifier == "localStorage.setItem") { Entered.TrySetResult(); await Release.Task; Stored = (string?)args![1]; }
            if (identifier == "localStorage.removeItem") { Stored = null; Removed.TrySetResult(); }
            return default!;
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }

    [Fact]
    public async Task Logout_clear_waits_for_old_storage_write_and_remains_final()
    {
        await using var game = new GameClient(new Navigation());
        var auth = new AuthState(Proxy.Make<IAuthApiClient>((m, _) => m.Name == "LogoutAsync" ? Task.CompletedTask : Task.FromResult(new AuthSessionDto(true, new("u", "u@example.test", "U")))));
        await auth.RefreshAsync();
        var storage = new DelayedStorage();
        using var session = new SessionState(Proxy.Make<ISessionApiClient>((_, _) => Task.FromResult<SessionDetailsDto?>(new("a", "A", "code", "u", [], []))), auth, game, storage);
        Select(game, "a"); var load = LoadSession(session, "a"); await storage.Entered.Task;
        await auth.LogoutAsync(); storage.Release.SetResult(); await load; await storage.Removed.Task;
        Assert.Null(storage.Stored); Assert.Null(session.ActiveSession);
    }

    [Fact]
    public async Task Hero_load_cannot_restore_hero_after_logout()
    {
        var auth = new AuthState(Proxy.Make<IAuthApiClient>((m, _) => m.Name == "LogoutAsync" ? Task.CompletedTask : Task.FromResult(new AuthSessionDto(true, new("u", "u@example.test", "U")))));
        await auth.RefreshAsync();
        var pending = new TaskCompletionSource<Hero?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var active = new ActiveHeroState(Proxy.Make<IHeroApiClient>((_, _) => pending.Task), auth);
        var load = active.EnsureLoadedAsync(); await auth.LogoutAsync(); pending.SetResult(new Hero {Name="old"}); await load;
        Assert.Null(active.CurrentHero);
    }

    private sealed class StoredSelection : IJSRuntime
    {
        public int Removes;
        public string? Written;
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            if (identifier == "localStorage.removeItem") Removes++;
            if (identifier == "localStorage.setItem") Written = (string?)args![1];
            return ValueTask.FromResult(identifier == "localStorage.getItem" ? (T)(object)"stored" : default!);
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }

    [Fact]
    public async Task Restore_does_not_delete_selection_before_latest_session_list_arrives()
    {
        await using var game = new GameClient(new Navigation());
        var auth = new AuthState(Proxy.Make<IAuthApiClient>((_, _) => Task.FromResult(new AuthSessionDto(true, new("u", "u@example.test", "U")))));
        await auth.RefreshAsync();
        var pending = new TaskCompletionSource<IReadOnlyList<SessionSummaryDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new StoredSelection();
        using var session = new SessionState(Proxy.Make<ISessionApiClient>((_, _) => pending.Task), auth, game, storage);
        var refresh = session.RefreshAsync(); var restore = session.RestoreActiveSessionAsync();
        Assert.False(restore.IsCompleted); Assert.Equal(0, storage.Removes);
        pending.SetResult([]); await refresh; await restore;
        Assert.Equal(1, storage.Removes);
    }

    [Fact]
    public async Task Metadata_refresh_also_persists_current_selection()
    {
        await using var game = new GameClient(new Navigation());
        var auth = new AuthState(Proxy.Make<IAuthApiClient>((_, _) => Task.FromResult(new AuthSessionDto(true, new("u", "u@example.test", "U")))));
        await auth.RefreshAsync();
        var storage = new StoredSelection();
        var api = Proxy.Make<ISessionApiClient>((method, _) => method.Name == "GetMySessionsAsync"
            ? Task.FromResult<IReadOnlyList<SessionSummaryDto>>([new("a", "A", "code", "u", [])])
            : Task.FromResult<SessionDetailsDto?>(new("a", "A", "code", "u", [], [])));
        using var session = new SessionState(api, auth, game, storage);
        Select(game, "a");
        await (Task)typeof(SessionState).GetMethod("HandleSessionsChangedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, null)!;
        Assert.Equal("a", storage.Written);
    }
}
