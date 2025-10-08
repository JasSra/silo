using Microsoft.EntityFrameworkCore;
using Silo.Core.Models;

namespace Silo.Core.Data;

public class SiloDbContext : DbContext
{
    public SiloDbContext(DbContextOptions<SiloDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantApiKey> TenantApiKeys => Set<TenantApiKey>();
    
    // Tenant-scoped data models
    public DbSet<FileMetadata> FileMetadata => Set<FileMetadata>();
    public DbSet<BackupJob> BackupJobs => Set<BackupJob>();
    public DbSet<FileVersion> FileVersions => Set<FileVersion>();
    public DbSet<FileDiff> FileDiffs => Set<FileDiff>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.TenantId);
            
            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Users)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Role configuration
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Permission configuration
        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Resource, e.Action }).IsUnique();
        });

        // UserRole configuration (many-to-many)
        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.RoleId });
            
            entity.HasOne(e => e.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(e => e.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // RolePermission configuration (many-to-many)
        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(e => new { e.RoleId, e.PermissionId });
            
            entity.HasOne(e => e.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(e => e.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(e => e.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserSession configuration
        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RefreshToken).IsUnique();
            entity.HasIndex(e => e.UserId);
            
            entity.HasOne(e => e.User)
                .WithMany(u => u.UserSessions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Tenant configuration
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasIndex(e => e.Name);
        });

        // TenantApiKey configuration
        modelBuilder.Entity<TenantApiKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.KeyHash).IsUnique();
            entity.HasIndex(e => e.TenantId);
            
            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.ApiKeys)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // FileMetadata configuration (tenant-scoped)
        modelBuilder.Entity<FileMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.Checksum });
            entity.HasIndex(e => new { e.TenantId, e.FileName });
            entity.Ignore(e => e.Metadata); // Ignore dictionary
            entity.Ignore(e => e.Tags); // Ignore list
            entity.Ignore(e => e.Categories); // Ignore list
            entity.Ignore(e => e.ScanResult); // Ignore complex type for now
        });

        // BackupJob configuration (tenant-scoped)
        modelBuilder.Entity<BackupJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.Status });
            entity.Ignore(e => e.Configuration); // Ignore dictionary for now
        });

        // FileVersion configuration (tenant-scoped)
        modelBuilder.Entity<FileVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.FilePath });
            entity.HasIndex(e => new { e.TenantId, e.FilePath, e.VersionNumber });
            entity.Ignore(e => e.Metadata); // Ignore dictionary
        });

        // FileDiff configuration
        modelBuilder.Entity<FileDiff>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.SourceVersionId, e.TargetVersionId });
            entity.Ignore(e => e.DiffMetadata); // Ignore dictionary
            
            entity.HasOne(e => e.SourceVersion)
                .WithMany(v => v.SourceDiffs)
                .HasForeignKey(e => e.SourceVersionId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(e => e.TargetVersion)
                .WithMany(v => v.TargetDiffs)
                .HasForeignKey(e => e.TargetVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Seed default data
        SeedDefaultData(modelBuilder);
    }

    private void SeedDefaultData(ModelBuilder modelBuilder)
    {
        // Default tenant for system/admin
        var systemTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        modelBuilder.Entity<Tenant>().HasData(new Tenant
        {
            Id = systemTenantId,
            Name = "System",
            Slug = "system",
            Description = "System tenant for administrative purposes",
            IsActive = true,
            SubscriptionTier = "Enterprise",
            StorageQuotaBytes = long.MaxValue,
            MaxUsers = int.MaxValue,
            MaxApiKeys = int.MaxValue,
            CreatedAt = DateTime.UtcNow
        });

        // Default roles
        var adminRoleId = Guid.Parse("00000000-0000-0000-0000-000000000011");
        var userRoleId = Guid.Parse("00000000-0000-0000-0000-000000000012");
        var fileManagerRoleId = Guid.Parse("00000000-0000-0000-0000-000000000013");

        modelBuilder.Entity<Role>().HasData(
            new Role
            {
                Id = adminRoleId,
                Name = "Administrator",
                Description = "Full system access",
                CreatedAt = DateTime.UtcNow
            },
            new Role
            {
                Id = userRoleId,
                Name = "User",
                Description = "Standard user access",
                CreatedAt = DateTime.UtcNow
            },
            new Role
            {
                Id = fileManagerRoleId,
                Name = "FileManager",
                Description = "File management access",
                CreatedAt = DateTime.UtcNow
            }
        );

        // Default permissions
        var permissions = new[]
        {
            new Permission { Id = Guid.NewGuid(), Name = "files:read", Resource = "files", Action = "read", Description = "Read files", CreatedAt = DateTime.UtcNow },
            new Permission { Id = Guid.NewGuid(), Name = "files:write", Resource = "files", Action = "write", Description = "Write files", CreatedAt = DateTime.UtcNow },
            new Permission { Id = Guid.NewGuid(), Name = "files:delete", Resource = "files", Action = "delete", Description = "Delete files", CreatedAt = DateTime.UtcNow },
            new Permission { Id = Guid.NewGuid(), Name = "files:upload", Resource = "files", Action = "upload", Description = "Upload files", CreatedAt = DateTime.UtcNow },
            new Permission { Id = Guid.NewGuid(), Name = "files:download", Resource = "files", Action = "download", Description = "Download files", CreatedAt = DateTime.UtcNow },
            new Permission { Id = Guid.NewGuid(), Name = "users:read", Resource = "users", Action = "read", Description = "Read users", CreatedAt = DateTime.UtcNow },
            new Permission { Id = Guid.NewGuid(), Name = "users:write", Resource = "users", Action = "write", Description = "Write users", CreatedAt = DateTime.UtcNow },
            new Permission { Id = Guid.NewGuid(), Name = "users:manage", Resource = "users", Action = "manage", Description = "Manage users", CreatedAt = DateTime.UtcNow },
            new Permission { Id = Guid.NewGuid(), Name = "system:admin", Resource = "system", Action = "admin", Description = "System administration", CreatedAt = DateTime.UtcNow },
        };

        modelBuilder.Entity<Permission>().HasData(permissions);
    }
}
