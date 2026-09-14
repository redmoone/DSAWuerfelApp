using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Client.Services;

public enum CombatResourceKind
{
    LeP,
    AuP,
    AeP,
    KeP
}

public sealed class CombatState : IDisposable
{
    private readonly ActiveHeroState _activeHeroState;
    private readonly AuthState _authState;
    private readonly SessionState _sessionState;
    private readonly IHeroApiClient _heroApiClient;
    private readonly CombatStateStore _store;
    private readonly SemaphoreSlim _contextLock = new(1, 1);

    private CancellationTokenSource? _contextCancellation;
    private CombatRuntimeSnapshot _runtime = CreateEmptyRuntime("unbound", 0);
    private CombatRuntimeSnapshot? _undo;
    private string? _contextKey;
    private string? _persistenceError;
    private long _contextVersion;
    private bool _contextLoaded;
    private bool _disposed;

    public CombatState(
        ActiveHeroState activeHeroState,
        AuthState authState,
        SessionState sessionState,
        IHeroApiClient heroApiClient,
        CombatStateStore store)
    {
        _activeHeroState = activeHeroState;
        _authState = authState;
        _sessionState = sessionState;
        _heroApiClient = heroApiClient;
        _store = store;

        _activeHeroState.Changed += HandleContextChanged;
        _sessionState.ActiveSessionChanged += HandleContextChanged;
        _authState.Changed += HandleAuthChanged;
    }

    public CombatProfileDto? Profile { get; private set; }
    public bool IsProfileLoading { get; private set; }
    public string? ProfileError { get; private set; }
    public string? ContextKey => _contextKey;
    public CombatRuntimeSnapshot Runtime => _runtime;
    public bool IsStarted => _runtime.IsStarted;
    public bool CanUndo => _undo is not null;
    public string? PersistenceError => _persistenceError;
    public string SelectedAction => _runtime.SelectedAction;
    public string SelectedProbe => _runtime.SelectedProbe;
    public string? SelectedAttribute => _runtime.SelectedAttribute;
    public IReadOnlyList<string> SelectedAttributes => _runtime.SelectedAttributes;
    public int Modifier => _runtime.SituationalModifier;
    public string RollText => _runtime.RollText;
    public CombatFacing Facing => _runtime.Facing;
    public CombatWoundZone? SelectedZone => _runtime.SelectedZone;
    public string? SelectedSetId => _runtime.SelectedSetId;
    public string? SelectedWeaponId => _runtime.SelectedWeaponId;
    public int? CurrentLeP => _runtime.CurrentLeP;
    public int? CurrentAuP => _runtime.CurrentAuP;
    public int? CurrentAeP => _runtime.CurrentAeP;
    public int? CurrentKeP => _runtime.CurrentKeP;
    public int? CurrentInitiative => _runtime.CurrentInitiative;
    public IReadOnlyDictionary<CombatWoundZone, int?> Wounds => _runtime.Wounds;
    public IReadOnlyList<string> Effects => _runtime.Effects;

    public CombatSetVariantDto? SelectedSet =>
        Profile?.Sets.FirstOrDefault(set => set.Id == _runtime.SelectedSetId);

    public IReadOnlyList<CombatWeaponDto> Weapons =>
        SelectedSet?.Weapons ?? Array.Empty<CombatWeaponDto>();

    public CombatWeaponDto? SelectedWeapon =>
        Weapons.FirstOrDefault(weapon => weapon.Id == _runtime.SelectedWeaponId);

    public event Action? Changed;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        await _authState.EnsureLoadedAsync(cancellationToken);
        if (!_authState.Current.IsAuthenticated)
        {
            ResetWithoutContext();
            return;
        }

