using System.Security.Cryptography;
using System.Text;
using Silo.Core.Models;

namespace Silo.Api.Services;

public class UserService : IUserService
{
    private readonly Dictionary<string, User> _users = new();
    private readonly Dictionary<string, Role> _roles = new();
    private readonly Dictionary<string, Permission> _permissions = new();
    private readonly ILogger<UserService> _logger;

    public UserService(ILogger<UserService> logger)
    {
        _logger = logger;
        InitializeDefaultData();
    }

    public async Task<UserDto?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        return user != null ? await MapToUserDto(user) : null;
    }

    public async Task<UserDto?> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = _users.Values.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        return user != null ? await MapToUserDto(user) : null;
    }

    public async Task<UserDto?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = _users.Values.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        return user != null ? await MapToUserDto(user) : null;
    }

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var users = _users.Values
            .Skip(skip)
            .Take(take)
            .ToList();

        var userDtos = new List<UserDto>();
        foreach (var user in users)
        {
            userDtos.Add(await MapToUserDto(user));
        }

        return userDtos;
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        // Check if user already exists
        if (_users.Values.Any(u => u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"User with username '{request.Username}' already exists");
        }

        if (_users.Values.Any(u => u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"User with email '{request.Email}' already exists");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            Email = request.Email,
            PasswordHash = HashPassword(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            IsActive = true,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        // Assign roles if provided
        if (request.Roles != null && request.Roles.Length > 0)
        {
            foreach (var roleName in request.Roles)
            {
                var role = _roles.Values.FirstOrDefault(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
                if (role != null)
                {
                    user.UserRoles.Add(new UserRole
                    {
                        UserId = user.Id,
                        RoleId = role.Id,
                        AssignedAt = DateTime.UtcNow
                    });
                }
            }
        }

        _users[user.Id.ToString()] = user;
        _logger.LogInformation("Created user {Username} with ID {UserId}", user.Username, user.Id);

        return await MapToUserDto(user);
    }

    public async Task<UserDto> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            throw new InvalidOperationException($"User with ID {userId} not found");
        }

        if (request.FirstName != null)
            user.FirstName = request.FirstName;

        if (request.LastName != null)
            user.LastName = request.LastName;

        if (request.Email != null)
        {
            // Check if email is already taken by another user
            if (_users.Values.Any(u => u.Id != userId && u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Email '{request.Email}' is already taken");
            }
            user.Email = request.Email;
        }

        if (request.IsActive.HasValue)
            user.IsActive = request.IsActive.Value;

        _logger.LogInformation("Updated user {Username} with ID {UserId}", user.Username, user.Id);

        return await MapToUserDto(user);
    }

    public Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user != null)
        {
            _users.Remove(user.Id.ToString());
            _logger.LogInformation("Deleted user {Username} with ID {UserId}", user.Username, user.Id);
        }

        return Task.CompletedTask;
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return false;
        }

        // Verify current password
        if (!VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            return false;
        }

        // Update password
        user.PasswordHash = HashPassword(request.NewPassword);
        _logger.LogInformation("Changed password for user {Username} with ID {UserId}", user.Username, user.Id);

        return true;
    }

    public Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        var role = _roles.Values.FirstOrDefault(r => r.Id == roleId);

        if (user == null || role == null)
        {
            throw new InvalidOperationException("User or role not found");
        }

        // Check if user already has this role
        if (!user.UserRoles.Any(ur => ur.RoleId == roleId))
        {
            user.UserRoles.Add(new UserRole
            {
                UserId = userId,
                RoleId = roleId,
                AssignedAt = DateTime.UtcNow
            });

            _logger.LogInformation("Assigned role {RoleName} to user {Username}", role.Name, user.Username);
        }

        return Task.CompletedTask;
    }

    public Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            throw new InvalidOperationException("User not found");
        }

        var userRole = user.UserRoles.FirstOrDefault(ur => ur.RoleId == roleId);
        if (userRole != null)
        {
            user.UserRoles.Remove(userRole);
            _logger.LogInformation("Removed role from user {Username}", user.Username);
        }

        return Task.CompletedTask;
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
    {
        var permissions = await GetUserPermissionsAsync(userId, cancellationToken);
        return permissions.Contains(permission);
    }

    public async Task<string[]> GetUserPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return Array.Empty<string>();
        }

        var permissions = new HashSet<string>();

        foreach (var userRole in user.UserRoles)
        {
            var role = _roles.Values.FirstOrDefault(r => r.Id == userRole.RoleId);
            if (role != null)
            {
                foreach (var rolePermission in role.RolePermissions)
                {
                    var permission = _permissions.Values.FirstOrDefault(p => p.Id == rolePermission.PermissionId);
                    if (permission != null)
                    {
                        permissions.Add(permission.Name);
                    }
                }
            }
        }

        return permissions.ToArray();
    }

    // Internal method used by AuthenticationService
    internal User? GetUserByUsernameInternal(string username)
    {
        return _users.Values.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<UserDto> MapToUserDto(User user)
    {
        var roles = user.UserRoles
            .Select(ur => _roles.Values.FirstOrDefault(r => r.Id == ur.RoleId)?.Name)
            .Where(r => r != null)
            .Cast<string>()
            .ToArray();

        var permissions = await GetUserPermissionsAsync(user.Id);

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

    private void InitializeDefaultData()
    {
        // Create default permissions
        CreatePermission(Permissions.FilesRead);
        CreatePermission(Permissions.FilesWrite);
        CreatePermission(Permissions.FilesDelete);
        CreatePermission(Permissions.FilesUpload);
        CreatePermission(Permissions.FilesDownload);
        CreatePermission(Permissions.UsersRead);
        CreatePermission(Permissions.UsersWrite);
        CreatePermission(Permissions.UsersDelete);
        CreatePermission(Permissions.UsersManage);
        CreatePermission(Permissions.SystemAdmin);
        CreatePermission(Permissions.SystemBackup);
        CreatePermission(Permissions.SystemMonitor);
        CreatePermission(Permissions.SystemPipeline);
        CreatePermission(Permissions.RolesRead);
        CreatePermission(Permissions.RolesWrite);
        CreatePermission(Permissions.RolesDelete);
        CreatePermission(Permissions.RolesAssign);

        // Create default roles
        var adminRole = CreateRole(Roles.Administrator, "Full system access");
        var userRole = CreateRole(Roles.User, "Basic user access");
        var fileManagerRole = CreateRole(Roles.FileManager, "File management access");

        // Assign permissions to Admin role (all permissions)
        foreach (var permission in _permissions.Values)
        {
            adminRole.RolePermissions.Add(new RolePermission
            {
                RoleId = adminRole.Id,
                PermissionId = permission.Id,
                AssignedAt = DateTime.UtcNow
            });
        }

        // Assign basic permissions to User role
        var userPermissions = new[]
        {
            Permissions.FilesRead,
            Permissions.FilesUpload,
            Permissions.FilesDownload
        };

        foreach (var permissionName in userPermissions)
        {
            var permission = _permissions.Values.FirstOrDefault(p => p.Name == permissionName);
            if (permission != null)
            {
                userRole.RolePermissions.Add(new RolePermission
                {
                    RoleId = userRole.Id,
                    PermissionId = permission.Id,
                    AssignedAt = DateTime.UtcNow
                });
            }
        }

        // Create default admin user
        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            Username = "admin",
            Email = "admin@silo.local",
            PasswordHash = HashPassword("admin123"),
            FirstName = "System",
            LastName = "Administrator",
            IsActive = true,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        adminUser.UserRoles.Add(new UserRole
        {
            UserId = adminUser.Id,
            RoleId = adminRole.Id,
            AssignedAt = DateTime.UtcNow
        });

        _users[adminUser.Id.ToString()] = adminUser;

        _logger.LogInformation("Initialized default user data (admin user: admin/admin123)");
    }

    private Permission CreatePermission(string name)
    {
        var permission = new Permission
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = $"Permission for {name}",
            CreatedAt = DateTime.UtcNow
        };

        _permissions[permission.Id.ToString()] = permission;
        return permission;
    }

    private Role CreateRole(string name, string description)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        _roles[role.Id.ToString()] = role;
        return role;
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hashedBytes);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        var computedHash = HashPassword(password);
        return computedHash == hash;
    }
}