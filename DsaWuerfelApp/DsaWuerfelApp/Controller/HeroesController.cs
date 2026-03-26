using System.Xml;

using DsaWuerfelApp.Core.Dtos;
using DsaWuerfelApp.Core.Mappers;
using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DsaWuerfelApp.Controller;

[ApiController]
[Route("api/[controller]")]
public class HeroesController : ControllerBase
{
    private readonly HeroDbContext _dbContext;
    private readonly XmlHeroDeserializer _xmlDeserializer;

    public HeroesController(HeroDbContext dbContext, XmlHeroDeserializer xmlDeserializer)
    {
        _dbContext = dbContext;
        _xmlDeserializer = xmlDeserializer;
    }

    [HttpGet]
    public async Task<ActionResult<List<Hero>>> GetHeroes()
    {
        var heroes = await _dbContext.Heroes
            .AsNoTracking()
            .OrderBy(hero => hero.Name)
            .ToListAsync();

        return Ok(heroes);
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadHeroes(List<IFormFile> files)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest();
        }

        var createdHeroes = new List<Hero>();

        foreach (var file in files)
        {
            if (file.Length == 0)
            {
                continue;
            }

            if (!string.Equals(Path.GetExtension(file.FileName), ".xml", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest($"Datei '{file.FileName}' hat keine .xml-Endung.");
            }

            HeldenDatenDto dto;

            try
            {
                using var stream = file.OpenReadStream();
                dto = _xmlDeserializer.Deserialize(stream);
            }
            catch (InvalidOperationException)
            {
                return BadRequest($"Datei '{file.FileName}' enthaelt kein gueltiges XML.");
            }
            catch (XmlException)
            {
                return BadRequest($"Datei '{file.FileName}' enthaelt kein gueltiges XML.");
            }

            var hero = HeroMapper.Map(dto);

            if (hero.Id == Guid.Empty)
            {
                hero.Id = Guid.NewGuid();
            }

            createdHeroes.Add(hero);
        }

        if (createdHeroes.Count == 0)
        {
            return BadRequest("Keine gueltigen Dateien zum Import gefunden.");
        }

        await _dbContext.Heroes.AddRangeAsync(createdHeroes);
        await _dbContext.SaveChangesAsync();

        return Ok(createdHeroes);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteHero(Guid id)
    {
        var hero = await _dbContext.Heroes.FindAsync(id);
        if (hero is null)
        {
            return NotFound();
        }

        _dbContext.Heroes.Remove(hero);
        await _dbContext.SaveChangesAsync();

        return Ok();
    }
}