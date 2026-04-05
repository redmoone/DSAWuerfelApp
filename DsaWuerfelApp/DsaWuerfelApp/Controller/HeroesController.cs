using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Services;
using DsaWuerfelApp.Services.Application.Import;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DsaWuerfelApp.Controller;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class HeroesController(HeroDbContext dbContext, HeroImportService heroImportService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Hero>>> GetHeroes() =>
        Ok(await dbContext.Heroes
            .AsNoTracking()
            .OrderByDescending(hero => hero.IsActive)
            .ThenBy(hero => hero.Name)
            .ToListAsync());

    [HttpGet("active")]
    public async Task<ActionResult<Hero?>> GetActiveHero() =>
        Ok(await dbContext.Heroes
            .AsNoTracking()
            .FirstOrDefaultAsync(hero => hero.IsActive));

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadHeroes(List<IFormFile> files, CancellationToken cancellationToken)
    {
        try
        {
            var createdHeroes = await heroImportService.ImportAsync(files, cancellationToken);
            return Ok(createdHeroes);
        }
        catch (HeroImportException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteHero(Guid id)
    {
        var hero = await dbContext.Heroes.FindAsync(id);
        if (hero is null)
        {
            return NotFound();
        }

        dbContext.Heroes.Remove(hero);
        await dbContext.SaveChangesAsync();

        return Ok();
    }

    [HttpPut("{id:guid}/activate")]
    public async Task<ActionResult<Hero>> ActivateHero(Guid id)
    {
        var hero = await dbContext.Heroes.FirstOrDefaultAsync(existingHero => existingHero.Id == id);
        if (hero is null)
        {
            return NotFound();
        }

        await dbContext.Heroes
            .Where(existingHero => existingHero.IsActive && existingHero.Id != id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(existingHero => existingHero.IsActive, false));

        hero.IsActive = true;
        await dbContext.SaveChangesAsync();

        return Ok(hero);
    }
}