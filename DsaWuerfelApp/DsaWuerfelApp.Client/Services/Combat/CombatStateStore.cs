using System.Text.Json;

using DsaWuerfelApp.Shared;

using Microsoft.JSInterop;

namespace DsaWuerfelApp.Client.Services;

public sealed class CombatStateStore(IJSRuntime jsRuntime)
{
    private const string StoragePrefix = "dsa.combat-state:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CombatPersistedState?> LoadAsync(
        string contextKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await jsRuntime.InvokeAsync<string?>(
                "localStorage.getItem",
                cancellationToken,
                BuildStorageKey(contextKey));
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<CombatPersistedState>(json, JsonOptions);
        }
        catch (JSException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<bool> SaveAsync(
        string contextKey,
        CombatPersistedState state,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await jsRuntime.InvokeVoidAsync(
                "localStorage.setItem",
                cancellationToken,
                BuildStorageKey(contextKey),
                json);
            return true;
        }
        catch (JSException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildStorageKey(string contextKey) =>
        $"{StoragePrefix}{Uri.EscapeDataString(contextKey)}";
}

public sealed record CombatPersistedState(
    CombatRuntimeSnapshot Current,
    CombatRuntimeSnapshot? Undo);

public sealed record CombatRuntimeSnapshot(
    int SchemaVersion,
    string ContextKey,
    int ProfileRevision,
    bool IsStarted,
    int? CurrentLeP,
    int? CurrentAuP,
    int? CurrentInitiative,
    Dictionary<CombatWoundZone, int?> Wounds,
    int Round,
    string[] Effects,
    string? Note,
    DateTimeOffset? LastChangedAtUtc)
{
    public int? CurrentAeP { get; init; }
    public int? CurrentKeP { get; init; }
    public string? SelectedSetId { get; init; }
    public string? SelectedWeaponId { get; init; }
    public string SelectedAction { get; init; } = "attack";
    public string SelectedProbe { get; init; } = string.Empty;
    public string? SelectedAttribute { get; init; }
    public string[] SelectedAttributes { get; init; } = [];
    public int SituationalModifier { get; init; }
    public int InitiativeRuntimeModifier { get; init; }
    public string RollText { get; init; } = string.Empty;
    public CombatFacing Facing { get; init; } = CombatFacing.Front;
    public CombatWoundZone? SelectedZone { get; init; }
}