        await _activeHeroState.EnsureLoadedAsync();
        await _sessionState.EnsureLoadedAsync(cancellationToken);
        await LoadCurrentContextAsync(cancellationToken);
    }

    public async Task ReloadProfileAsync(CancellationToken cancellationToken = default)
    {
        _contextLoaded = false;
        await LoadCurrentContextAsync(cancellationToken);
    }

    public async Task SetSelectedSetAsync(string setId)
    {
        if (Profile is null || Profile.Sets.All(set => !string.Equals(set.Id, setId, StringComparison.Ordinal)))
        {
            return;
        }

        var set = Profile.Sets.First(set => string.Equals(set.Id, setId, StringComparison.Ordinal));
        var previous = _runtime;
        var weaponId = PreferredWeaponId(set);
        var next = _runtime with { SelectedSetId = set.Id, SelectedWeaponId = weaponId };
        var previousBase = ResolveInitiativeBase(previous);
        var nextBase = ResolveInitiativeBase(next);
        var previousModifier = ResolveInitiativeRuntimeModifier(previous);
        var nextModifier = ResolveInitiativeRuntimeModifier(next);
        var baseDelta = previousBase.HasValue && nextBase.HasValue
            ? nextBase.Value - previousBase.Value
            : 0;
        var initiativeDelta = baseDelta + nextModifier - previousModifier;
        _runtime = next with
        {
            InitiativeRuntimeModifier = nextModifier,
            CurrentInitiative = previous.CurrentInitiative.HasValue
                ? previous.CurrentInitiative.Value + initiativeDelta
                : next.CurrentInitiative
        };
        if (previous.CurrentInitiative.HasValue && initiativeDelta != 0)
        {
            _undo = previous;
        }
        await PersistCurrentAsync();
    }

    public async Task SetSelectedWeaponAsync(string weaponId)
    {
        if (Weapons.All(weapon => !string.Equals(weapon.Id, weaponId, StringComparison.Ordinal)))
        {
            return;
        }

        _runtime = _runtime with { SelectedWeaponId = weaponId };
        await PersistCurrentAsync();
    }

    public async Task SetSelectedActionAsync(string action)
    {
        _runtime = _runtime with { SelectedAction = string.IsNullOrWhiteSpace(action) ? "attack" : action };
        await PersistCurrentAsync();
    }

    public async Task SetSelectedProbeAsync(string probe)
    {
        _runtime = _runtime with { SelectedProbe = probe ?? string.Empty };
        await PersistCurrentAsync();
    }

    public async Task SetSelectedAttributeAsync(string? attribute)
    {
        _runtime = _runtime with { SelectedAttribute = attribute };
        await PersistCurrentAsync();
    }

    public async Task AddSelectedAttributeAsync(string attribute)
    {
        if (Profile?.Attributes.All(current => !string.Equals(current.Key, attribute, StringComparison.Ordinal)) != false)
        {
            return;
        }

        var selection = _runtime.SelectedAttributes.Append(attribute).ToArray();
        _runtime = _runtime with
        {
            SelectedAttributes = selection,
            SelectedAttribute = attribute
        };
        await PersistCurrentAsync();
    }

    public async Task RemoveSelectedAttributeAtAsync(int index)
    {
        if (index < 0 || index >= _runtime.SelectedAttributes.Length)
        {
            return;
        }

        var selection = _runtime.SelectedAttributes.ToList();
        selection.RemoveAt(index);
        _runtime = _runtime with
        {
            SelectedAttributes = selection.ToArray(),
            SelectedAttribute = selection.LastOrDefault()
        };
        await PersistCurrentAsync();
    }

    public async Task SetModifierAsync(int modifier)
    {
        _runtime = _runtime with { SituationalModifier = modifier };
        await PersistCurrentAsync();
    }

    public async Task SetResourceAsync(CombatResourceKind resource, int? value)
    {
        if (Profile is null)
        {
            return;
        }

        _undo = _runtime;
        var normalized = resource == CombatResourceKind.LeP
            ? value
            : value.HasValue ? Math.Max(0, value.Value) : null;
        var next = (resource switch
        {
            CombatResourceKind.LeP => _runtime with { CurrentLeP = normalized },
            CombatResourceKind.AuP => _runtime with { CurrentAuP = normalized },
            CombatResourceKind.AeP => _runtime with { CurrentAeP = normalized },
            CombatResourceKind.KeP => _runtime with { CurrentKeP = normalized },
            _ => _runtime
        }) with
        {
            IsStarted = _runtime.IsStarted || normalized.HasValue,
            LastChangedAtUtc = DateTimeOffset.UtcNow
        };
        _runtime = AdjustInitiativeForRuntimeChange(_runtime, next);
        await PersistCurrentAsync();
    }

    public async Task SetWoundAsync(CombatWoundZone zone, int value)
    {
        if (Profile is null)
        {
            return;
        }

        _undo = _runtime;
        var wounds = new Dictionary<CombatWoundZone, int?>(_runtime.Wounds)
        {
            [zone] = Math.Clamp(value, 0, 3)
        };
        var next = _runtime with
        {
            IsStarted = true,
            Wounds = wounds,
            LastChangedAtUtc = DateTimeOffset.UtcNow
        };
        _runtime = AdjustInitiativeForRuntimeChange(_runtime, next);
        await PersistCurrentAsync();
    }

    public async Task SetRollTextAsync(string text)
    {
        _runtime = _runtime with { RollText = text ?? string.Empty };
        await PersistCurrentAsync();
    }

    public async Task ResetActionAsync()
    {
        _runtime = _runtime with { SituationalModifier = 0, RollText = string.Empty };
        await PersistCurrentAsync();
    }

    public async Task SetFacingAsync(CombatFacing facing)
    {
        _runtime = _runtime with { Facing = facing };
        await PersistCurrentAsync();
    }

    public async Task SetSelectedZoneAsync(CombatWoundZone? zone)
    {
        _runtime = _runtime with { SelectedZone = zone };
        await PersistCurrentAsync();
    }

    public async Task<bool> StartCombatAsync()
    {
        if (Profile is null)
        {
            return false;
        }

        if (_runtime.IsStarted)
        {
            return true;
        }

        _undo = _runtime;
        var wounds = _runtime.Wounds.ToDictionary(pair => pair.Key, pair => (int?)(pair.Value ?? 0));
        var next = _runtime with
        {
            IsStarted = true,
            CurrentLeP = _runtime.CurrentLeP ?? Profile.Resources.LeP,
            CurrentAuP = _runtime.CurrentAuP ?? Profile.Resources.AuP,
            CurrentAeP = _runtime.CurrentAeP ?? Profile.Resources.AeP,
            CurrentKeP = _runtime.CurrentKeP ?? Profile.Resources.KeP,
            Wounds = wounds,
            LastChangedAtUtc = DateTimeOffset.UtcNow
        };
        _runtime = AdjustInitiativeForRuntimeChange(_runtime, next);
        await PersistCurrentAsync();
        return true;
    }

    public async Task<bool> SetInitiativeAsync(int? initiative)
    {
        if (Profile is null)
        {
            return false;
        }

        _undo = _runtime;
        _runtime = _runtime with
        {
            CurrentInitiative = initiative,
            InitiativeRuntimeModifier = ResolveInitiativeRuntimeModifier(_runtime),
            LastChangedAtUtc = DateTimeOffset.UtcNow
        };
        await PersistCurrentAsync();
        return true;
    }

    public async Task<bool> ApplyHitAsync(
        CombatWoundZone zone,
        int lepLoss,
        int newWounds,
        string? note)
    {
        if (!_runtime.IsStarted || !_runtime.CurrentLeP.HasValue)
        {
            return false;
        }

        _undo = _runtime;
        var wounds = new Dictionary<CombatWoundZone, int?>(_runtime.Wounds)
        {
            [zone] = Math.Clamp(newWounds, 0, 3)
        };
        _runtime = _runtime with
        {
            CurrentLeP = _runtime.CurrentLeP.Value - Math.Max(0, lepLoss),
            Wounds = wounds,
            Note = string.IsNullOrWhiteSpace(note) ? _runtime.Note : note.Trim(),
            LastChangedAtUtc = DateTimeOffset.UtcNow
        };
        await PersistCurrentAsync();
        return true;
    }

    public async Task<bool> UndoLastChangeAsync()
    {
        if (_undo is null)
        {
            return false;
        }

        _runtime = NormalizeSnapshot(_undo, _contextKey ?? _runtime.ContextKey, Profile?.SourceRevision ?? _runtime.ProfileRevision);
        _undo = null;
        await PersistCurrentAsync();
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activeHeroState.Changed -= HandleContextChanged;
        _sessionState.ActiveSessionChanged -= HandleContextChanged;
        _authState.Changed -= HandleAuthChanged;
        _contextCancellation?.Cancel();
        _contextCancellation?.Dispose();
        _contextLock.Dispose();
    }

    private async Task LoadCurrentContextAsync(CancellationToken cancellationToken = default)
    {
        await _contextLock.WaitAsync(cancellationToken);
        try
        {
            var hero = _activeHeroState.CurrentHero;
            var contextKey = BuildContextKey(hero);
            if (_contextLoaded && string.Equals(_contextKey, contextKey, StringComparison.Ordinal))
            {
                return;
            }

            var version = ++_contextVersion;
            _contextCancellation?.Cancel();
            _contextCancellation?.Dispose();
            _contextCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _contextCancellation.Token;
            _contextKey = contextKey;
            _contextLoaded = false;
            Profile = null;
            ProfileError = null;
            IsProfileLoading = hero is not null;
            _persistenceError = null;
            _runtime = CreateEmptyRuntime(contextKey ?? "unbound", 0);
            _undo = null;
            Notify();

            if (hero is null || string.IsNullOrWhiteSpace(contextKey))
            {
                IsProfileLoading = false;
                _contextLoaded = true;
                Notify();
                return;
            }

            try
            {
                var profile = await _heroApiClient.GetCombatProfileAsync(hero.Id, token);
                if (!IsCurrent(version, contextKey, hero.Id, token))
                {
                    return;
                }

                Profile = profile;
                var persisted = await _store.LoadAsync(contextKey, token);
                if (!IsCurrent(version, contextKey, hero.Id, token))
                {
                    return;
                }

                _runtime = NormalizeSnapshot(
                    persisted?.Current ?? CreateInitialRuntime(contextKey, profile),
                    contextKey,
                    profile.SourceRevision);
                _undo = persisted?.Undo is null
                    ? null
                    : NormalizeSnapshot(persisted.Undo, contextKey, profile.SourceRevision);
                RestoreSelection(profile);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (HttpRequestException exception)
            {
                if (IsCurrent(version, contextKey, hero.Id, token))
                {
                    ProfileError = exception.Message.Trim('"');
                }
            }
            catch (Exception exception) when (IsCurrent(version, contextKey, hero.Id, token))
            {
                ProfileError = exception.Message;
            }
            finally
            {
                if (IsCurrent(version, contextKey, hero.Id, token))
                {
                    IsProfileLoading = false;
                    _contextLoaded = true;
                    Notify();
                }
            }
        }
        finally
        {
            _contextLock.Release();
        }
    }

    private async void HandleContextChanged()
    {
        try
        {
            _contextLoaded = false;
            await LoadCurrentContextAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void HandleAuthChanged()
    {
        _contextLoaded = false;
        if (!_authState.Current.IsAuthenticated)
        {
            ResetWithoutContext();
            return;
        }

        HandleContextChanged();
    }

    private bool IsCurrent(long version, string contextKey, Guid heroId, CancellationToken token)
    {
        return !_disposed &&
               !token.IsCancellationRequested &&
               version == _contextVersion &&
               string.Equals(_contextKey, contextKey, StringComparison.Ordinal) &&
               string.Equals(BuildContextKey(_activeHeroState.CurrentHero), contextKey, StringComparison.Ordinal) &&
               _activeHeroState.CurrentHero?.Id == heroId &&
               _authState.Current.IsAuthenticated;
    }

    private async Task PersistCurrentAsync()
    {
        if (_disposed || string.IsNullOrWhiteSpace(_contextKey) || Profile is null)
        {
            Notify();
            return;
        }

        _runtime = _runtime with
        {
            ContextKey = _contextKey,
            ProfileRevision = Profile.SourceRevision,
            SelectedSetId = _runtime.SelectedSetId,
            SelectedWeaponId = _runtime.SelectedWeaponId
        };
        var persisted = new CombatPersistedState(_runtime, _undo);
        var saved = await _store.SaveAsync(_contextKey, persisted);
        _persistenceError = saved ? null : "Änderungen werden gerade nicht dauerhaft gespeichert.";
        Notify();
    }

    private void RestoreSelection(CombatProfileDto profile)
    {
        var selectedSet = profile.Sets.FirstOrDefault(set => set.Id == _runtime.SelectedSetId)
            ?? PreferredSet(profile.Sets);
        var selectedWeapon = selectedSet?.Weapons.FirstOrDefault(weapon => weapon.Id == _runtime.SelectedWeaponId)
            ?? selectedSet?.Weapons.FirstOrDefault(weapon => weapon.IsAvailable == true)
            ?? selectedSet?.Weapons.FirstOrDefault();
        _runtime = _runtime with
        {
            SelectedSetId = selectedSet?.Id,
            SelectedWeaponId = selectedWeapon?.Id,
            SelectedAction = string.IsNullOrWhiteSpace(_runtime.SelectedAction) ? "attack" : _runtime.SelectedAction,
            SelectedProbe = _runtime.SelectedProbe ?? string.Empty,
            RollText = _runtime.RollText ?? string.Empty,
            SelectedAttributes = _runtime.SelectedAttributes ?? []
        };
    }

    private static CombatSetVariantDto? PreferredSet(IReadOnlyList<CombatSetVariantDto> sets)
    {
        return sets
            .Where(set => set.IsInUse)
            .OrderByDescending(set => set.IsDefault)
            .ThenBy(set => set.Number)
            .ThenBy(set => set.ArmorModel)
            .FirstOrDefault()
            ?? sets.OrderBy(set => set.Number).ThenBy(set => set.ArmorModel).FirstOrDefault();
    }

    private static string? PreferredWeaponId(CombatSetVariantDto? set)
    {
        return set?.Weapons
            .OrderByDescending(weapon => weapon.IsAvailable == true)
            .ThenBy(weapon => weapon.Category)
            .ThenBy(weapon => weapon.Number)
            .Select(weapon => weapon.Id)
            .FirstOrDefault();
    }

    private string? BuildContextKey(Hero? hero, bool includeSession = true)
    {
        var userId = _authState.Current.User?.Id;
        if (hero is null || string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var sessionId = includeSession && !string.IsNullOrWhiteSpace(_sessionState.ActiveSessionId)
            ? _sessionState.ActiveSessionId
            : "solo";
        return $"{userId}:{hero.Id:N}:{sessionId}";
    }

    private void ResetWithoutContext()
    {
        _contextLoaded = true;
        _contextKey = null;
        Profile = null;
        ProfileError = null;
        IsProfileLoading = false;
        _runtime = CreateEmptyRuntime("unbound", 0);
        _undo = null;
        Notify();
    }

    private static CombatRuntimeSnapshot CreateEmptyRuntime(string contextKey, int profileRevision)
    {
        return new CombatRuntimeSnapshot(
            1,
            contextKey,
            profileRevision,
            false,
            null,
            null,
            null,
            EmptyWounds(),
            0,
            [],
            null,
            null);
    }

    private static CombatRuntimeSnapshot CreateInitialRuntime(string contextKey, CombatProfileDto profile)
    {
        return new CombatRuntimeSnapshot(
            1,
            contextKey,
            profile.SourceRevision,
            false,
            null,
            null,
            null,
            EmptyWounds(),
            0,
            [],
            null,
            null);
    }

    private static Dictionary<CombatWoundZone, int?> EmptyWounds() =>
        Enum.GetValues<CombatWoundZone>().ToDictionary(zone => zone, _ => (int?)null);

    private CombatRuntimeSnapshot AdjustInitiativeForRuntimeChange(
        CombatRuntimeSnapshot previous,
        CombatRuntimeSnapshot next)
    {
        var previousModifier = ResolveInitiativeRuntimeModifier(previous);
        var nextModifier = ResolveInitiativeRuntimeModifier(next);
        return next with
        {
            InitiativeRuntimeModifier = nextModifier,
            CurrentInitiative = previous.CurrentInitiative.HasValue
                ? previous.CurrentInitiative.Value + nextModifier - previousModifier
                : next.CurrentInitiative
        };
    }

    private int ResolveInitiativeRuntimeModifier(CombatRuntimeSnapshot runtime)
    {
        if (Profile is null)
        {
            return 0;
        }

        var set = Profile.Sets.FirstOrDefault(current => current.Id == runtime.SelectedSetId)
            ?? PreferredSet(Profile.Sets);
        if (set is null)
        {
            return 0;
        }

        var state = new CombatRuntimeStateDto
        {
            IsStarted = runtime.IsStarted,
            CurrentLeP = runtime.CurrentLeP,
            CurrentAuP = runtime.CurrentAuP,
            Wounds = runtime.Wounds?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? EmptyWounds()
        };
        return CombatRuntimeModifierRules.ResolveInitiative(Profile, set, state).Modifier;
    }

    private int? ResolveInitiativeBase(CombatRuntimeSnapshot runtime)
    {
        if (Profile is null)
        {
            return null;
        }

        return Profile.Sets.FirstOrDefault(current => current.Id == runtime.SelectedSetId)?.Initiative
               ?? PreferredSet(Profile.Sets)?.Initiative;
    }

    private static CombatRuntimeSnapshot NormalizeSnapshot(
        CombatRuntimeSnapshot snapshot,
        string contextKey,
        int profileRevision)
    {
        var wounds = EmptyWounds();
        if (snapshot.Wounds is not null)
        {
            foreach (var zone in wounds.Keys.ToArray())
            {
                if (snapshot.Wounds.TryGetValue(zone, out var value))
                {
                    wounds[zone] = value.HasValue ? Math.Clamp(value.Value, 0, 3) : null;
                }
            }
        }

        return snapshot with
        {
            ContextKey = contextKey,
            ProfileRevision = profileRevision,
            Wounds = wounds,
            Effects = snapshot.Effects ?? [],
            SelectedAction = string.IsNullOrWhiteSpace(snapshot.SelectedAction) ? "attack" : snapshot.SelectedAction,
            SelectedProbe = snapshot.SelectedProbe ?? string.Empty,
            RollText = snapshot.RollText ?? string.Empty
        };
    }

    private void Notify() => Changed?.Invoke();
}
