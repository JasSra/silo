using Hangfire;
using Silo.Core.Models;

namespace Silo.Api.Services;

public interface IBackupService
{
    Task<BackupJob> CreateBackupJobAsync(BackupJobRequest request, CancellationToken cancellationToken = default);
    Task<BackupJob> GetBackupJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupJob>> GetBackupJobsAsync(int skip = 0, int take = 50, CancellationToken cancellationToken = default);
    Task<BackupExecutionResult> ExecuteBackupAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<bool> CancelBackupAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task ScheduleRecurringBackupsAsync(CancellationToken cancellationToken = default);
    Task CleanupExpiredBackupsAsync(CancellationToken cancellationToken = default);
}

public record BackupJobRequest(
    string Name,
    string Description,
    BackupType Type,
    string SourcePath,
    string DestinationPath,
    BackupSchedule? Schedule = null,
    BackupRetentionPolicy? RetentionPolicy = null,
    Dictionary<string, string>? Metadata = null);

public record BackupJob(
    Guid Id,
    string Name,
    string Description,
    BackupType Type,
    string SourcePath,
    string DestinationPath,
    BackupStatus Status,
    BackupSchedule? Schedule,
    BackupRetentionPolicy? RetentionPolicy,
    DateTime CreatedAt,
    DateTime? LastExecutedAt,
    DateTime? NextExecutionAt,
    Dictionary<string, string>? Metadata = null);

public record BackupExecutionResult(
    Guid JobId,
    bool Success,
    DateTime StartTime,
    DateTime EndTime,
    long TotalBytes,
    int TotalFiles,
    int SuccessfulFiles,
    int FailedFiles,
    string? ErrorMessage = null,
    Dictionary<string, object>? ExecutionMetadata = null);

public enum BackupType
{
    Full,
    Incremental,
    Differential
}

public enum BackupStatus
{
    Created,
    Scheduled,
    Running,
    Completed,
    Failed,
    Cancelled
}

public record BackupSchedule(
    string CronExpression,
    bool IsEnabled = true);

public record BackupRetentionPolicy(
    int MaxBackupCount = 10,
    TimeSpan MaxAge = default,
    long MaxTotalSizeBytes = 0);

public class BackupService : IBackupService
{
    private readonly ILogger<BackupService> _logger;
    private readonly IMinioStorageService _storageService;
    private readonly IOpenSearchIndexingService _indexingService;
    private readonly BackupConfiguration _config;
    private readonly Dictionary<Guid, BackupJob> _backupJobs = new();
    private readonly SemaphoreSlim _backupSemaphore = new(3, 3); // Max 3 concurrent backups

    public BackupService(
        ILogger<BackupService> logger,
        IMinioStorageService storageService,
        IOpenSearchIndexingService indexingService,
        BackupConfiguration config)
    {
        _logger = logger;
        _storageService = storageService;
        _indexingService = indexingService;
        _config = config;
    }

    public Task<BackupJob> CreateBackupJobAsync(BackupJobRequest request, CancellationToken cancellationToken = default)
    {
        var job = new BackupJob(
            Guid.NewGuid(),
            request.Name,
            request.Description,
            request.Type,
            request.SourcePath,
            request.DestinationPath,
            BackupStatus.Created,
            request.Schedule,
            request.RetentionPolicy ?? _config.DefaultRetentionPolicy,
            DateTime.UtcNow,
            null,
            null,
            request.Metadata);

        _backupJobs[job.Id] = job;

        // Schedule the job if it has a schedule
        if (request.Schedule?.IsEnabled == true)
        {
            ScheduleBackupJob(job);
        }

        _logger.LogInformation("Created backup job {JobId}: {JobName}", job.Id, job.Name);
        return Task.FromResult(job);
    }

    public Task<BackupJob> GetBackupJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        if (!_backupJobs.TryGetValue(jobId, out var job))
        {
            throw new ArgumentException($"Backup job {jobId} not found");
        }

