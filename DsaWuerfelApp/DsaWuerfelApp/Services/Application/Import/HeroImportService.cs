using System.IO.Compression;
using System.Net.Http.Headers;

using DsaWuerfelApp.Core.Mappers;
using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services.Application.Import;

public sealed class HeroImportService(
    HeroDbContext dbContext,
    XmlHeroDeserializer xmlHeroDeserializer,
    HeroMapper heroMapper,
    IHttpClientFactory httpClientFactory)
{
    public async Task<IReadOnlyList<Hero>> ImportAsync(
        IReadOnlyCollection<IFormFile>? files,
        CancellationToken cancellationToken = default)
    {
        if (files is null || files.Count == 0)
        {
            throw new HeroImportException("Keine Dateien zum Import übergeben.");
        }

        using var zipMemoryStream = new MemoryStream();
        using (var archive = new ZipArchive(zipMemoryStream, ZipArchiveMode.Create, true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.FileName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var fileStream = file.OpenReadStream();
                await fileStream.CopyToAsync(entryStream, cancellationToken);
            }
        }

        zipMemoryStream.Position = 0;

        var httpClient = httpClientFactory.CreateClient("JavaMicroservice");
        using var content = new StreamContent(zipMemoryStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync("/api/convert", content, cancellationToken);
        }
        catch (HttpRequestException)
        {
            var target = httpClient.BaseAddress is null
                ? "das konfigurierte Java-Backend"
                : httpClient.BaseAddress.ToString().TrimEnd('/');

            throw new HeroImportException(
                $"Das Java-Backend ist nicht erreichbar ({target}). Pruefe, ob der Helden-Microservice laeuft.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HeroImportException("Das Java-Backend hat nicht rechtzeitig geantwortet.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = await response.Content.ReadAsStringAsync(cancellationToken);
            errorMessage = string.IsNullOrWhiteSpace(errorMessage)
                ? "Das Java-Backend konnte die Dateien nicht verarbeiten."
                : errorMessage.Trim();

            throw new HeroImportException(errorMessage);
        }

        await using var enrichedXmlStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var deserializedHeroes = xmlHeroDeserializer.DeserializeMultiple(enrichedXmlStream);

        if (deserializedHeroes.Count == 0)
        {
            throw new HeroImportException("Keine gültigen Helden in den Dateien gefunden.");
        }

        var createdHeroes = new List<Hero>();
        foreach (var (dto, rawXml) in deserializedHeroes)
        {
            var hero = heroMapper.Map(dto);
            hero.Id = Guid.NewGuid();
            hero.SourceXml = rawXml;
            hero.SourceFileName = $"{hero.Name}.xml";
            hero.ImportVersion = HeroImportVersioning.CurrentVersion;
            hero.ImportedAtUtc = DateTime.UtcNow;
            createdHeroes.Add(hero);
        }

        await dbContext.Heroes.AddRangeAsync(createdHeroes, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return createdHeroes;
    }

    public Hero Reimport(Hero existingHero)
    {
        ArgumentNullException.ThrowIfNull(existingHero);

        if (existingHero.SourceXml is null || existingHero.SourceXml.Length == 0)
        {
            throw new HeroImportException($"Held '{existingHero.Name}' besitzt kein gespeichertes Import-XML.");
        }

        using var stream = new MemoryStream(existingHero.SourceXml, writable: false);
        var deserializedHeroes = xmlHeroDeserializer.DeserializeMultiple(stream);

        if (deserializedHeroes.Count == 0)
        {
            throw new HeroImportException("Kein gültiges XML für den Reimport gefunden.");
        }

        var importedHero = heroMapper.Map(deserializedHeroes[0].Dto);

        existingHero.Name = importedHero.Name;
        existingHero.Geschlecht = importedHero.Geschlecht;
        existingHero.Alter = importedHero.Alter;
        existingHero.Eigenschaften = importedHero.Eigenschaften;
        existingHero.SchlechteEigenschaften = importedHero.SchlechteEigenschaften;
        existingHero.Talente = importedHero.Talente;
        existingHero.ImportVersion = HeroImportVersioning.CurrentVersion;
        existingHero.ImportedAtUtc = DateTime.UtcNow;

        return existingHero;
    }
}