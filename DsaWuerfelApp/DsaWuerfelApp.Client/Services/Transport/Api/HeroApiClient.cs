using System.Net.Http.Headers;
using System.Net.Http.Json;

using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Components.Forms;

namespace DsaWuerfelApp.Client.Services;

public interface IHeroApiClient
{
    Task<Hero?> GetActiveHeroAsync();
    Task<List<Hero>> GetHeroesAsync();
    Task<Hero> SetActiveHeroAsync(Guid heroId);
    Task<List<Hero>> UploadHeroesAsync(IReadOnlyList<IBrowserFile> files, long maxFileSize);
    Task DeleteHeroAsync(Guid heroId);
}

public class HeroApiClient : IHeroApiClient
{
    private readonly HttpClient _httpClient;

    public HeroApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<Hero>> GetHeroesAsync()
    {
        var heroes = await _httpClient.GetFromJsonAsync<List<Hero>>("api/heroes");
        return heroes ?? new List<Hero>();
    }

    public async Task<Hero?> GetActiveHeroAsync()
    {
        return await _httpClient.GetFromJsonAsync<Hero?>("api/heroes/active");
    }

    public async Task<Hero> SetActiveHeroAsync(Guid heroId)
    {
        var response = await _httpClient.PutAsync($"api/heroes/{heroId}/activate", null);
        response.EnsureSuccessStatusCode();

        var hero = await response.Content.ReadFromJsonAsync<Hero>();
        return hero ?? throw new HttpRequestException("Aktiver Held konnte nicht gelesen werden.");
    }

    public async Task<List<Hero>> UploadHeroesAsync(IReadOnlyList<IBrowserFile> files, long maxFileSize)
    {
        using var content = new MultipartFormDataContent();

        foreach (var file in files)
        {
            var streamContent = new StreamContent(file.OpenReadStream(maxFileSize));
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
            content.Add(streamContent, "files", file.Name);
        }

        var response = await _httpClient.PostAsync("api/heroes/upload", content);
        response.EnsureSuccessStatusCode();

        var heroes = await response.Content.ReadFromJsonAsync<List<Hero>>();
        return heroes ?? new List<Hero>();
    }

    public async Task DeleteHeroAsync(Guid heroId)
    {
        var response = await _httpClient.DeleteAsync($"api/heroes/{heroId}");
        response.EnsureSuccessStatusCode();
    }
}