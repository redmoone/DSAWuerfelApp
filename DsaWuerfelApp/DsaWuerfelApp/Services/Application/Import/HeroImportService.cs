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
            var hero = await ImportFileAsync(file, cancellationToken);
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

    public Hero Reimport(Hero existingHero)
    {
        ArgumentNullException.ThrowIfNull(existingHero);

        if (existingHero.SourceXml is not { Length: > 0 })
        {
            throw new HeroImportException($"Held '{existingHero.Name}' besitzt kein gespeichertes Import-XML.");
        }

        using var stream = new MemoryStream(existingHero.SourceXml, writable: false);
        var dto = Deserialize(existingHero.SourceFileName ?? existingHero.Name, stream);
        var importedHero = heroMapper.Map(dto);

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

    private async Task<Hero?> ImportFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return null;
        }

        ValidateExtension(file.FileName);

        await using var sourceStream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await sourceStream.CopyToAsync(buffer, cancellationToken);

        var xmlBytes = buffer.ToArray();
        using var deserializeStream = new MemoryStream(xmlBytes, writable: false);
        var dto = Deserialize(file.FileName, deserializeStream);
        var hero = heroMapper.Map(dto);

        if (hero.Id == Guid.Empty)
        {
            hero.Id = Guid.NewGuid();
        }

        hero.SourceXml = xmlBytes;
        hero.SourceFileName = file.FileName;
        hero.ImportVersion = HeroImportVersioning.CurrentVersion;
        hero.ImportedAtUtc = DateTime.UtcNow;

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