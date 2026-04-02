using System.Security.Claims;

using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Services.Auth;
using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DsaWuerfelApp.Controller;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(MagicLinkService magicLinkService) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("me")]
    public ActionResult<AuthSessionDto> GetSession()
    {
        return Ok(BuildSessionDto(User));
    }

    [AllowAnonymous]
    [HttpPost("magic-link/request")]
    public async Task<ActionResult<MagicLinkRequestResultDto>> RequestMagicLink(
        [FromBody] MagicLinkRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            await magicLinkService.RequestMagicLinkAsync(
                request.Email,
                request.RedirectPath,
                BuildBaseUrl(),
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);

            return Ok(new MagicLinkRequestResultDto(
                "Wenn die Adresse gueltig ist, wurde ein Magic Link verschickt."));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [AllowAnonymous]
    [HttpGet("/auth/magic-link/verify")]
    public async Task<IActionResult> VerifyMagicLink(
        [FromQuery] string token,
        CancellationToken cancellationToken)
    {
        var verificationResult = await magicLinkService.VerifyAsync(token, cancellationToken);
        if (verificationResult is null)
        {
            return Redirect("/?auth=invalid");
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            BuildPrincipal(verificationResult.User),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) });

        return Redirect(verificationResult.RedirectPath);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    private string BuildBaseUrl()
    {
        return $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
    }

    private static ClaimsPrincipal BuildPrincipal(AuthUser user)
    {
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? MagicLinkService.BuildDefaultDisplayName(user.Email)
            : user.DisplayName;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString("N")),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, displayName)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    private static AuthSessionDto BuildSessionDto(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return new AuthSessionDto(false, null);
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(ClaimTypes.Email);
        var displayName = principal.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(displayName))
        {
            return new AuthSessionDto(false, null);
        }

        return new AuthSessionDto(true, new AuthUserDto(userId, email, displayName));
    }
}