using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace DsaWuerfelApp.Client.Pages;

public partial class HeldenVerwaltung : ComponentBase, IAsyncDisposable
{
    private const long MaxFileSize = 1024 * 1024 * 2;
    private const int MaxAllowedFiles = 15;

    private IJSObjectReference? _dropZoneModule;
    private bool _dropZoneRegistered;

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

    protected void LoadFiles(InputFileChangeEventArgs e)
    {
        Console.WriteLine($"LoadFiles fired. Count: {e.FileCount}");
        ErrorMessage = string.Empty;
        ClearDragClass();
        SelectedFiles = e.GetMultipleFiles(MaxAllowedFiles);
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
            SelectedFiles = Array.Empty<IBrowserFile>();
        }
        catch (IOException)
        {
            ErrorMessage = "Eine oder mehrere Dateien überschreiten die maximal erlaubte Größe von 2 MB.";
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Fehler bei der Kommunikation mit dem Server.";
        }
        catch (Exception)
        {
            ErrorMessage = "Ein unerwarteter Fehler ist beim Hochladen aufgetreten.";
        }
    }

    protected async Task RemoveHeroAsync(Hero hero)
    {
        try
        {
            await HeroApiClient.DeleteHeroAsync(hero.Id);
            Heroes.Remove(hero);

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

    protected async Task OpenFilePickerAsync()
    {
        if (_dropZoneModule is null)
        {
            return;
        }

        await _dropZoneModule.InvokeVoidAsync("openFilePicker", DropZoneElement);
    }
}