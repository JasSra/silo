using System.Security.Cryptography;
using System.Text;
using Hangfire;
using Silo.Core.Models;
using Silo.Core.Pipeline;
using Silo.Core.Services;

namespace Silo.Api.Services;

public interface IFileSyncService
{
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);
    Task StopMonitoringAsync(CancellationToken cancellationToken = default);
    Task SyncDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);
    Task<SyncStatus> GetSyncStatusAsync(CancellationToken cancellationToken = default);
}

public record SyncStatus(
    bool IsMonitoring,
    DateTime LastSyncTime,
    int PendingSyncCount,
    string[] MonitoredPaths);

public class FileSyncService : IFileSyncService, IDisposable
{
    private readonly ILogger<FileSyncService> _logger;
    private readonly IMinioStorageService _storageService;
    private readonly IOpenSearchIndexingService _indexingService;
    private readonly IPipelineOrchestrator _pipelineOrchestrator;
    private readonly ITenantContextProvider _tenantContextProvider;
    private readonly FileSyncConfiguration _config;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly SemaphoreSlim _syncSemaphore = new(1, 1);
    private readonly Timer? _conflictResolutionTimer;
    private bool _isMonitoring = false;
    private DateTime _lastSyncTime = DateTime.MinValue;
    private readonly HashSet<string> _pendingSyncFiles = new();

