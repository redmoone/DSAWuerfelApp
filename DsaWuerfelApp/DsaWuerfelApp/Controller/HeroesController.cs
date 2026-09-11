using System.Security.Claims;

using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Services;
using DsaWuerfelApp.Services.Application.Import;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DsaWuerfelApp.Controller;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class HeroesController(
    HeroDbContext dbContext,
    HeroImportService heroImportService,
    HeroCombatProfileReader heroCombatProfileReader) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Hero>>> GetHeroes()
    {
        var userId = GetRequiredUserId();

        return Ok(await dbContext.Heroes
            .AsNoTracking()
            .Where(hero => hero.OwnerUserId == userId)
            .OrderByDescending(hero => hero.IsActive)
            .ThenBy(hero => hero.Name)
            .ToListAsync());
    }

    [HttpGet("active")]
    public async Task<ActionResult<Hero?>> GetActiveHero()
    {
        var userId = GetRequiredUserId();

        return Ok(await dbContext.Heroes
            .AsNoTracking()
            .FirstOrDefaultAsync(hero => hero.OwnerUserId == userId && hero.IsActive));
    }

    [HttpGet("{id:guid}/combat-profile")]
    public async Task<ActionResult<CombatProfileDto>> GetCombatProfile(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await heroCombatProfileReader.ReadAsync(id, GetRequiredUserId(), cancellationToken);
            return profile is null ? NotFound() : Ok(profile);
        }
        catch (HeroCombatProfileException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadHeroes(List<IFormFile> files, CancellationToken cancellationToken)
    {
        try
        {
            var createdHeroes = await heroImportService.ImportAsync(GetRequiredUserId(), files, cancellationToken);
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
        var userId = GetRequiredUserId();
        var hero = await dbContext.Heroes.FirstOrDefaultAsync(existingHero =>
            existingHero.Id == id && existingHero.OwnerUserId == userId);
        if (hero is null)
        {
            return NotFound();
        }

        dbContext.Heroes.Remove(hero);
        await dbContext.SaveChangesAsync();

        return Ok();
    }

    [HttpPut("{id:guid}/activate")]
    public async Task<ActionResult<Hero>> ActivateHero(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = GetRequiredUserId();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var hero = await dbContext.Heroes.AsNoTracking().FirstOrDefaultAsync(existingHero =>
            existingHero.Id == id && existingHero.OwnerUserId == userId, cancellationToken);
        if (hero is null)
        {
            return NotFound();
        }

        await dbContext.Heroes
            .Where(existingHero => existingHero.OwnerUserId == userId && existingHero.IsActive && existingHero.Id != id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(existingHero => existingHero.IsActive, false), cancellationToken);

        var activated = await dbContext.Heroes
            .Where(existingHero => existingHero.Id == id && existingHero.OwnerUserId == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(existingHero => existingHero.IsActive, true), cancellationToken);
        if (activated != 1) throw new InvalidOperationException("Aktivierung konnte nicht abgeschlossen werden.");

        hero = await dbContext.Heroes.AsNoTracking().SingleAsync(existingHero => existingHero.Id == id && existingHero.OwnerUserId == userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(hero);
    }

    private string GetRequiredUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ??
               throw new InvalidOperationException("Benutzer ist nicht authentifiziert.");
    }
}
