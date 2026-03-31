using System.Xml;

using DsaWuerfelApp.Core.Dtos;
using DsaWuerfelApp.Core.Mappers;
using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed class HeroImportService(
    HeroDbContext dbContext,
    XmlHeroDeserializer xmlHeroDeserializer,
    HeroMapper heroMapper)
{
    public async Task<IReadOnlyList<Hero>> ImportAsync(
        IReadOnlyCollection<IFormFile>? files,
        CancellationToken cancellationToken = default)
    {
        if (files is null || files.Count == 0)
        {
            throw new HeroImportException("Keine Dateien zum Import übergeben.");
        }

        var createdHeroes = new List<Hero>();

        foreach (var file in files)
        {
            var hero = ImportFile(file);
            if (hero is not null)
            {
                createdHeroes.Add(hero);
            }
        }

        if (createdHeroes.Count == 0)
        {
            throw new HeroImportException("Keine gültigen Dateien zum Import gefunden.");
        }

        await dbContext.Heroes.AddRangeAsync(createdHeroes, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return createdHeroes;
    }

    private Hero? ImportFile(IFormFile file)
    {
        if (file.Length == 0)
        {
            return null;
        }

        ValidateExtension(file.FileName);

        using var stream = file.OpenReadStream();
        var dto = Deserialize(file.FileName, stream);
        var hero = heroMapper.Map(dto);

        if (hero.Id == Guid.Empty)
        {
            hero.Id = Guid.NewGuid();
        }

        return hero;
    }

    private HeldenDatenDto Deserialize(string fileName, Stream stream)
    {
        try
        {
            return xmlHeroDeserializer.Deserialize(stream);
        }
        catch (InvalidOperationException)
        {
            throw new HeroImportException($"Datei '{fileName}' enthält kein gültiges XML.");
        }
        catch (XmlException)
        {
            throw new HeroImportException($"Datei '{fileName}' enthält kein gültiges XML.");
        }
    }

    private static void ValidateExtension(string fileName)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new HeroImportException($"Datei '{fileName}' hat keine .xml-Endung.");
        }
    }
}