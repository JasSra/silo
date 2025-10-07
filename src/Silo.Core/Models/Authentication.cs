using System.ComponentModel.DataAnnotations;

namespace Silo.Core.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [StringLength(100)]
    public string Username { get; set; } = string.Empty;
    
    [Required]
    [EmailAddress]
    [StringLength(200)]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    public string PasswordHash { get; set; } = string.Empty;
    
    [StringLength(200)]
    public string? FirstName { get; set; }
    
    [StringLength(200)]
    public string? LastName { get; set; }
    
    public bool IsActive { get; set; } = true;
    public bool EmailConfirmed { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    
    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public virtual ICollection<UserSession> UserSessions { get; set; } = new List<UserSession>();
}

public class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(500)]
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string Resource { get; set; } = string.Empty;
    
    [Required]
    [StringLength(50)]
    public string Action { get; set; } = string.Empty;
    
    [StringLength(500)]
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public Guid? AssignedBy { get; set; }
    
    public virtual User User { get; set; } = null!;
    public virtual Role Role { get; set; } = null!;
}

public class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public Guid? GrantedBy { get; set; }
    
    public virtual Role Role { get; set; } = null!;
    public virtual Permission Permission { get; set; } = null!;
}

public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
    
    [StringLength(45)]
    public string? IpAddress { get; set; }
    
    [StringLength(500)]
    public string? UserAgent { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsActive => RevokedAt == null && ExpiresAt > DateTime.UtcNow;
    
    public virtual User User { get; set; } = null!;
}

// DTOs for API
public record LoginRequest(
    [Required] string Username,
    [Required] string Password,
    bool RememberMe = false);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User);

public record RefreshTokenRequest(
    [Required] string RefreshToken);

public record RegisterRequest(
    [Required] string Username,
    [Required] [EmailAddress] string Email,
    [Required] [MinLength(8)] string Password,
    string? FirstName = null,
    string? LastName = null);

public record UserDto(
    Guid Id,
    string Username,
    string Email,
    string? FirstName,
    string? LastName,
    bool IsActive,
    bool EmailConfirmed,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    string[] Roles,
    string[] Permissions);

public record CreateUserRequest(
    [Required] string Username,
    [Required] [EmailAddress] string Email,
    [Required] string Password,
    string? FirstName = null,
    string? LastName = null,
    string[] Roles = null!);

public record UpdateUserRequest(
    string? FirstName = null,
    string? LastName = null,
    string? Email = null,
    bool? IsActive = null);

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required] [MinLength(8)] string NewPassword);

public record AssignRoleRequest(
    [Required] Guid UserId,
    [Required] Guid RoleId);

public record CreateRoleRequest(
    [Required] string Name,
    string? Description = null,
    Guid[] PermissionIds = null!);

public record UpdateRoleRequest(
    string? Name = null,
    string? Description = null);

// Authorization constants
public static class Permissions
{
    // File permissions
    public const string FilesRead = "files:read";
    public const string FilesWrite = "files:write";
    public const string FilesDelete = "files:delete";
    public const string FilesUpload = "files:upload";
    public const string FilesDownload = "files:download";
    
    // User management permissions
    public const string UsersRead = "users:read";
    public const string UsersWrite = "users:write";
    public const string UsersDelete = "users:delete";
    public const string UsersManage = "users:manage";
    
    // System administration permissions
    public const string SystemAdmin = "system:admin";
    public const string SystemBackup = "system:backup";
    public const string SystemMonitor = "system:monitor";
    public const string SystemPipeline = "system:pipeline";
    
    // Role management permissions
    public const string RolesRead = "roles:read";
    public const string RolesWrite = "roles:write";
    public const string RolesDelete = "roles:delete";
    public const string RolesAssign = "roles:assign";
}

public static class Roles
{
    public const string Administrator = "Administrator";
    public const string User = "User";
    public const string FileManager = "FileManager";
    public const string SystemOperator = "SystemOperator";
    public const string ReadOnlyUser = "ReadOnlyUser";
}

public class AuthConfiguration
{
    public string JwtSecretKey { get; set; } = string.Empty;
    public string JwtIssuer { get; set; } = "Silo.Api";
    public string JwtAudience { get; set; } = "Silo.Client";
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 30;
    public bool RequireEmailConfirmation { get; set; } = false;
    public int MaxFailedLoginAttempts { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 30;
    public bool EnableTwoFactorAuth { get; set; } = false;
}