namespace DsaWuerfelApp.Client.Services;

public interface IHeroApiClient
{
    Task UploadHeroAsync(Stream fileStream, string fileName);
    Task DeleteHeroAsync(Guid heroId);
}