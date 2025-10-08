namespace Silo.Core.Services;

public interface IStorageService
{
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string? contentType = null);
    Task<Stream> DownloadFileAsync(string filePath);
    Task<bool> DeleteFileAsync(string filePath);
    Task<bool> FileExistsAsync(string filePath);
    Task<long> GetFileSizeAsync(string filePath);
    Task<string> GetFileUrlAsync(string filePath, TimeSpan? expiry = null);
    Task<IEnumerable<string>> ListFilesAsync(string? prefix = null);
    Task<string> MoveFileAsync(string sourcePath, string destinationPath);
    Task<string> CopyFileAsync(string sourcePath, string destinationPath);
}

public interface ISearchService
{
    Task<bool> IndexFileAsync(Models.FileMetadata fileMetadata);
    Task<IEnumerable<Models.FileMetadata>> SearchFilesAsync(string query, int skip = 0, int take = 20);
    Task<IEnumerable<Models.FileMetadata>> SearchAsync(string query, int skip = 0, int take = 20);
    Task<IEnumerable<Models.FileMetadata>> SearchByTagsAsync(IEnumerable<string> tags, int skip = 0, int take = 20);
    Task<Models.FileMetadata?> GetByIdAsync(Guid fileId);
    Task<bool> RemoveFromIndexAsync(Guid fileId);
    Task<bool> DeleteFromIndexAsync(Guid fileId);
    Task<long> GetIndexSizeAsync();
    Task<bool> UpdateFileMetadataAsync(Models.FileMetadata fileMetadata);
    Task<bool> UpdateIndexAsync(Models.FileMetadata fileMetadata);
    Task<IEnumerable<Models.FileMetadata>> GetFilesByStatusAsync(Models.FileStatus status, int skip = 0, int take = 50);
    Task<bool> InitializeIndexAsync();
}

public interface IScanService
{
    Task<Models.ScanResult> ScanFileAsync(Stream fileStream, string fileName);
    Task<Models.ScanResult> ScanFileAsync(string filePath);
    Task<bool> IsServiceHealthyAsync();
    Task UpdateVirusDefinitionsAsync();
}

public interface IMetadataExtractionService
{
    Task<Dictionary<string, object>> ExtractMetadataAsync(Stream fileStream, string fileName, string mimeType);
    Task<string?> ExtractTextAsync(Stream fileStream, string fileName, string mimeType);
    Task<byte[]?> GenerateThumbnailAsync(Stream fileStream, string fileName, string mimeType);
    Task<IEnumerable<string>> GenerateTagsAsync(string text, Dictionary<string, object> metadata);
}

public interface IBackupService
{
    Task<Guid> ScheduleBackupAsync(Models.BackupJob backupJob);
    Task<Models.BackupResult> ExecuteBackupAsync(Guid jobId);
    Task<bool> CancelBackupAsync(Guid jobId);
    Task<Models.BackupJob?> GetBackupJobAsync(Guid jobId);
    Task<IEnumerable<Models.BackupJob>> GetActiveBackupJobsAsync();
    Task<bool> DeleteBackupJobAsync(Guid jobId);
    Task<Models.BackupResult?> GetBackupResultAsync(Guid jobId);
    Task CleanupOldBackupsAsync(int retentionDays);
}

public interface IFileProcessingService
{
    Task ProcessFileAsync(Guid fileId);
    Task<Models.FileMetadata?> GetFileMetadataAsync(Guid fileId);
    Task<IEnumerable<Models.FileMetadata>> GetFilesByStatusAsync(Models.FileStatus status, int skip = 0, int take = 20);
    Task UpdateFileStatusAsync(Guid fileId, Models.FileStatus status);
    Task<string> CalculateChecksumAsync(Stream fileStream);
}

public interface INotificationService
{
    Task SendFileProcessedNotificationAsync(Models.FileMetadata fileMetadata);
    Task SendBackupCompletedNotificationAsync(Models.BackupResult backupResult);
    Task SendErrorNotificationAsync(string message, Exception? exception = null);
    Task SendHealthCheckNotificationAsync(string serviceName, bool isHealthy);
}

public interface IFileHashIndex
{
    Task<IReadOnlyCollection<Guid>> GetFileIdsAsync(string hash, CancellationToken cancellationToken = default);
    Task AddOrUpdateAsync(string hash, Guid fileId, CancellationToken cancellationToken = default);
}
