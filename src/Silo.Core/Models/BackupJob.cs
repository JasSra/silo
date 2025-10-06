namespace Silo.Core.Models;

public class BackupJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public BackupType Type { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public string? Schedule { get; set; } // Cron expression
    public DateTime? LastRun { get; set; }
    public DateTime? NextRun { get; set; }
    public BackupStatus Status { get; set; } = BackupStatus.Scheduled;
    public long TotalBytes { get; set; }
    public long ProcessedBytes { get; set; }
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Configuration { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
}

public enum BackupType
{
    Full,
    Incremental,
    Differential,
    Synchronization
}

public enum BackupStatus
{
    Scheduled,
    Running,
    Completed,
    Failed,
    Cancelled,
    Paused
}

public class BackupResult
{
    public Guid JobId { get; set; }
    public bool Success { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public long BytesTransferred { get; set; }
    public int FilesTransferred { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string? BackupPath { get; set; }
    public string Checksum { get; set; } = string.Empty;
}