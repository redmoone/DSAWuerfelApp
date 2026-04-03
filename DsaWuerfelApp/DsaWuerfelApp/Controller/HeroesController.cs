using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Services;
using DsaWuerfelApp.Services.Application.Import;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DsaWuerfelApp.Controller;

[ApiController]
[Route("api/[controller]")]
public class HeroesController : ControllerBase
{
    private readonly HeroDbContext _dbContext;
    private readonly HeroImportService _heroImportService;

    public HeroesController(HeroDbContext dbContext, HeroImportService heroImportService)
    {
        _dbContext = dbContext;
        _heroImportService = heroImportService;
    }

    [HttpGet]
    public async Task<ActionResult<List<Hero>>> GetHeroes()
    {
        var heroes = await _dbContext.Heroes
            .AsNoTracking()
            .OrderByDescending(hero => hero.IsActive)
            .ThenBy(hero => hero.Name)
            .ToListAsync();

        return Ok(heroes);
    }

    [HttpGet("active")]
    public async Task<ActionResult<Hero?>> GetActiveHero()
    {
        var activeHero = await _dbContext.Heroes
            .AsNoTracking()
            .FirstOrDefaultAsync(hero => hero.IsActive);

        return Ok(activeHero);
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadHeroes(List<IFormFile> files, CancellationToken cancellationToken)
    {
        try
        {
            var createdHeroes = await _heroImportService.ImportAsync(files, cancellationToken);
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
        var hero = await _dbContext.Heroes.FindAsync(id);
        if (hero is null)
        {
            return NotFound();
        }

        _dbContext.Heroes.Remove(hero);
        await _dbContext.SaveChangesAsync();

        return Ok();
    }

    [HttpPut("{id:guid}/activate")]
    public async Task<ActionResult<Hero>> ActivateHero(Guid id)
    {
        var hero = await _dbContext.Heroes.FirstOrDefaultAsync(existingHero => existingHero.Id == id);
        if (hero is null)
        {
            return NotFound();
        }

        var activeHeroes = await _dbContext.Heroes
            .Where(existingHero => existingHero.IsActive && existingHero.Id != id)
            .ToListAsync();

        foreach (var activeHero in activeHeroes)
        {
            activeHero.IsActive = false;
        }

        hero.IsActive = true;
        await _dbContext.SaveChangesAsync();

        return Ok(hero);
    }
}