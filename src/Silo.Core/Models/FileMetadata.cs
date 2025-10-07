using System.ComponentModel.DataAnnotations;

namespace Silo.Core.Models;

public class FileMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    public string FileName { get; set; } = string.Empty;
    
    [Required]
    public string OriginalPath { get; set; } = string.Empty;
    
    [Required]
    public string StoragePath { get; set; } = string.Empty;
    
    public long FileSize { get; set; }
    
    [Required]
    public string MimeType { get; set; } = string.Empty;
    
    [Required]
    public string Checksum { get; set; } = string.Empty;
    
    public FileStatus Status { get; set; } = FileStatus.Pending;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? LastModified { get; set; }
    
    public DateTime? ProcessedAt { get; set; }
    
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    public List<string> Tags { get; set; } = new();
    
    public ScanResult? ScanResult { get; set; }
    
    public string? ThumbnailPath { get; set; }
    
    public string? ExtractedText { get; set; }
    
    public List<string> Categories { get; set; } = new();
    
    public string? Description { get; set; }
    
    public int Version { get; set; } = 1;
    
    public string? CreatedBy { get; set; }
    
    public bool IsDeleted { get; set; } = false;
    
    public DateTime? DeletedAt { get; set; }
}

public enum FileStatus
{
    Pending,
    Scanning,
    Processing,
    Processed,
    Indexed,
    Error,
    Quarantined,
    Archived
}

public class ScanResult
{
    public bool IsClean { get; set; }
    public string? ThreatName { get; set; }
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
    public string Scanner { get; set; } = "ClamAV";
    public string? ScannerVersion { get; set; }
}