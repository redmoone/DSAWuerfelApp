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

    [Inject] private ActiveHeroState ActiveHeroState { get; set; } = default!;
    [Inject] private IHeroApiClient HeroApiClient { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    protected List<Hero> Heroes { get; set; } = new();
    protected IReadOnlyList<IBrowserFile> SelectedFiles { get; set; } = Array.Empty<IBrowserFile>();
    protected Hero? SelectedHero { get; set; }
    protected string ErrorMessage { get; set; } = string.Empty;
    protected string DragClass { get; set; } = string.Empty;
    protected ElementReference DropZoneElement { get; set; }

    public async ValueTask DisposeAsync()
    {
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
        try
        {
            Heroes = await HeroApiClient.GetHeroesAsync();
            Heroes = Heroes
                .OrderByDescending(hero => hero.IsActive)
                .ThenBy(hero => hero.Name)
                .ToList();
            ActiveHeroState.SetCurrentHero(Heroes.FirstOrDefault(hero => hero.IsActive));
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Gespeicherte Helden konnten nicht geladen werden.";
        }
    }

    protected void LoadFiles(InputFileChangeEventArgs e)
    {
        ClearDragClass();

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
        ErrorMessage = string.Empty;

        if (!SelectedFiles.Any())
        {
            return;
        }

        try
        {
            var uploadedHeroes = await HeroApiClient.UploadHeroesAsync(SelectedFiles, MaxFileSize);
            Heroes.AddRange(uploadedHeroes);
            Heroes = Heroes
                .OrderByDescending(hero => hero.IsActive)
                .ThenBy(hero => hero.Name)
                .ToList();
            SelectedFiles = Array.Empty<IBrowserFile>();
        }
        catch (HttpRequestException exception)
        {
            ErrorMessage = string.IsNullOrWhiteSpace(exception.Message)
                ? "Fehler bei der Kommunikation mit dem Server."
                : exception.Message;
        }
        catch (Exception)
        {
            ErrorMessage = "Ein unerwarteter Fehler ist beim Hochladen aufgetreten.";
        }
    }

    protected async Task SetActiveHeroAsync(Hero hero)
    {
        try
        {
            var activeHero = await HeroApiClient.SetActiveHeroAsync(hero.Id);

            foreach (var existingHero in Heroes)
            {
                existingHero.IsActive = existingHero.Id == activeHero.Id;
            }

            ActiveHeroState.SetCurrentHero(activeHero);
            Heroes = Heroes
                .OrderByDescending(existingHero => existingHero.IsActive)
                .ThenBy(existingHero => existingHero.Name)
                .ToList();
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Aktiver Held konnte nicht gesetzt werden.";
        }
    }

    protected async Task RemoveHeroAsync(Hero hero)
    {
        try
        {
            await HeroApiClient.DeleteHeroAsync(hero.Id);
            Heroes.Remove(hero);
            ActiveHeroState.ClearIfMatches(hero.Id);

            if (SelectedHero?.Id == hero.Id)
            {
                SelectedHero = null;
            }
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Fehler beim Löschen des Helden auf dem Server.";
        }
    }

    protected void SelectHero(Hero hero)
    {
        SelectedHero = hero;
    }

    protected void SetDragClass()
    {
        DragClass = "drag-active";
    }

    protected void ClearDragClass()
    {
        DragClass = string.Empty;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _dropZoneRegistered)
        {
            return;
        }

        _dropZoneModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/hero-dropzone.js");
        await _dropZoneModule.InvokeVoidAsync("registerHeroDropZone", DropZoneElement);
        _dropZoneRegistered = true;
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
            messages.Add($"Zu groß (max 5MB): {string.Join(", ", oversizedFiles)}");
        }

        if (messages.Count == 0)
        {
            return string.Empty;
        }

        var prefix = validFileCount > 0 ? "Ignoriert" : "Keine Datei akzeptiert";
        return $"{prefix}: {string.Join(" | ", messages)}";
    }
}