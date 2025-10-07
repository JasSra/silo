using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Silo.Core.Models;

namespace Silo.Api.Services;

public interface IAuthenticationService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress = null, string? userAgent = null, CancellationToken cancellationToken = default);
    Task<LoginResponse> RefreshTokenAsync(RefreshTokenRequest request, string? ipAddress = null, CancellationToken cancellationToken = default);
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);
    Task RevokeAllUserSessionsAsync(Guid userId, CancellationToken cancellationToken = default);
}

public interface IUserService
{
    Task<UserDto?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserDto?> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<UserDto?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserDto>> GetUsersAsync(int skip = 0, int take = 50, CancellationToken cancellationToken = default);
    Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<UserDto> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default);
    Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);
    Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);
    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default);
    Task<string[]> GetUserPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
}

public class AuthenticationService : IAuthenticationService
{
    private readonly ILogger<AuthenticationService> _logger;
    private readonly IUserService _userService;
    private readonly AuthConfiguration _config;
    private readonly Dictionary<string, User> _users = new(); // In-memory store for demo
    private readonly Dictionary<string, UserSession> _sessions = new(); // In-memory store for demo

    public AuthenticationService(
        ILogger<AuthenticationService> logger,
        IUserService userService,
        AuthConfiguration config)
    {
        _logger = logger;
        _userService = userService;
        _config = config;
        
        // Initialize with default admin user
        InitializeDefaultUsers();
    }

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request, 
        string? ipAddress = null, 
        string? userAgent = null, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Login attempt for username: {Username}", request.Username);

