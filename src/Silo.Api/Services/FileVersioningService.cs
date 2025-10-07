using System.IO.Compression;
using System.Text;
using Silo.Core.Models;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Silo.Api.Services;

public interface IFileVersioningService
{
    Task<FileVersionDto> CreateVersionAsync(CreateVersionRequest request, Guid? userId = null, CancellationToken cancellationToken = default);
    Task<FileVersionDto?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken = default);
    Task<FileVersionHistoryDto> GetFileHistoryAsync(string filePath, CancellationToken cancellationToken = default);
    Task<Stream> GetVersionContentAsync(Guid versionId, CancellationToken cancellationToken = default);
    Task<FileVersionDto> RestoreVersionAsync(RestoreVersionRequest request, Guid? userId = null, CancellationToken cancellationToken = default);
    Task<VersionComparisonDto> CompareVersionsAsync(CompareVersionsRequest request, CancellationToken cancellationToken = default);
    Task DeleteVersionAsync(Guid versionId, CancellationToken cancellationToken = default);
    Task CleanupOldVersionsAsync(string? filePath = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileVersionDto>> GetVersionsAsync(int skip = 0, int take = 50, CancellationToken cancellationToken = default);
}

public class FileVersioningService : IFileVersioningService
{
    private readonly ILogger<FileVersioningService> _logger;
    private readonly IMinioStorageService _storageService;
    private readonly VersioningConfiguration _config;
    private readonly Dictionary<string, List<FileVersion>> _versionsByPath = new();
    private readonly Dictionary<Guid, FileVersion> _versionsById = new();
    private readonly SemaphoreSlim _versioningSemaphore = new(1, 1);

    public FileVersioningService(
        ILogger<FileVersioningService> logger,
        IMinioStorageService storageService,
        VersioningConfiguration config)
    {
        _logger = logger;
        _storageService = storageService;
        _config = config;
    }

