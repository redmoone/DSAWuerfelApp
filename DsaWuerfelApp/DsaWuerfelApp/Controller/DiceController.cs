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
        var result = await workflow.GetContextAsync(heroId, cancellationToken);
        return Ok(result);
    }

    [HttpGet("catalog-context")]
    public async Task<ActionResult<DicePageContextDto>> GetCatalogContext(CancellationToken cancellationToken)
    {
        var result = await workflow.GetCatalogContextAsync(cancellationToken);
        return Ok(result);
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
        var result =
            await workflow.GetProbeInfoAsync(
                new ProbeInfoRequestDto(heroId, probeValue, modifier, badTraitName, spellOptionValue ?? []),
                cancellationToken);
        return Ok(result);
    }

    [HttpPost("free-roll")]
    public ActionResult<FreeRollResultDto> RollFree([FromBody] FreeRollRequestDto request)
    {
        var result = workflow.RollFree(request);
        return Ok(result);
    }

    [HttpPost("talent-roll")]
    public async Task<ActionResult<TalentRollResultDto>> RollTalent(
        [FromBody] TalentRollRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await workflow.RollTalentAsync(request, cancellationToken: cancellationToken);
        return Ok(result);
    }

    [HttpPost("attribute-roll")]
    public async Task<ActionResult<AttributeRollResultDto>> RollAttribute(
        [FromBody] AttributeRollRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await workflow.RollAttributeAsync(request, cancellationToken: cancellationToken);
        return Ok(result);
    }

    [HttpPost("master-talent-roll")]
    public async Task<ActionResult<MasterTalentRollTargetResultDto[]>> RollMasterTalent(
        [FromBody] MasterTalentRollRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await workflow.RollMasterTalentAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("master-attribute-roll")]
    public async Task<ActionResult<MasterAttributeRollTargetResultDto[]>> RollMasterAttribute(
        [FromBody] MasterAttributeRollRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await workflow.RollMasterAttributeAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("bad-trait-roll")]
    public async Task<ActionResult<BadTraitRollResultDto>> RollBadTrait(
        [FromBody] BadTraitRollRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await workflow.RollBadTraitAsync(request, cancellationToken: cancellationToken);
        return Ok(result);
    }

    [HttpGet("debug-mode")]
    public ActionResult<bool> GetDebugMode()
    {
        return Ok(Debugger.IsAttached);
    }
}