        var user = _users.Values.FirstOrDefault(u => 
            u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase) ||
            u.Email.Equals(request.Username, StringComparison.OrdinalIgnoreCase));

        if (user == null || !VerifyPassword(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Invalid login attempt for username: {Username}", request.Username);
            throw new UnauthorizedAccessException("Invalid username or password");
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Login attempt for inactive user: {Username}", request.Username);
            throw new UnauthorizedAccessException("Account is disabled");
        }

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;

        // Create session
        var session = new UserSession
        {
            UserId = user.Id,
            RefreshToken = GenerateRefreshToken(),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(_config.RefreshTokenExpirationDays)
        };

        _sessions[session.RefreshToken] = session;

        // Generate access token
        var accessToken = GenerateAccessToken(user);
        var expiresAt = DateTime.UtcNow.AddMinutes(_config.AccessTokenExpirationMinutes);

        var userDto = await MapToUserDto(user);

        _logger.LogInformation("User {Username} logged in successfully", request.Username);

        return new LoginResponse(accessToken, session.RefreshToken, expiresAt, userDto);
    }

    public async Task<LoginResponse> RefreshTokenAsync(
        RefreshTokenRequest request, 
        string? ipAddress = null, 
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(request.RefreshToken, out var session) || !session.IsActive)
        {
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        var user = _users.Values.FirstOrDefault(u => u.Id == session.UserId);
        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedAccessException("User not found or inactive");
        }

        // Generate new tokens
        var newAccessToken = GenerateAccessToken(user);
        var newRefreshToken = GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(_config.AccessTokenExpirationMinutes);

        // Update session
        _sessions.Remove(request.RefreshToken);
        session.RefreshToken = newRefreshToken;
        session.CreatedAt = DateTime.UtcNow;
        session.ExpiresAt = DateTime.UtcNow.AddDays(_config.RefreshTokenExpirationDays);
        session.IpAddress = ipAddress;
        _sessions[newRefreshToken] = session;

        var userDto = await MapToUserDto(user);

        return new LoginResponse(newAccessToken, newRefreshToken, expiresAt, userDto);
    }

    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(refreshToken, out var session))
        {
            _sessions.Remove(refreshToken);
            _logger.LogInformation("User session revoked for user: {UserId}", session.UserId);
        }

        return Task.CompletedTask;
    }

    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        // Check if user already exists
        if (_users.Values.Any(u => u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Username already exists");
        }

        if (_users.Values.Any(u => u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Email already exists");
        }

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = HashPassword(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = !_config.RequireEmailConfirmation
        };

        _users[user.Id.ToString()] = user;

        // Assign default user role
        var userRole = new UserRole
        {
            UserId = user.Id,
            RoleId = GetDefaultUserRoleId(),
            AssignedAt = DateTime.UtcNow
        };
        user.UserRoles.Add(userRole);

        _logger.LogInformation("New user registered: {Username} ({Email})", user.Username, user.Email);

        return await MapToUserDto(user);
    }

    public Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_config.JwtSecretKey);

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
            }, out SecurityToken validatedToken);

            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task RevokeAllUserSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userSessions = _sessions.Values.Where(s => s.UserId == userId).ToList();
        foreach (var session in userSessions)
        {
            _sessions.Remove(session.RefreshToken);
        }

        _logger.LogInformation("Revoked all sessions for user: {UserId}", userId);
        return Task.CompletedTask;
    }

    private string GenerateAccessToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_config.JwtSecretKey);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new("sub", user.Id.ToString()),
            new("username", user.Username),
            new("email", user.Email)
        };

        // Add role claims
        foreach (var role in user.UserRoles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Role?.Name ?? "User"));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_config.AccessTokenExpirationMinutes),
            Issuer = _config.JwtIssuer,
            Audience = _config.JwtAudience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "salt")); // Simplified hashing
        return Convert.ToBase64String(hashedBytes);
    }

    private bool VerifyPassword(string password, string hash)
    {
        return HashPassword(password) == hash;
    }

    private async Task<UserDto> MapToUserDto(User user)
    {
        var roles = user.UserRoles.Select(ur => ur.Role?.Name ?? "User").ToArray();
        var permissions = await GetUserPermissionsFromRoles(roles);

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
            permissions);
    }

    private Task<string[]> GetUserPermissionsFromRoles(string[] roles)
    {
        var permissions = new HashSet<string>();

        foreach (var roleName in roles)
        {
            switch (roleName)
            {
                case Roles.Administrator:
                    permissions.Add(Permissions.SystemAdmin);
                    permissions.Add(Permissions.UsersManage);
                    permissions.Add(Permissions.RolesAssign);
                    permissions.Add(Permissions.FilesRead);
                    permissions.Add(Permissions.FilesWrite);
                    permissions.Add(Permissions.FilesDelete);
                    permissions.Add(Permissions.FilesUpload);
                    permissions.Add(Permissions.FilesDownload);
                    permissions.Add(Permissions.SystemBackup);
                    permissions.Add(Permissions.SystemMonitor);
                    permissions.Add(Permissions.SystemPipeline);
                    break;

                case Roles.FileManager:
                    permissions.Add(Permissions.FilesRead);
                    permissions.Add(Permissions.FilesWrite);
                    permissions.Add(Permissions.FilesDelete);
                    permissions.Add(Permissions.FilesUpload);
                    permissions.Add(Permissions.FilesDownload);
                    break;

                case Roles.SystemOperator:
                    permissions.Add(Permissions.SystemMonitor);
                    permissions.Add(Permissions.SystemBackup);
                    permissions.Add(Permissions.SystemPipeline);
                    permissions.Add(Permissions.FilesRead);
                    break;

                case Roles.User:
                    permissions.Add(Permissions.FilesRead);
                    permissions.Add(Permissions.FilesUpload);
                    permissions.Add(Permissions.FilesDownload);
                    break;

                case Roles.ReadOnlyUser:
                    permissions.Add(Permissions.FilesRead);
                    permissions.Add(Permissions.FilesDownload);
                    break;
            }
        }

        return Task.FromResult(permissions.ToArray());
    }

    private Guid GetDefaultUserRoleId()
    {
        // Return hardcoded User role ID for simplicity
        return Guid.Parse("11111111-1111-1111-1111-111111111111");
    }

    private void InitializeDefaultUsers()
    {
        // Create default admin user
        var adminUser = new User
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000000"),
            Username = "admin",
            Email = "admin@silo.local",
            PasswordHash = HashPassword("admin123"), // Default password
            FirstName = "System",
            LastName = "Administrator",
            IsActive = true,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        var adminRole = new UserRole
        {
            UserId = adminUser.Id,
            RoleId = Guid.Parse("00000000-0000-0000-0000-000000000001"), // Admin role ID
            AssignedAt = DateTime.UtcNow
        };

        adminUser.UserRoles.Add(adminRole);
        _users[adminUser.Id.ToString()] = adminUser;

        _logger.LogInformation("Initialized default admin user (username: admin, password: admin123)");
    }
}