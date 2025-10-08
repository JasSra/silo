using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Silo.Api.Services;
using Silo.Core.Models;

namespace Silo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthenticationService authService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Register a new user account
    /// </summary>
    [HttpPost("signup")]
    [AllowAnonymous]
    public async Task<ActionResult<UserDto>> SignUp([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _authService.RegisterAsync(request, cancellationToken);
            _logger.LogInformation("New user registered: {Username}", user.Username);
            return Ok(user);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Authenticate and obtain access token
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            
            var response = await _authService.LoginAsync(request, ipAddress, userAgent, cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Refresh access token using refresh token
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var response = await _authService.RefreshTokenAsync(request, ipAddress, cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Logout and revoke refresh token
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(request.RefreshToken, cancellationToken);
        return Ok(new { message = "Logged out successfully" });
    }

    /// <summary>
    /// Get current user information
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public ActionResult<object> GetCurrentUser()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var username = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var tenantId = User.FindFirst("tenant_id")?.Value;
        var roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToArray();
        var permissions = User.FindAll("permission").Select(c => c.Value).ToArray();

        return Ok(new
        {
            userId,
            username,
            email,
            tenantId,
            roles,
            permissions
        });
    }

    /// <summary>
    /// Revoke all sessions for current user
    /// </summary>
    [HttpPost("revoke-all-sessions")]
    [Authorize]
    public async Task<IActionResult> RevokeAllSessions(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        await _authService.RevokeAllUserSessionsAsync(userId, cancellationToken);
        return Ok(new { message = "All sessions revoked successfully" });
    }
}