    public FileSyncService(
        ILogger<FileSyncService> logger,
        IMinioStorageService storageService,
        IOpenSearchIndexingService indexingService,
        IPipelineOrchestrator pipelineOrchestrator,
        ITenantContextProvider tenantContextProvider,
        FileSyncConfiguration config)
    {
        _logger = logger;
        _storageService = storageService;
        _indexingService = indexingService;
        _pipelineOrchestrator = pipelineOrchestrator;
        _tenantContextProvider = tenantContextProvider;
        _config = config;
        
        // Setup periodic conflict resolution
        _conflictResolutionTimer = new Timer(ResolveConflicts, null, 
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_isMonitoring)
        {
            _logger.LogWarning("File sync monitoring is already active");
            return;
        }

        _logger.LogInformation("Starting file sync monitoring for {PathCount} paths", _config.MonitoredPaths.Count);

        foreach (var path in _config.MonitoredPaths)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogWarning("Monitored path does not exist: {Path}", path);
                continue;
            }

            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                Filter = "*.*",
                IncludeSubdirectories = _config.IncludeSubdirectories,
                EnableRaisingEvents = true
            };

            watcher.Created += OnFileChanged;
            watcher.Changed += OnFileChanged;
            watcher.Deleted += OnFileDeleted;
            watcher.Renamed += OnFileRenamed;
            watcher.Error += OnWatcherError;

            _watchers[path] = watcher;
            _logger.LogInformation("Started monitoring path: {Path}", path);
        }

        _isMonitoring = true;
        
        // Perform initial sync
        await PerformInitialSyncAsync(cancellationToken);
    }

    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (!_isMonitoring)
        {
            return;
        }

        _logger.LogInformation("Stopping file sync monitoring");

        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        _isMonitoring = false;

        _logger.LogInformation("File sync monitoring stopped");
    }

    public async Task SyncDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        await _syncSemaphore.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Starting directory sync for: {DirectoryPath}", directoryPath);

            var files = Directory.GetFiles(directoryPath, "*.*", 
                _config.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            var syncTasks = files
                .Where(ShouldSyncFile)
                .Select(filePath => SyncFileAsync(filePath, cancellationToken))
                .ToArray();

            await Task.WhenAll(syncTasks);
            _lastSyncTime = DateTime.UtcNow;

            _logger.LogInformation("Directory sync completed for: {DirectoryPath}, synced {FileCount} files", 
                directoryPath, syncTasks.Length);
        }
        finally
        {
            _syncSemaphore.Release();
        }
    }

    public Task<SyncStatus> GetSyncStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = new SyncStatus(
            _isMonitoring,
            _lastSyncTime,
            _pendingSyncFiles.Count,
            _config.MonitoredPaths.ToArray());

        return Task.FromResult(status);
    }

    private async Task PerformInitialSyncAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing initial sync for all monitored paths");

        foreach (var path in _config.MonitoredPaths)
        {
            if (Directory.Exists(path))
            {
                await SyncDirectoryAsync(path, cancellationToken);
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!ShouldSyncFile(e.FullPath))
        {
            return;
        }

        _logger.LogDebug("File change detected: {FilePath} ({ChangeType})", e.FullPath, e.ChangeType);
        
        // Add to pending sync with delay to handle rapid changes
        lock (_pendingSyncFiles)
        {
            _pendingSyncFiles.Add(e.FullPath);
        }

        // Schedule sync with delay to batch changes
        BackgroundJob.Schedule(() => ProcessPendingSyncFile(e.FullPath), TimeSpan.FromSeconds(_config.SyncDelaySeconds));
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        _logger.LogInformation("File deleted: {FilePath}", e.FullPath);
        
        // Schedule deletion from storage and index
        BackgroundJob.Enqueue(() => HandleFileDeletedAsync(e.FullPath));
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        _logger.LogInformation("File renamed: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);
        
        // Handle as delete old + add new
        BackgroundJob.Enqueue(() => HandleFileRenamedAsync(e.OldFullPath, e.FullPath));
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "FileSystemWatcher error occurred");
        
        // Try to restart monitoring
        BackgroundJob.Schedule(() => RestartMonitoringAsync(), TimeSpan.FromMinutes(1));
    }

    [Queue("file-processing")]
    public async Task ProcessPendingSyncFile(string filePath)
    {
        try
        {
            // Remove from pending list
            lock (_pendingSyncFiles)
            {
                _pendingSyncFiles.Remove(filePath);
            }

            if (File.Exists(filePath) && ShouldSyncFile(filePath))
            {
                await SyncFileAsync(filePath, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing pending sync for file: {FilePath}", filePath);
        }
    }

    [Queue("file-processing")]
    public async Task HandleFileDeletedAsync(string filePath)
    {
        try
        {
            var relativePath = GetRelativePath(filePath);
            
            // Remove from storage
            var existsInStorage = await _storageService.FileExistsAsync("files", relativePath);
            if (existsInStorage)
            {
                await _storageService.DeleteFileAsync("files", relativePath);
                _logger.LogInformation("Deleted file from storage: {RelativePath}", relativePath);
            }

            // Remove from search index
            await _indexingService.DeleteFileAsync(relativePath);
            _logger.LogInformation("Deleted file from index: {RelativePath}", relativePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file deletion: {FilePath}", filePath);
        }
    }

    [Queue("file-processing")]
    public async Task HandleFileRenamedAsync(string oldPath, string newPath)
    {
        try
        {
            // Delete old
            await HandleFileDeletedAsync(oldPath);
            
            // Add new if it should be synced
            if (ShouldSyncFile(newPath))
            {
                await SyncFileAsync(newPath, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file rename: {OldPath} -> {NewPath}", oldPath, newPath);
        }
    }

    [Queue("file-processing")]
    public async Task RestartMonitoringAsync()
    {
        try
        {
            _logger.LogInformation("Attempting to restart file monitoring");
            await StopMonitoringAsync();
            await Task.Delay(5000); // Wait 5 seconds
            await StartMonitoringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting file monitoring");
        }
    }

    private async Task SyncFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogDebug("File no longer exists, skipping sync: {FilePath}", filePath);
                return;
            }

            var fileInfo = new FileInfo(filePath);
            var relativePath = GetRelativePath(filePath);
            
            // Check if file has changed since last sync
            var lastWriteTime = fileInfo.LastWriteTimeUtc;
            var existingMetadata = await GetExistingFileMetadata(relativePath);
            
            if (existingMetadata != null && existingMetadata.LastModified >= lastWriteTime)
            {
                _logger.LogDebug("File hasn't changed since last sync: {FilePath}", filePath);
                return;
            }

            _logger.LogInformation("Syncing file: {FilePath}", filePath);

            // Create file metadata
            var checksum = await CalculateFileChecksumAsync(filePath);
            var mimeType = GetMimeType(filePath);
            
            var metadata = new Core.Models.FileMetadata
            {
                FileName = fileInfo.Name,
                OriginalPath = relativePath,
                StoragePath = relativePath,
                FileSize = fileInfo.Length,
                MimeType = mimeType,
                Checksum = checksum,
                LastModified = lastWriteTime,
                CreatedAt = DateTime.UtcNow
            };

            // Upload file through pipeline
            using var fileStream = File.OpenRead(filePath);
            var context = new PipelineContext
            {
                FileMetadata = metadata,
                FileStream = fileStream,
                TenantId = _tenantContextProvider.GetCurrentTenantId()
            };

            var result = await _pipelineOrchestrator.ExecuteAsync(context, cancellationToken);
            
            if (result.Success)
            {
                _logger.LogInformation("Successfully synced file: {FilePath}", filePath);
            }
            else
            {
                _logger.LogError("Failed to sync file {FilePath}: {ErrorMessage}", filePath, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing file: {FilePath}", filePath);
        }
    }

    private bool ShouldSyncFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        
        // Check file size limits
        if (fileInfo.Length > _config.MaxFileSizeBytes)
        {
            return false;
        }

        // Check excluded patterns
        if (_config.ExcludePatterns.Any(pattern => 
            filePath.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Check included extensions
        if (_config.IncludeExtensions.Any() && 
            !_config.IncludeExtensions.Contains(fileInfo.Extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private string GetRelativePath(string fullPath)
    {
        foreach (var monitoredPath in _config.MonitoredPaths)
        {
            if (fullPath.StartsWith(monitoredPath, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(monitoredPath, fullPath).Replace('\\', '/');
            }
        }
        
        return Path.GetFileName(fullPath);
    }

    private async Task<string> CalculateFileChecksumAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var fileStream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(fileStream);
        return Convert.ToHexString(hashBytes);
    }

    private string GetMimeType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            ".zip" => "application/zip",
            ".json" => "application/json",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }

    private async Task<Core.Models.FileMetadata?> GetExistingFileMetadata(string relativePath)
    {
        try
        {
            var searchResults = await _indexingService.SearchFilesAsync($"filePath:\"{relativePath}\"", 1);
            var apiMetadata = searchResults.FirstOrDefault();
            if (apiMetadata == null) return null;
            
            // Convert from API FileMetadata to Core FileMetadata
            return new Core.Models.FileMetadata
            {
                Id = Guid.Parse(apiMetadata.Id),
                FileName = apiMetadata.FileName,
                OriginalPath = apiMetadata.FilePath,
                StoragePath = apiMetadata.FilePath,
                FileSize = apiMetadata.FileSize,
                MimeType = apiMetadata.MimeType,
                CreatedAt = apiMetadata.CreatedAt,
                LastModified = apiMetadata.UpdatedAt
            };
        }
        catch
        {
            return null;
        }
    }

    private void ResolveConflicts(object? state)
    {
        // Placeholder for conflict resolution logic
        // This could handle cases where files were modified both locally and remotely
        _logger.LogDebug("Running conflict resolution check");
    }

    public void Dispose()
    {
        _conflictResolutionTimer?.Dispose();
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }
        _syncSemaphore.Dispose();
    }
}

public class FileSyncConfiguration
{
    public List<string> MonitoredPaths { get; set; } = new();
    public bool IncludeSubdirectories { get; set; } = true;
    public long MaxFileSizeBytes { get; set; } = 1024 * 1024 * 1024; // 1GB
    public List<string> ExcludePatterns { get; set; } = new() { ".tmp", ".temp", "~", ".git", "node_modules" };
    public List<string> IncludeExtensions { get; set; } = new(); // Empty = all extensions
    public int SyncDelaySeconds { get; set; } = 5; // Delay to batch rapid changes
}