    public async Task<FileVersionDto> CreateVersionAsync(
        CreateVersionRequest request, 
        Guid? userId = null, 
        CancellationToken cancellationToken = default)
    {
        if (!_config.EnableVersioning)
        {
            throw new InvalidOperationException("File versioning is disabled");
        }

        await _versioningSemaphore.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Creating new version for file: {FilePath}", request.FilePath);

            var checksum = await CalculateChecksumAsync(request.FileStream);
            
            // Check if this version already exists
            var existingVersions = GetFileVersions(request.FilePath);
            var existingVersion = existingVersions.FirstOrDefault(v => v.Checksum == checksum);
            if (existingVersion != null)
            {
                _logger.LogInformation("Version with same checksum already exists for {FilePath}, skipping", request.FilePath);
                return MapToDto(existingVersion);
            }

            var versionNumber = GetNextVersionNumber(request.FilePath);
            var storagePath = GenerateVersionStoragePath(request.FilePath, versionNumber);

            // Store the file content
            request.FileStream.Position = 0;
            Stream storeStream = request.FileStream;
            
            if (_config.CompressVersions)
            {
                storeStream = await CompressStreamAsync(request.FileStream);
                _logger.LogDebug("Compressed version for {FilePath}, original size: {OriginalSize}, compressed size: {CompressedSize}", 
                    request.FilePath, request.FileStream.Length, storeStream.Length);
            }

            await _storageService.UploadFileAsync(
                "file-versions", 
                storagePath, 
                storeStream,
                "application/octet-stream", 
                cancellationToken);

            // Create version record
            var version = new FileVersion
            {
                FilePath = request.FilePath,
                VersionNumber = versionNumber,
                StoragePath = storagePath,
                Checksum = checksum,
                FileSize = request.FileStream.Length,
                MimeType = GetMimeType(request.FilePath),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId,
                ChangeDescription = request.ChangeDescription,
                IsCurrentVersion = true,
                VersionType = request.VersionType,
                Metadata = request.Metadata ?? new Dictionary<string, string>()
            };

            // Mark previous version as non-current
            var previousCurrent = existingVersions.FirstOrDefault(v => v.IsCurrentVersion);
            if (previousCurrent != null)
            {
                previousCurrent.IsCurrentVersion = false;
                
                // Generate diff if enabled
                if (_config.GenerateDiffs)
                {
                    await GenerateDiffAsync(previousCurrent, version, cancellationToken);
                }
            }

            // Store version
            AddVersionToStorage(version);

            // Cleanup old versions if needed
            if (_config.RetentionPolicy.AutoCleanup)
            {
                await CleanupOldVersionsAsync(request.FilePath, cancellationToken);
            }

            _logger.LogInformation("Created version {VersionNumber} for file: {FilePath}", versionNumber, request.FilePath);
            return MapToDto(version);
        }
        finally
        {
            _versioningSemaphore.Release();
        }
    }

    public Task<FileVersionDto?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        if (_versionsById.TryGetValue(versionId, out var version))
        {
            return Task.FromResult<FileVersionDto?>(MapToDto(version));
        }

        return Task.FromResult<FileVersionDto?>(null);
    }

    public Task<FileVersionHistoryDto> GetFileHistoryAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var versions = GetFileVersions(filePath);
        var currentVersion = versions.FirstOrDefault(v => v.IsCurrentVersion);
        
        if (currentVersion == null)
        {
            throw new FileNotFoundException($"No versions found for file: {filePath}");
        }

        var versionDtos = versions
            .OrderByDescending(v => v.VersionNumber)
            .Select(MapToDto)
            .ToList();

        return Task.FromResult(new FileVersionHistoryDto(
            filePath,
            MapToDto(currentVersion),
            versionDtos,
            versions.Count));
    }

    public async Task<Stream> GetVersionContentAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        if (!_versionsById.TryGetValue(versionId, out var version))
        {
            throw new FileNotFoundException($"Version {versionId} not found");
        }

        var stream = await _storageService.DownloadFileAsync(_config.VersionStorageBucket, version.StoragePath, cancellationToken);
        
        // Decompress if needed
        if (_config.CompressVersions && version.Metadata.TryGetValue("compressed", out var compressed) && compressed == "true")
        {
            stream = new GZipStream(stream, CompressionMode.Decompress, false);
        }

        return stream;
    }

    public async Task<FileVersionDto> RestoreVersionAsync(
        RestoreVersionRequest request, 
        Guid? userId = null, 
        CancellationToken cancellationToken = default)
    {
        if (!_versionsById.TryGetValue(request.VersionId, out var version))
        {
            throw new FileNotFoundException($"Version {request.VersionId} not found");
        }

        _logger.LogInformation("Restoring version {VersionNumber} for file: {FilePath}", 
            version.VersionNumber, version.FilePath);

        if (request.MakeCurrentVersion)
        {
            // Create a new version from the restored content
            using var contentStream = await GetVersionContentAsync(request.VersionId, cancellationToken);
            
            var createRequest = new CreateVersionRequest(
                version.FilePath,
                contentStream,
                $"Restored from version {version.VersionNumber}",
                VersionType.Normal);

            return await CreateVersionAsync(createRequest, userId, cancellationToken);
        }
        else
        {
            // Just return the existing version
            return MapToDto(version);
        }
    }

    public async Task<VersionComparisonDto> CompareVersionsAsync(
        CompareVersionsRequest request, 
        CancellationToken cancellationToken = default)
    {
        if (!_versionsById.TryGetValue(request.SourceVersionId, out var sourceVersion))
        {
            throw new FileNotFoundException($"Source version {request.SourceVersionId} not found");
        }

        if (!_versionsById.TryGetValue(request.TargetVersionId, out var targetVersion))
        {
            throw new FileNotFoundException($"Target version {request.TargetVersionId} not found");
        }

        if (sourceVersion.FilePath != targetVersion.FilePath)
        {
            throw new ArgumentException("Cannot compare versions of different files");
        }

        _logger.LogInformation("Comparing versions {SourceVersion} and {TargetVersion} for file: {FilePath}",
            sourceVersion.VersionNumber, targetVersion.VersionNumber, sourceVersion.FilePath);

        // Check if diff already exists
        var existingDiff = sourceVersion.SourceDiffs.FirstOrDefault(d => d.TargetVersionId == request.TargetVersionId);
        if (existingDiff != null)
        {
            return new VersionComparisonDto(
                MapToDto(sourceVersion),
                MapToDto(targetVersion),
                existingDiff.DiffType,
                existingDiff.DiffContent,
                existingDiff.DiffSize,
                existingDiff.DiffMetadata);
        }

        // Generate diff
        var diffType = request.DiffType == "auto" ? DetectDiffType(sourceVersion.MimeType) : request.DiffType;
        
        using var sourceStream = await GetVersionContentAsync(request.SourceVersionId, cancellationToken);
        using var targetStream = await GetVersionContentAsync(request.TargetVersionId, cancellationToken);

        var (diffContent, diffSize, diffMetadata) = await GenerateDiffContentAsync(
            sourceStream, targetStream, diffType, cancellationToken);

        return new VersionComparisonDto(
            MapToDto(sourceVersion),
            MapToDto(targetVersion),
            diffType,
            diffContent,
            diffSize,
            diffMetadata);
    }

    public async Task DeleteVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        if (!_versionsById.TryGetValue(versionId, out var version))
        {
            throw new FileNotFoundException($"Version {versionId} not found");
        }

        if (version.IsCurrentVersion)
        {
            var otherVersions = GetFileVersions(version.FilePath).Where(v => v.Id != versionId).ToList();
            if (otherVersions.Any())
            {
                // Promote the most recent version to current
                var newCurrent = otherVersions.OrderByDescending(v => v.VersionNumber).First();
                newCurrent.IsCurrentVersion = true;
            }
        }

        // Delete from storage
        await _storageService.DeleteFileAsync(_config.VersionStorageBucket, version.StoragePath, cancellationToken);

        // Remove from memory storage
        RemoveVersionFromStorage(version);

        _logger.LogInformation("Deleted version {VersionNumber} for file: {FilePath}", 
            version.VersionNumber, version.FilePath);
    }

    public async Task CleanupOldVersionsAsync(string? filePath = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting version cleanup for file: {FilePath}", filePath ?? "all files");

        var filesToCleanup = filePath != null 
            ? new[] { filePath }
            : _versionsByPath.Keys.ToArray();

        foreach (var file in filesToCleanup)
        {
            await CleanupFileVersionsAsync(file, cancellationToken);
        }
    }

    public Task<IReadOnlyList<FileVersionDto>> GetVersionsAsync(int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var versions = _versionsById.Values
            .OrderByDescending(v => v.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(MapToDto)
            .ToList();

        return Task.FromResult<IReadOnlyList<FileVersionDto>>(versions);
    }

    private async Task CleanupFileVersionsAsync(string filePath, CancellationToken cancellationToken)
    {
        var versions = GetFileVersions(filePath);
        var policy = _config.RetentionPolicy;

        // Keep current version and major versions if configured
        var versionsToKeep = versions.Where(v => 
            v.IsCurrentVersion || 
            (policy.KeepMajorVersions && v.VersionType == VersionType.Major)).ToList();

        // Keep the most recent versions up to the limit
        var otherVersions = versions.Except(versionsToKeep)
            .OrderByDescending(v => v.VersionNumber)
            .ToList();

        var remainingSlots = Math.Max(0, policy.MaxVersionsPerFile - versionsToKeep.Count);
        versionsToKeep.AddRange(otherVersions.Take(remainingSlots));

        // Remove old versions by age
        if (policy.MaxVersionAge != default)
        {
            var cutoffDate = DateTime.UtcNow - policy.MaxVersionAge;
            versionsToKeep = versionsToKeep.Where(v => v.CreatedAt >= cutoffDate || v.IsCurrentVersion).ToList();
        }

        // Delete versions that should be removed
        var versionsToDelete = versions.Except(versionsToKeep).ToList();
        foreach (var version in versionsToDelete)
        {
            try
            {
                await DeleteVersionAsync(version.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete version {VersionId} during cleanup", version.Id);
            }
        }

        if (versionsToDelete.Any())
        {
            _logger.LogInformation("Cleaned up {Count} old versions for file: {FilePath}", 
                versionsToDelete.Count, filePath);
        }
    }

    private async Task GenerateDiffAsync(FileVersion sourceVersion, FileVersion targetVersion, CancellationToken cancellationToken)
    {
        try
        {
            var diffType = DetectDiffType(sourceVersion.MimeType);
            
            using var sourceStream = await GetVersionContentAsync(sourceVersion.Id, cancellationToken);
            using var targetStream = await GetVersionContentAsync(targetVersion.Id, cancellationToken);

            var (diffContent, diffSize, diffMetadata) = await GenerateDiffContentAsync(
                sourceStream, targetStream, diffType, cancellationToken);

            var diff = new FileDiff
            {
                SourceVersionId = sourceVersion.Id,
                TargetVersionId = targetVersion.Id,
                DiffType = diffType,
                DiffContent = diffContent,
                DiffSize = diffSize,
                GeneratedAt = DateTime.UtcNow,
                DiffMetadata = diffMetadata
            };

            sourceVersion.SourceDiffs.Add(diff);
            targetVersion.TargetDiffs.Add(diff);

            _logger.LogDebug("Generated {DiffType} diff between versions {SourceVersion} and {TargetVersion}",
                diffType, sourceVersion.VersionNumber, targetVersion.VersionNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate diff between versions {SourceVersion} and {TargetVersion}",
                sourceVersion.VersionNumber, targetVersion.VersionNumber);
        }
    }

    private async Task<(string? diffContent, long diffSize, Dictionary<string, object> metadata)> GenerateDiffContentAsync(
        Stream sourceStream, Stream targetStream, string diffType, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object>();

        if (diffType == "text")
        {
            var sourceText = await ReadStreamAsTextAsync(sourceStream);
            var targetText = await ReadStreamAsTextAsync(targetStream);

            var differ = new Differ();
            var diffBuilder = new InlineDiffBuilder(differ);
            var diff = diffBuilder.BuildDiffModel(sourceText, targetText);

            var diffLines = new List<string>();
            foreach (var line in diff.Lines)
            {
                var prefix = line.Type switch
                {
                    ChangeType.Inserted => "+",
                    ChangeType.Deleted => "-",
                    _ => " "
                };
                diffLines.Add($"{prefix}{line.Text}");
            }

            var diffContent = string.Join('\n', diffLines);
            metadata["added-lines"] = diff.Lines.Count(l => l.Type == ChangeType.Inserted);
            metadata["removed-lines"] = diff.Lines.Count(l => l.Type == ChangeType.Deleted);
            metadata["unchanged-lines"] = diff.Lines.Count(l => l.Type == ChangeType.Unchanged);

            return (diffContent, Encoding.UTF8.GetByteCount(diffContent), metadata);
        }
        else
        {
            // Binary diff - just store basic metadata
            metadata["source-size"] = sourceStream.Length;
            metadata["target-size"] = targetStream.Length;
            metadata["size-difference"] = targetStream.Length - sourceStream.Length;

            return (null, 0, metadata);
        }
    }

    private string DetectDiffType(string mimeType)
    {
        return mimeType.StartsWith("text/") || 
               mimeType == "application/json" || 
               mimeType == "application/xml" 
            ? "text" 
            : "binary";
    }

    private async Task<string> ReadStreamAsTextAsync(Stream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private async Task<Stream> CompressStreamAsync(Stream source)
    {
        var compressed = new MemoryStream();
        source.Position = 0;
        
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            await source.CopyToAsync(gzip);
        }
        
        compressed.Position = 0;
        return compressed;
    }

    private async Task<string> CalculateChecksumAsync(Stream stream)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        stream.Position = 0;
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hashBytes);
    }

    private string GetMimeType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }

    private int GetNextVersionNumber(string filePath)
    {
        var versions = GetFileVersions(filePath);
        return versions.Any() ? versions.Max(v => v.VersionNumber) + 1 : 1;
    }

    private string GenerateVersionStoragePath(string filePath, int versionNumber)
    {
        var fileName = Path.GetFileName(filePath);
        var directory = Path.GetDirectoryName(filePath)?.Replace('\\', '/') ?? "";
        return $"{directory}/{fileName}/v{versionNumber:D6}";
    }

    private List<FileVersion> GetFileVersions(string filePath)
    {
        return _versionsByPath.TryGetValue(filePath, out var versions) ? versions : new List<FileVersion>();
    }

    private void AddVersionToStorage(FileVersion version)
    {
        if (!_versionsByPath.TryGetValue(version.FilePath, out var versions))
        {
            versions = new List<FileVersion>();
            _versionsByPath[version.FilePath] = versions;
        }

        versions.Add(version);
        _versionsById[version.Id] = version;
    }

    private void RemoveVersionFromStorage(FileVersion version)
    {
        if (_versionsByPath.TryGetValue(version.FilePath, out var versions))
        {
            versions.Remove(version);
            if (!versions.Any())
            {
                _versionsByPath.Remove(version.FilePath);
            }
        }

        _versionsById.Remove(version.Id);
    }

    private FileVersionDto MapToDto(FileVersion version)
    {
        return new FileVersionDto(
            version.Id,
            version.FilePath,
            version.VersionNumber,
            version.Checksum,
            version.FileSize,
            version.MimeType,
            version.CreatedAt,
            version.CreatedBy,
            version.ChangeDescription,
            version.IsCurrentVersion,
            version.VersionType,
            version.Metadata);
    }
}