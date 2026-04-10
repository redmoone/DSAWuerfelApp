using System.Diagnostics;

using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DsaWuerfelApp.Controllers;

[ApiController]
[Authorize]
[Route("api/dice")]
public class DiceController(DiceWorkflowService workflow) : ControllerBase
{
    [HttpGet("context")]
    public async Task<ActionResult<DicePageContextDto>> GetContext(
        [FromQuery] Guid? heroId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await workflow.GetContextAsync(heroId, cancellationToken);
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
        [FromQuery] string? badTraitName = null,
        [FromQuery] string[]? spellOptionValue = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result =
                await workflow.GetProbeInfoAsync(
                    new ProbeInfoRequestDto(heroId, probeValue, modifier, badTraitName, spellOptionValue ?? []),
                    cancellationToken);
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
    public async Task<ActionResult<TalentRollResultDto>> RollTalent(
        [FromBody] TalentRollRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await workflow.RollTalentAsync(request, cancellationToken: cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("attribute-roll")]
    public async Task<ActionResult<AttributeRollResultDto>> RollAttribute(
        [FromBody] AttributeRollRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await workflow.RollAttributeAsync(request, cancellationToken: cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("bad-trait-roll")]
    public async Task<ActionResult<BadTraitRollResultDto>> RollBadTrait(
        [FromBody] BadTraitRollRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await workflow.RollBadTraitAsync(request, cancellationToken: cancellationToken);
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