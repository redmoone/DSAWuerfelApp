using DsaWuerfelApp.Core.Mappers;
using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Mvc;

namespace DsaWuerfelApp.Controller;

[ApiController]
[Route("api/[controller]")]
public class HeroesController : ControllerBase
{
    private readonly XmlHeroDeserializer _xmlDeserializer;

    public HeroesController(XmlHeroDeserializer xmlDeserializer)
    {
        _xmlDeserializer = xmlDeserializer;
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public IActionResult UploadHeroes(List<IFormFile> files)
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

            using var stream = file.OpenReadStream();
            var dto = _xmlDeserializer.Deserialize(stream);
            var hero = HeroMapper.Map(dto);

            if (hero.Id == Guid.Empty)
            {
                hero.Id = Guid.NewGuid();
            }

            createdHeroes.Add(hero);
        }

        return Ok(createdHeroes);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult DeleteHero(Guid id)
    {
        return Ok();
    }
}