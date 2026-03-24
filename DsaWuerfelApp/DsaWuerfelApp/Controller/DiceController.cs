using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Mvc;

namespace DsaWuerfelApp.Controllers;

[ApiController]
[Route("api/dice")]
public class DiceController : ControllerBase
{
    private readonly DiceService _dice;
    public DiceController(DiceService dice) => _dice = dice;

    [HttpPost("rollset")]
    public ActionResult RollSet([FromBody] RollSetRequest req)
    {
        try
        {
            var result = _dice.RollSet(req.Dice, req.Modifier);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public record RollSetRequest(List<DiceGroup> Dice, int Modifier);