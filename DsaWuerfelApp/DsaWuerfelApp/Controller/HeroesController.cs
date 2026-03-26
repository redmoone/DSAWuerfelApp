using DsaWuerfelApp.Core.Mappers;
using DsaWuerfelApp.Services;

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
    public IActionResult UploadHero(IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest();
        }

        using var stream = file.OpenReadStream();
        var dto = _xmlDeserializer.Deserialize(stream);
        var hero = HeroMapper.Map(dto);

        return Ok();
    }
}