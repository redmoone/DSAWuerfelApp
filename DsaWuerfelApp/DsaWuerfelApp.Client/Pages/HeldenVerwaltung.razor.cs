using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace DsaWuerfelApp.Client.Pages;

public partial class HeldenVerwaltung : ComponentBase, IAsyncDisposable
{
    private const long MaxFileSize = 1024 * 1024 * 5;
    private const int MaxAllowedFiles = 15;

    private IJSObjectReference? _dropZoneModule;
    private bool _dropZoneRegistered;
    private bool _isBusy;
    private bool _isLoadingHeroes;
    private bool _heroesLoadFailed;
    private bool _disposed;

    [Inject] private ActiveHeroState ActiveHeroState { get; set; } = default!;
    [Inject] private IHeroApiClient HeroApiClient { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    protected List<Hero> Heroes { get; set; } = new();
    protected IReadOnlyList<IBrowserFile> SelectedFiles { get; set; } = Array.Empty<IBrowserFile>();
    protected Guid? PendingDeleteHeroId { get; set; }
    protected string ErrorMessage { get; set; } = string.Empty;
    protected string LoadErrorMessage { get; set; } = string.Empty;
    protected string StatusMessage { get; set; } = string.Empty;
    protected bool IsLoadingHeroes => _isLoadingHeroes;
    protected bool HeroesLoadFailed => _heroesLoadFailed;
    protected bool IsBusy => _isBusy;
    protected string DragClass { get; set; } = string.Empty;
    protected ElementReference DropZoneElement { get; set; }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        if (_dropZoneModule is null)
        {
            return;
        }

        try
        {
            await _dropZoneModule.InvokeVoidAsync("disposeHeroDropZone", DropZoneElement);
            await _dropZoneModule.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadHeroesAsync();
    }

    protected async Task LoadHeroesAsync()
    {
        if (_isLoadingHeroes || _disposed)
        {
            return;
        }

        _isLoadingHeroes = true;
        _heroesLoadFailed = false;
        LoadErrorMessage = string.Empty;
        ErrorMessage = string.Empty;

        try
        {
            var heroes = await HeroApiClient.GetHeroesAsync();
            if (_disposed)
            {
                return;
            }

            Heroes = SortHeroes(heroes);
            ActiveHeroState.SetCurrentHero(Heroes.FirstOrDefault(hero => hero.IsActive));
        }
        catch (HttpRequestException)
        {
            if (!_disposed)
            {
                _heroesLoadFailed = true;
                LoadErrorMessage = "Gespeicherte Helden konnten nicht geladen werden.";
            }
        }
        catch (Exception)
        {
            if (!_disposed)
            {
                _heroesLoadFailed = true;
                LoadErrorMessage = "Beim Laden der gespeicherten Helden ist ein Fehler aufgetreten.";
            }
        }
        finally
        {
            _isLoadingHeroes = false;
        }
    }

    protected void LoadFiles(InputFileChangeEventArgs e)
    {
        ClearDragClass();

        if (IsBusy)
        {
            return;
        }

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (e.FileCount > MaxAllowedFiles)
        {
            SelectedFiles = Array.Empty<IBrowserFile>();
            ErrorMessage = $"Maximal {MaxAllowedFiles} Dateien können gleichzeitig ausgewählt werden.";
            return;
        }

        var files = e.GetMultipleFiles(MaxAllowedFiles);
        var validFiles = new List<IBrowserFile>();
        var invalidExtensionFiles = new List<string>();
        var oversizedFiles = new List<string>();

        foreach (var file in files)
        {
            if (!HasValidExtension(file))
            {
                invalidExtensionFiles.Add(file.Name);
                continue;
            }

            if (file.Size > MaxFileSize)
            {
                oversizedFiles.Add(file.Name);
                continue;
            }

            validFiles.Add(file);
        }

        SelectedFiles = validFiles;
        ErrorMessage = BuildValidationMessage(invalidExtensionFiles, oversizedFiles, validFiles.Count);
    }

    protected async Task UploadFilesAsync()
    {
        if (_isBusy || !SelectedFiles.Any() || _disposed)
        {
            return;
        }

        var files = SelectedFiles.ToArray();
        _isBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        try
        {
            var uploadedHeroes = await HeroApiClient.UploadHeroesAsync(files, MaxFileSize);
            if (_disposed)
            {
                return;
            }

            Heroes.AddRange(uploadedHeroes);
            Heroes = SortHeroes(Heroes);
            SelectedFiles = Array.Empty<IBrowserFile>();
            StatusMessage = uploadedHeroes.Count == 1
                ? "1 Held wurde importiert."
                : $"{uploadedHeroes.Count} Helden wurden importiert.";
        }
        catch (HttpRequestException exception)
        {
            if (!_disposed)
            {
                ErrorMessage = string.IsNullOrWhiteSpace(exception.Message)
                    ? "Fehler beim Importieren der Helden. Die Auswahl bleibt erhalten."
                    : $"{exception.Message} Die Auswahl bleibt erhalten.";
            }
        }
        catch (Exception)
        {
            if (!_disposed)
            {
                ErrorMessage = "Ein unerwarteter Fehler ist beim Importieren aufgetreten. Die Auswahl bleibt erhalten.";
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    protected async Task SetActiveHeroAsync(Hero hero)
    {
        if (_isBusy || hero.IsActive || _disposed)
        {
            return;
        }

        _isBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        try
        {
            var activeHero = await HeroApiClient.SetActiveHeroAsync(hero.Id);
            if (_disposed)
            {
                return;
            }

            foreach (var existingHero in Heroes)
            {
                existingHero.IsActive = existingHero.Id == activeHero.Id;
            }

            Heroes = SortHeroes(Heroes);
            ActiveHeroState.SetCurrentHero(activeHero);
            StatusMessage = $"„{activeHero.Name}“ ist jetzt aktiv.";
        }
        catch (HttpRequestException)
        {
            if (!_disposed)
            {
                ErrorMessage = "Aktiver Held konnte nicht gesetzt werden.";
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    protected void BeginRemoveHero(Hero hero)
    {
        if (IsBusy)
        {
            return;
        }

        PendingDeleteHeroId = hero.Id;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
    }

    protected void CancelRemoveHero()
    {
        PendingDeleteHeroId = null;
    }

    protected async Task ConfirmRemoveHeroAsync(Guid heroId)
    {
        if (_isBusy || _disposed)
        {
            return;
        }

        var hero = Heroes.FirstOrDefault(existingHero => existingHero.Id == heroId);
        if (hero is null)
        {
            PendingDeleteHeroId = null;
            return;
        }

        _isBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        try
        {
            await HeroApiClient.DeleteHeroAsync(heroId);
            if (_disposed)
            {
                return;
            }

            Heroes.RemoveAll(existingHero => existingHero.Id == heroId);
            ActiveHeroState.ClearIfMatches(heroId);
            PendingDeleteHeroId = null;
            StatusMessage = $"Held „{hero.Name}“ wurde entfernt.";
        }
        catch (HttpRequestException)
        {
            if (!_disposed)
            {
                ErrorMessage = "Held konnte auf dem Server nicht entfernt werden.";
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    protected void SetDragClass()
    {
        if (!IsBusy)
        {
            DragClass = "drag-active";
        }
    }

    protected void ClearDragClass()
    {
        DragClass = string.Empty;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _dropZoneRegistered || _disposed)
        {
            return;
        }

        _dropZoneModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/hero-dropzone.js");
        if (_disposed)
        {
            return;
        }

        await _dropZoneModule.InvokeVoidAsync("registerHeroDropZone", DropZoneElement);
        _dropZoneRegistered = true;
    }

    private static List<Hero> SortHeroes(IEnumerable<Hero> heroes)
    {
        return heroes
            .OrderByDescending(hero => hero.IsActive)
            .ThenBy(hero => hero.Name)
            .ToList();
    }

    private static bool HasValidExtension(IBrowserFile file)
    {
        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        return ext == ".xml" || ext == ".zip" || ext == ".hld";
    }

    private static string BuildValidationMessage(
        IReadOnlyCollection<string> invalidExtensionFiles,
        IReadOnlyCollection<string> oversizedFiles,
        int validFileCount)
    {
        var messages = new List<string>();

        if (invalidExtensionFiles.Count > 0)
        {
            messages.Add($"Ungültige Dateiendung: {string.Join(", ", invalidExtensionFiles)}");
        }

        if (oversizedFiles.Count > 0)
        {
            messages.Add($"Zu groß (max 5 MiB): {string.Join(", ", oversizedFiles)}");
        }

        if (messages.Count == 0)
        {
            return string.Empty;
        }

        var prefix = validFileCount > 0 ? "Ignoriert" : "Keine Datei akzeptiert";
        return $"{prefix}: {string.Join(" | ", messages)}";
    }
}
