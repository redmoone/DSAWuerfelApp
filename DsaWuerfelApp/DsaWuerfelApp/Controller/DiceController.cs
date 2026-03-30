using System.Diagnostics;

using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Mvc;

namespace DsaWuerfelApp.Controllers;

[ApiController]
[Route("api/dice")]
public class DiceController : ControllerBase
{
    private readonly DiceService _dice;
    private readonly TalentProbeService _talentProbes;

    public DiceController(DiceService dice, TalentProbeService talentProbes)
    {
        _dice = dice;
        _talentProbes = talentProbes;
    }

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

    [HttpPost("talentprobe")]
    public ActionResult<TalentProbeResult> RollTalentProbe([FromBody] TalentProbeRequest req)
    {
        try
        {
            var result = _talentProbes.RollTalentProbe(req);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("debug-mode")]
    public ActionResult<bool> GetDebugMode()
    {
        return Ok(Debugger.IsAttached);
    }
}

public record RollSetRequest(List<DiceGroup> Dice, int Modifier);