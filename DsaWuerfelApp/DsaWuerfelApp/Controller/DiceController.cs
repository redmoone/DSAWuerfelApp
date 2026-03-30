using System.Diagnostics;

using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Mvc;

namespace DsaWuerfelApp.Controllers;

[ApiController]
[Route("api/dice")]
public class DiceController(DiceWorkflowService workflow) : ControllerBase
{
    [HttpGet("context")]
    public async Task<ActionResult<DicePageContextDto>> GetContext([FromQuery] Guid? heroId)
    {
        try
        {
            var result = await workflow.GetContextAsync(heroId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("probe-info")]
    public async Task<ActionResult<ProbeInfoResultDto>> GetProbeInfo(
        [FromQuery] Guid? heroId,
        [FromQuery] string probeValue,
        [FromQuery] int modifier = 0,
        [FromQuery] string? badTraitName = null)
    {
        try
        {
            var result =
                await workflow.GetProbeInfoAsync(new ProbeInfoRequestDto(heroId, probeValue, modifier, badTraitName));
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("free-roll")]
    public ActionResult<FreeRollResultDto> RollFree([FromBody] FreeRollRequestDto request)
    {
        try
        {
            var result = workflow.RollFree(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("talent-roll")]
    public async Task<ActionResult<TalentRollResultDto>> RollTalent([FromBody] TalentRollRequestDto request)
    {
        try
        {
            var result = await workflow.RollTalentAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("attribute-roll")]
    public async Task<ActionResult<AttributeRollResultDto>> RollAttribute([FromBody] AttributeRollRequestDto request)
    {
        try
        {
            var result = await workflow.RollAttributeAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("bad-trait-roll")]
    public async Task<ActionResult<BadTraitRollResultDto>> RollBadTrait([FromBody] BadTraitRollRequestDto request)
    {
        try
        {
            var result = await workflow.RollBadTraitAsync(request);
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