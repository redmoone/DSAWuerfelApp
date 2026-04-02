using System.Security.Claims;

using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DsaWuerfelApp.Controller;

[ApiController]
[Authorize]
[Route("api/sessions")]
public sealed class SessionsController(SessionService sessionService) : ControllerBase
{
    [HttpGet("mine")]
    public ActionResult<SessionSummaryDto[]> GetMySessions()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var sessions = sessionService.GetSessionsForUser(userId).ToArray();
        return Ok(sessions);
    }

    [HttpGet("{sessionId}")]
    public ActionResult<SessionDetailsDto> GetSession(string sessionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var session = sessionService.GetSessionDetails(sessionId, userId);
            return Ok(session);
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }
}