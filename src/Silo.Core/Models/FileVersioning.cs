using System.ComponentModel.DataAnnotations;

namespace Silo.Core.Models;

public class FileVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    public string FilePath { get; set; } = string.Empty;
    
    public int VersionNumber { get; set; }
    
    [Required]
    public string StoragePath { get; set; } = string.Empty;
    
    [Required]
    public string Checksum { get; set; } = string.Empty;
    
    public long FileSize { get; set; }
    
    [StringLength(200)]
    public string MimeType { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public Guid? CreatedBy { get; set; }
    
    [StringLength(500)]
    public string? ChangeDescription { get; set; }
    
    public bool IsCurrentVersion { get; set; }
    
    public VersionType VersionType { get; set; } = VersionType.Normal;
    
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    // Navigation properties for diff tracking
    public virtual ICollection<FileDiff> SourceDiffs { get; set; } = new List<FileDiff>();
    public virtual ICollection<FileDiff> TargetDiffs { get; set; } = new List<FileDiff>();
}

public class FileDiff
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid SourceVersionId { get; set; }
    public Guid TargetVersionId { get; set; }
    
    [Required]
    public string DiffType { get; set; } = string.Empty; // "text", "binary", "metadata"
    
    public string? DiffContent { get; set; } // For text diffs, base64 for binary diffs
    
    public long DiffSize { get; set; }
    
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    
    public Dictionary<string, object> DiffMetadata { get; set; } = new();
    
    public virtual FileVersion SourceVersion { get; set; } = null!;
    public virtual FileVersion TargetVersion { get; set; } = null!;
}

public enum VersionType
{
    Normal,
    Major,
    Minor,
    Patch,
    Snapshot,
    Backup
}

// DTOs
public record FileVersionDto(
    Guid Id,
    string FilePath,
    int VersionNumber,
    string Checksum,
    long FileSize,
    string MimeType,
    DateTime CreatedAt,
    Guid? CreatedBy,
    string? ChangeDescription,
    bool IsCurrentVersion,
    VersionType VersionType,
    Dictionary<string, string> Metadata);

public record CreateVersionRequest(
    [Required] string FilePath,
    [Required] Stream FileStream,
    string? ChangeDescription = null,
    VersionType VersionType = VersionType.Normal,
    Dictionary<string, string>? Metadata = null);

public record FileVersionHistoryDto(
    string FilePath,
    FileVersionDto CurrentVersion,
    IReadOnlyList<FileVersionDto> Versions,
    int TotalVersions);

public record RestoreVersionRequest(
    [Required] Guid VersionId,
    bool MakeCurrentVersion = true);

public record CompareVersionsRequest(
    [Required] Guid SourceVersionId,
    [Required] Guid TargetVersionId,
    string DiffType = "auto"); // "auto", "text", "binary", "metadata"

public record VersionComparisonDto(
    FileVersionDto SourceVersion,
    FileVersionDto TargetVersion,
    string DiffType,
    string? DiffContent,
    long DiffSize,
    Dictionary<string, object> DiffMetadata);

public record VersionRetentionPolicy(
    int MaxVersionsPerFile = 10,
    TimeSpan MaxVersionAge = default,
    long MaxTotalVersionSizeBytes = 0,
    bool KeepMajorVersions = true,
    bool AutoCleanup = true);

public class VersioningConfiguration
{
    public bool EnableVersioning { get; set; } = true;
    public bool AutoVersionOnChange { get; set; } = true;
    public VersionRetentionPolicy RetentionPolicy { get; set; } = new();
    public string VersionStorageBucket { get; set; } = "versions";
    public bool GenerateDiffs { get; set; } = true;
    public int MaxDiffSizeBytes { get; set; } = 10 * 1024 * 1024; // 10MB
    public bool CompressVersions { get; set; } = true;
    public string[] ExcludedFileTypes { get; set; } = { ".tmp", ".temp", ".log" };
}