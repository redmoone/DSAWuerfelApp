using System.Net.Http.Headers;

namespace DsaWuerfelApp.Client.Services;

public class HeroApiClient : IHeroApiClient
{
    private readonly HttpClient _httpClient;

    public HeroApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task UploadHeroAsync(Stream fileStream, string fileName)
    {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);

        streamContent.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
        content.Add(streamContent, "file", fileName);

        var response = await _httpClient.PostAsync("api/heroes/upload", content);

        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteHeroAsync(Guid heroId)
    {
        var response = await _httpClient.DeleteAsync($"api/heroes/{heroId}");

        response.EnsureSuccessStatusCode();
    }
}