        return Task.FromResult(job);
    }

    public Task<IReadOnlyList<BackupJob>> GetBackupJobsAsync(int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var jobs = _backupJobs.Values
            .OrderByDescending(j => j.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToList();

        return Task.FromResult<IReadOnlyList<BackupJob>>(jobs);
    }

    public async Task<BackupExecutionResult> ExecuteBackupAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        if (!_backupJobs.TryGetValue(jobId, out var job))
        {
            throw new ArgumentException($"Backup job {jobId} not found");
        }

        await _backupSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            _logger.LogInformation("Starting backup execution for job {JobId}: {JobName}", jobId, job.Name);
            
            // Update job status
            var updatedJob = job with { Status = BackupStatus.Running };
            _backupJobs[jobId] = updatedJob;

            var startTime = DateTime.UtcNow;
            var result = await ExecuteBackupInternalAsync(updatedJob, cancellationToken);
            
            // Update job with execution results
            var finalStatus = result.Success ? BackupStatus.Completed : BackupStatus.Failed;
            var finalJob = updatedJob with 
            { 
                Status = finalStatus, 
                LastExecutedAt = startTime,
                NextExecutionAt = CalculateNextExecution(updatedJob.Schedule)
            };
            _backupJobs[jobId] = finalJob;

            _logger.LogInformation("Backup execution {Status} for job {JobId}: {JobName}", 
                result.Success ? "completed" : "failed", jobId, job.Name);

            return result;
        }
        finally
        {
            _backupSemaphore.Release();
        }
    }

    private async Task<BackupExecutionResult> ExecuteBackupInternalAsync(BackupJob job, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var totalBytes = 0L;
        var totalFiles = 0;
        var successfulFiles = 0;
        var failedFiles = 0;
        var executionMetadata = new Dictionary<string, object>();

        try
        {
            _logger.LogInformation("Executing {BackupType} backup from {SourcePath} to {DestinationPath}", 
                job.Type, job.SourcePath, job.DestinationPath);

            switch (job.Type)
            {
                case BackupType.Full:
                    var fullResult = await ExecuteFullBackupAsync(job, cancellationToken);
                    totalBytes = fullResult.TotalBytes;
                    totalFiles = fullResult.TotalFiles;
                    successfulFiles = fullResult.SuccessfulFiles;
                    failedFiles = fullResult.FailedFiles;
                    executionMetadata = fullResult.Metadata;
                    break;

                case BackupType.Incremental:
                    var incrementalResult = await ExecuteIncrementalBackupAsync(job, cancellationToken);
                    totalBytes = incrementalResult.TotalBytes;
                    totalFiles = incrementalResult.TotalFiles;
                    successfulFiles = incrementalResult.SuccessfulFiles;
                    failedFiles = incrementalResult.FailedFiles;
                    executionMetadata = incrementalResult.Metadata;
                    break;

                case BackupType.Differential:
                    var differentialResult = await ExecuteDifferentialBackupAsync(job, cancellationToken);
                    totalBytes = differentialResult.TotalBytes;
                    totalFiles = differentialResult.TotalFiles;
                    successfulFiles = differentialResult.SuccessfulFiles;
                    failedFiles = differentialResult.FailedFiles;
                    executionMetadata = differentialResult.Metadata;
                    break;
            }

            var endTime = DateTime.UtcNow;
            
            // Schedule cleanup if needed
            BackgroundJob.Schedule(() => CleanupBackupAsync(job.Id), TimeSpan.FromMinutes(5));

            return new BackupExecutionResult(
                job.Id,
                true,
                startTime,
                endTime,
                totalBytes,
                totalFiles,
                successfulFiles,
                failedFiles,
                null,
                executionMetadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup execution failed for job {JobId}", job.Id);
            
            return new BackupExecutionResult(
                job.Id,
                false,
                startTime,
                DateTime.UtcNow,
                totalBytes,
                totalFiles,
                successfulFiles,
                failedFiles,
                ex.Message,
                executionMetadata);
        }
    }

    private async Task<BackupExecutionData> ExecuteFullBackupAsync(BackupJob job, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object>
        {
            ["BackupType"] = "Full",
            ["Timestamp"] = DateTime.UtcNow
        };

        if (job.SourcePath.StartsWith("minio://"))
        {
            return await BackupFromMinioAsync(job, metadata, cancellationToken);
        }
        else
        {
            return await BackupFromFileSystemAsync(job, metadata, cancellationToken);
        }
    }

    private async Task<BackupExecutionData> ExecuteIncrementalBackupAsync(BackupJob job, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object>
        {
            ["BackupType"] = "Incremental",
            ["Timestamp"] = DateTime.UtcNow
        };

        // Find the last backup timestamp
        var lastBackupTime = job.LastExecutedAt ?? DateTime.MinValue;
        metadata["SinceTimestamp"] = lastBackupTime;

        if (job.SourcePath.StartsWith("minio://"))
        {
            return await BackupFromMinioIncrementalAsync(job, lastBackupTime, metadata, cancellationToken);
        }
        else
        {
            return await BackupFromFileSystemIncrementalAsync(job, lastBackupTime, metadata, cancellationToken);
        }
    }

    private async Task<BackupExecutionData> ExecuteDifferentialBackupAsync(BackupJob job, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object>
        {
            ["BackupType"] = "Differential",
            ["Timestamp"] = DateTime.UtcNow
        };

        // Find the last full backup timestamp (simplified - would need more sophisticated tracking)
        var lastFullBackupTime = job.CreatedAt;
        metadata["SinceFullBackup"] = lastFullBackupTime;

        if (job.SourcePath.StartsWith("minio://"))
        {
            return await BackupFromMinioIncrementalAsync(job, lastFullBackupTime, metadata, cancellationToken);
        }
        else
        {
            return await BackupFromFileSystemIncrementalAsync(job, lastFullBackupTime, metadata, cancellationToken);
        }
    }

    private async Task<BackupExecutionData> BackupFromMinioAsync(
        BackupJob job, 
        Dictionary<string, object> metadata, 
        CancellationToken cancellationToken)
    {
        var sourceBucket = ExtractBucketFromPath(job.SourcePath);
        var sourcePrefix = ExtractPrefixFromPath(job.SourcePath);
        var destinationPath = GenerateBackupPath(job);

        var totalBytes = 0L;
        var totalFiles = 0;
        var successfulFiles = 0;
        var failedFiles = 0;

        // List all objects in source
        var objects = await _storageService.ListFilesAsync(sourceBucket, sourcePrefix, cancellationToken);
        totalFiles = objects.Count();

        foreach (var fileName in objects)
        {
            try
            {
                using var sourceStream = await _storageService.DownloadFileAsync(sourceBucket, fileName, cancellationToken);
                var backupKey = $"{destinationPath}/{fileName}";
                
                await _storageService.UploadFileAsync(
                    "backups",
                    backupKey, 
                    sourceStream,
                    "application/octet-stream", 
                    cancellationToken);

                // We can't get size easily, so just increment
                successfulFiles++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backup object {ObjectKey}", fileName);
                failedFiles++;
            }
        }

        metadata["SourceBucket"] = sourceBucket;
        metadata["SourcePrefix"] = sourcePrefix;
        metadata["DestinationPath"] = destinationPath;

        return new BackupExecutionData(totalBytes, totalFiles, successfulFiles, failedFiles, metadata);
    }

    private async Task<BackupExecutionData> BackupFromFileSystemAsync(
        BackupJob job, 
        Dictionary<string, object> metadata, 
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(job.SourcePath))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {job.SourcePath}");
        }

        var destinationPath = GenerateBackupPath(job);
        var files = Directory.GetFiles(job.SourcePath, "*.*", SearchOption.AllDirectories);
        
        var totalBytes = 0L;
        var totalFiles = files.Length;
        var successfulFiles = 0;
        var failedFiles = 0;

        foreach (var filePath in files)
        {
            try
            {
                var relativePath = Path.GetRelativePath(job.SourcePath, filePath);
                var backupKey = $"{destinationPath}/{relativePath}".Replace('\\', '/');
                
                using var fileStream = File.OpenRead(filePath);
                await _storageService.UploadFileAsync(
                    "backups",
                    backupKey, 
                    fileStream,
                    "application/octet-stream", 
                    cancellationToken);

                var fileInfo = new FileInfo(filePath);
                totalBytes += fileInfo.Length;
                successfulFiles++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backup file {FilePath}", filePath);
                failedFiles++;
            }
        }

        metadata["SourceDirectory"] = job.SourcePath;
        metadata["DestinationPath"] = destinationPath;

        return new BackupExecutionData(totalBytes, totalFiles, successfulFiles, failedFiles, metadata);
    }

    private async Task<BackupExecutionData> BackupFromMinioIncrementalAsync(
        BackupJob job, 
        DateTime sinceTime, 
        Dictionary<string, object> metadata, 
        CancellationToken cancellationToken)
    {
        // This would require object metadata tracking for last modified times
        // For now, fall back to full backup
        _logger.LogWarning("Incremental backup from MinIO not fully implemented, performing full backup");
        return await BackupFromMinioAsync(job, metadata, cancellationToken);
    }

    private async Task<BackupExecutionData> BackupFromFileSystemIncrementalAsync(
        BackupJob job, 
        DateTime sinceTime, 
        Dictionary<string, object> metadata, 
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(job.SourcePath))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {job.SourcePath}");
        }

        var destinationPath = GenerateBackupPath(job);
        var files = Directory.GetFiles(job.SourcePath, "*.*", SearchOption.AllDirectories)
            .Where(f => File.GetLastWriteTimeUtc(f) > sinceTime)
            .ToArray();
        
        var totalBytes = 0L;
        var totalFiles = files.Length;
        var successfulFiles = 0;
        var failedFiles = 0;

        foreach (var filePath in files)
        {
            try
            {
                var relativePath = Path.GetRelativePath(job.SourcePath, filePath);
                var backupKey = $"{destinationPath}/{relativePath}".Replace('\\', '/');
                
                using var fileStream = File.OpenRead(filePath);
                await _storageService.UploadFileAsync(
                    "backups",
                    backupKey, 
                    fileStream,
                    "application/octet-stream", 
                    cancellationToken);

                var fileInfo = new FileInfo(filePath);
                totalBytes += fileInfo.Length;
                successfulFiles++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backup file {FilePath}", filePath);
                failedFiles++;
            }
        }

        metadata["SourceDirectory"] = job.SourcePath;
        metadata["DestinationPath"] = destinationPath;
        metadata["FilteredSince"] = sinceTime;

        return new BackupExecutionData(totalBytes, totalFiles, successfulFiles, failedFiles, metadata);
    }

    public Task<bool> CancelBackupAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        if (_backupJobs.TryGetValue(jobId, out var job) && job.Status == BackupStatus.Running)
        {
            var cancelledJob = job with { Status = BackupStatus.Cancelled };
            _backupJobs[jobId] = cancelledJob;
            
            _logger.LogInformation("Cancelled backup job {JobId}: {JobName}", jobId, job.Name);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task ScheduleRecurringBackupsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var job in _backupJobs.Values.Where(j => j.Schedule?.IsEnabled == true))
        {
            ScheduleBackupJob(job);
        }

        return Task.CompletedTask;
    }

    [Queue("backup")]
    public async Task CleanupExpiredBackupsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Running backup cleanup");

        foreach (var job in _backupJobs.Values)
        {
            if (job.RetentionPolicy != null)
            {
                await CleanupBackupAsync(job.Id);
            }
        }
    }

    private void ScheduleBackupJob(BackupJob job)
    {
        if (job.Schedule?.IsEnabled == true)
        {
            RecurringJob.AddOrUpdate(
                $"backup-{job.Id}",
                () => ExecuteBackupAsync(job.Id, CancellationToken.None),
                job.Schedule.CronExpression);

            _logger.LogInformation("Scheduled backup job {JobId} with cron: {CronExpression}", 
                job.Id, job.Schedule.CronExpression);
        }
    }

    [Queue("backup")]
    public async Task CleanupBackupAsync(Guid jobId)
    {
        try
        {
            if (!_backupJobs.TryGetValue(jobId, out var job) || job.RetentionPolicy == null)
            {
                return;
            }

            _logger.LogInformation("Cleaning up backups for job {JobId}", jobId);

            var backupPrefix = $"backups/{job.Name}/";
            var backups = await _storageService.ListFilesAsync("backups", backupPrefix);
            
            // Group by backup date and apply retention policies
            var backupGroups = backups
                .GroupBy(b => ExtractBackupDateFromKey(b))
                .OrderByDescending(g => g.Key)
                .ToList();

            // Keep only the allowed number of backups
            var backupsToDelete = backupGroups
                .Skip(job.RetentionPolicy.MaxBackupCount)
                .SelectMany(g => g)
                .ToList();

            // Delete old backups by age
            if (job.RetentionPolicy.MaxAge != default)
            {
                var cutoffDate = DateTime.UtcNow - job.RetentionPolicy.MaxAge;
                var oldBackups = backupGroups
                    .Where(g => g.Key < cutoffDate)
                    .SelectMany(g => g)
                    .ToList();
                
                backupsToDelete.AddRange(oldBackups);
            }

            // Delete expired backups
            foreach (var backup in backupsToDelete.Distinct())
            {
                await _storageService.DeleteFileAsync("backups", backup);
                _logger.LogDebug("Deleted expired backup: {BackupKey}", backup);
            }

            if (backupsToDelete.Any())
            {
                _logger.LogInformation("Cleaned up {Count} expired backups for job {JobId}", 
                    backupsToDelete.Count, jobId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during backup cleanup for job {JobId}", jobId);
        }
    }

    private string GenerateBackupPath(BackupJob job)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy/MM/dd/HHmmss");
        return $"backups/{job.Name}/{timestamp}";
    }

    private string ExtractBucketFromPath(string path)
    {
        // Extract bucket from minio://bucket/path format
        var uri = new Uri(path);
        return uri.Host;
    }

    private string ExtractPrefixFromPath(string path)
    {
        // Extract prefix from minio://bucket/path format
        var uri = new Uri(path);
        return uri.AbsolutePath.TrimStart('/');
    }

    private DateTime ExtractBackupDateFromKey(string key)
    {
        // Extract date from backup path: backups/jobname/yyyy/MM/dd/HHmmss/...
        try
        {
            var parts = key.Split('/');
            if (parts.Length >= 6)
            {
                var year = int.Parse(parts[2]);
                var month = int.Parse(parts[3]);
                var day = int.Parse(parts[4]);
                var timeStr = parts[5];
                
                if (timeStr.Length >= 6)
                {
                    var hour = int.Parse(timeStr.Substring(0, 2));
                    var minute = int.Parse(timeStr.Substring(2, 2));
                    var second = int.Parse(timeStr.Substring(4, 2));
                    
                    return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
                }
            }
        }
        catch
        {
            // Fall back to file creation time or current time
        }
        
        return DateTime.MinValue;
    }

    private DateTime? CalculateNextExecution(BackupSchedule? schedule)
    {
        if (schedule?.IsEnabled != true) return null;
        
        // This would need a proper cron parser in a real implementation
        // For now, return a simple daily schedule
        return DateTime.UtcNow.AddDays(1);
    }
}

public record BackupExecutionData(
    long TotalBytes,
    int TotalFiles,
    int SuccessfulFiles,
    int FailedFiles,
    Dictionary<string, object> Metadata);

public class BackupConfiguration
{
    public string DefaultBackupBucket { get; set; } = "backups";
    public BackupRetentionPolicy DefaultRetentionPolicy { get; set; } = new(
        MaxBackupCount: 7,
        MaxAge: TimeSpan.FromDays(30),
        MaxTotalSizeBytes: 100L * 1024 * 1024 * 1024); // 100GB
    public int MaxConcurrentBackups { get; set; } = 3;
    public TimeSpan BackupTimeout { get; set; } = TimeSpan.FromHours(4);
}