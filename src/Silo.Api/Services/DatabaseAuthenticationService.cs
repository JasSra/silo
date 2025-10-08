using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Silo.Core.Data;
using Silo.Core.Models;
using BCrypt.Net;

namespace Silo.Api.Services;

public class DatabaseAuthenticationService : IAuthenticationService
{
    private readonly ILogger<DatabaseAuthenticationService> _logger;
    private readonly SiloDbContext _context;
    private readonly AuthConfiguration _config;

    public DatabaseAuthenticationService(
        ILogger<DatabaseAuthenticationService> logger,
        SiloDbContext context,
        AuthConfiguration config)
    {
        _logger = logger;
        _context = context;
        _config = config;
    }

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        // Find user
        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Username == request.Username, cancellationToken);

        if (user == null)
        {
            _logger.LogWarning("Login failed for user {Username}: User not found", request.Username);
            throw new UnauthorizedAccessException("Invalid username or password");
        }

        // Check if account is locked
        if (user.LockedOutUntil.HasValue && user.LockedOutUntil.Value > DateTime.UtcNow)
        {
            _logger.LogWarning("Login failed for user {Username}: Account is locked until {LockedUntil}",
                request.Username, user.LockedOutUntil);
            throw new UnauthorizedAccessException($"Account is locked until {user.LockedOutUntil:u}");
        }

        // Verify password
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            // Increment failed login attempts
            user.FailedLoginAttempts++;
            
            if (user.FailedLoginAttempts >= _config.MaxFailedLoginAttempts)
            {
                user.LockedOutUntil = DateTime.UtcNow.AddMinutes(_config.LockoutDurationMinutes);
                _logger.LogWarning("User {Username} locked out after {Attempts} failed login attempts",
                    request.Username, user.FailedLoginAttempts);
            }

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Login failed for user {Username}: Invalid password", request.Username);
            throw new UnauthorizedAccessException("Invalid username or password");
        }

        // Reset failed login attempts on successful login
        user.FailedLoginAttempts = 0;
        user.LockedOutUntil = null;
        user.LastLoginAt = DateTime.UtcNow;

        // Create session
        var refreshToken = GenerateRefreshToken();
        var session = new UserSession
        {
            UserId = user.Id,
            RefreshToken = refreshToken,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(_config.RefreshTokenExpirationDays)
        };

        _context.UserSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        // Generate access token
        var accessToken = GenerateAccessToken(user);
        var expiresAt = DateTime.UtcNow.AddMinutes(_config.AccessTokenExpirationMinutes);

        var userDto = await MapToUserDto(user);

        _logger.LogInformation("User {Username} logged in successfully", request.Username);

        return new LoginResponse(accessToken, refreshToken, expiresAt, userDto);
    }

    public async Task<LoginResponse> RefreshTokenAsync(
        RefreshTokenRequest request,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.UserSessions
            .Include(s => s.User)
                .ThenInclude(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                        .ThenInclude(r => r.RolePermissions)
                            .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(s => s.RefreshToken == request.RefreshToken, cancellationToken);

        if (session == null || !session.IsActive)
        {
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        var user = session.User;
        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("User account is inactive");
        }

        // Rotate refresh token
        var newRefreshToken = GenerateRefreshToken();
        session.RefreshToken = newRefreshToken;
        session.CreatedAt = DateTime.UtcNow;
        session.ExpiresAt = DateTime.UtcNow.AddDays(_config.RefreshTokenExpirationDays);
        session.IpAddress = ipAddress;

        await _context.SaveChangesAsync(cancellationToken);

        // Generate new access token
        var accessToken = GenerateAccessToken(user);
        var expiresAt = DateTime.UtcNow.AddMinutes(_config.AccessTokenExpirationMinutes);

        var userDto = await MapToUserDto(user);

        return new LoginResponse(accessToken, newRefreshToken, expiresAt, userDto);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var session = await _context.UserSessions
            .FirstOrDefaultAsync(s => s.RefreshToken == refreshToken, cancellationToken);

        if (session != null)
        {
            session.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("User session revoked for refresh token");
        }
    }

    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        // Check if username exists
        if (await _context.Users.AnyAsync(u => u.Username == request.Username, cancellationToken))
        {
            throw new ArgumentException("Username already exists");
        }

        // Check if email exists
        if (await _context.Users.AnyAsync(u => u.Email == request.Email, cancellationToken))
        {
            throw new ArgumentException("Email already exists");
        }

        // Create user
        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = !_config.RequireEmailConfirmation,
            IsActive = true
        };

        _context.Users.Add(user);

        // Assign default User role
        var userRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == "User", cancellationToken);

        if (userRole != null)
        {
            _context.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = userRole.Id,
                AssignedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("New user registered: {Username}", user.Username);

        // Reload user with roles
        user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .FirstAsync(u => u.Id == user.Id, cancellationToken);

        return await MapToUserDto(user);
    }

    public async Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_config.JwtSecretKey);

        try
        {
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _config.JwtIssuer,
                ValidateAudience = true,
                ValidAudience = _config.JwtAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task RevokeAllUserSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var sessions = await _context.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.RevokedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("All sessions revoked for user {UserId}", userId);
    }

    private string GenerateAccessToken(User user)
    {
        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToArray();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .ToArray();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
        };

        // Add tenant claim if applicable
        if (user.TenantId.HasValue)
        {
            claims.Add(new Claim("tenant_id", user.TenantId.Value.ToString()));
        }

        // Add role claims
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // Add permission claims
        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_config.JwtSecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config.JwtIssuer,
            audience: _config.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_config.AccessTokenExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    private async Task<UserDto> MapToUserDto(User user)
    {
        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToArray();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .ToArray();

        return new UserDto(
            user.Id,
            user.Username,
            user.Email,
            user.FirstName,
            user.LastName,
            user.IsActive,
            user.EmailConfirmed,
            user.CreatedAt,
            user.LastLoginAt,
            roles,
            permissions
        );
    }
}
