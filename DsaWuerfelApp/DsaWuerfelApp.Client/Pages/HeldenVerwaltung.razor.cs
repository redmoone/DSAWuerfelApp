using DsaWuerfelApp.Client.Services;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace DsaWuerfelApp.Client.Pages;

public partial class HeldenVerwaltung : ComponentBase
{
    private const long MaxFileSize = 1024 * 1024 * 2;

    [Inject] private IHeroApiClient HeroApiClient { get; set; } = default!;

    protected List<Hero> Heroes { get; set; } = new();
    protected string ErrorMessage { get; set; } = string.Empty;

    protected async Task HandleFileUploadAsync(InputFileChangeEventArgs e)
    {
        ErrorMessage = string.Empty;

        try
        {
            await using var stream = e.File.OpenReadStream(MaxFileSize);
            await HeroApiClient.UploadHeroAsync(stream, e.File.Name);
        }
        catch (IOException)
        {
            ErrorMessage = "Die Datei überschreitet die maximal erlaubte Größe von 2 MB.";
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
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Fehler beim Löschen des Helden auf dem Server.";
        }
    }
}