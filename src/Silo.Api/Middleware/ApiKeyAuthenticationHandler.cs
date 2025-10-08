using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Silo.Core.Data;
using Silo.Core.Models;
using BCrypt.Net;

namespace Silo.Api.Middleware;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultSchemeName = "ApiKey";
    public string HeaderName { get; set; } = "X-API-Key";
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly SiloDbContext _context;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        SiloDbContext context) : base(options, logger, encoder)
    {
        _context = context;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var apiKeyHeaderValues))
        {
            return AuthenticateResult.NoResult();
        }

        var providedApiKey = apiKeyHeaderValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            return AuthenticateResult.NoResult();
        }

        // Find matching key by verifying hash
        TenantApiKey? matchedKey = null;
        var keys = await _context.TenantApiKeys
            .Include(k => k.Tenant)
            .Where(k => k.IsActive && 
                (k.ExpiresAt == null || k.ExpiresAt > DateTime.UtcNow) &&
                k.RevokedAt == null)
            .ToListAsync();

        foreach (var key in keys)
        {
            try
            {
                if (BCrypt.Net.BCrypt.Verify(providedApiKey, key.KeyHash))
                {
                    matchedKey = key;
                    break;
                }
            }
            catch
            {
                // Continue to next key
            }
        }

        if (matchedKey == null)
        {
            return AuthenticateResult.Fail("Invalid API key");
        }

        // Update last used timestamp
        matchedKey.LastUsedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Create claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, matchedKey.TenantId.ToString()),
            new("tenant_id", matchedKey.TenantId.ToString()),
            new("api_key_id", matchedKey.Id.ToString()),
            new(ClaimTypes.Name, matchedKey.Tenant.Name),
        };

        // Add scope/permission claims
        if (!string.IsNullOrWhiteSpace(matchedKey.Scopes))
        {
            var scopes = matchedKey.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var scope in scopes)
            {
                claims.Add(new Claim("permission", scope.Trim()));
            }
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
