using Silo.Core.Services;
using Silo.Core.Models;

namespace Silo.Api.Services;

public class SearchServiceAdapter : ISearchService
{
    private readonly IOpenSearchIndexingService _indexingService;
    private readonly ILogger<SearchServiceAdapter> _logger;

    public SearchServiceAdapter(IOpenSearchIndexingService indexingService, ILogger<SearchServiceAdapter> logger)
    {
        _indexingService = indexingService;
        _logger = logger;
    }

    public async Task<bool> IndexFileAsync(Core.Models.FileMetadata fileMetadata)
    {
        try
        {
            // Convert Core.Models.FileMetadata to API FileMetadata
            var apiFileMetadata = new Api.Services.FileMetadata
            {
                Id = fileMetadata.Id.ToString(),
                FileName = fileMetadata.FileName,
                FilePath = fileMetadata.OriginalPath,
                FileSize = fileMetadata.FileSize,
                MimeType = fileMetadata.MimeType,
                CreatedAt = fileMetadata.CreatedAt,
                UpdatedAt = fileMetadata.LastModified ?? fileMetadata.CreatedAt
            };

            await _indexingService.IndexFileAsync(apiFileMetadata);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index file {FileId}", fileMetadata.Id);
            return false;
        }
    }

    public async Task<IEnumerable<Core.Models.FileMetadata>> SearchFilesAsync(string query, int skip = 0, int take = 20)
    {
        try
        {
            var results = await _indexingService.SearchFilesAsync(query, take);
            return results.Skip(skip).Take(take).Select(ConvertToCoreFileMetadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search files with query {Query}", query);
            return Enumerable.Empty<Core.Models.FileMetadata>();
        }
    }

    public async Task<IEnumerable<Core.Models.FileMetadata>> SearchAsync(string query, int skip = 0, int take = 20)
    {
        return await SearchFilesAsync(query, skip, take);
    }

    public async Task<IEnumerable<Core.Models.FileMetadata>> SearchByTagsAsync(IEnumerable<string> tags, int skip = 0, int take = 20)
    {
        var query = string.Join(" OR ", tags.Select(tag => $"tags:\"{tag}\""));
        return await SearchFilesAsync(query, skip, take);
    }

    public async Task<Core.Models.FileMetadata?> GetByIdAsync(Guid fileId)
    {
        try
        {
            var results = await _indexingService.SearchFilesAsync($"id:\"{fileId}\"", 1);
            var result = results.FirstOrDefault();
            return result != null ? ConvertToCoreFileMetadata(result) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get file by ID {FileId}", fileId);
            return null;
        }
    }

    public async Task<bool> RemoveFromIndexAsync(Guid fileId)
    {
        return await DeleteFromIndexAsync(fileId);
    }

    public async Task<bool> DeleteFromIndexAsync(Guid fileId)
    {
        try
        {
            await _indexingService.DeleteFileFromIndexAsync(fileId.ToString());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file from index {FileId}", fileId);
            return false;
        }
    }

    public async Task<long> GetIndexSizeAsync()
    {
        try
        {
            // This is a rough estimation since we don't have a direct way to get index size
            var allFiles = await _indexingService.SearchFilesAsync("*", 10000);
            return allFiles.Count();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get index size");
            return 0;
        }
    }

    public async Task<bool> UpdateFileMetadataAsync(Core.Models.FileMetadata fileMetadata)
    {
        return await IndexFileAsync(fileMetadata);
    }

    public async Task<bool> UpdateIndexAsync(Core.Models.FileMetadata fileMetadata)
    {
        return await IndexFileAsync(fileMetadata);
    }

    public async Task<IEnumerable<Core.Models.FileMetadata>> GetFilesByStatusAsync(FileStatus status, int skip = 0, int take = 50)
    {
        try
        {
            var query = $"status:\"{status}\"";
            return await SearchFilesAsync(query, skip, take);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get files by status {Status}", status);
            return Enumerable.Empty<Core.Models.FileMetadata>();
        }
    }

    public async Task<bool> InitializeIndexAsync()
    {
        try
        {
            var indexExists = await _indexingService.IndexExistsAsync("files");
            if (!indexExists)
            {
                await _indexingService.CreateIndexAsync("files");
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize index");
            return false;
        }
    }

    private static Core.Models.FileMetadata ConvertToCoreFileMetadata(Api.Services.FileMetadata apiMetadata)
    {
        return new Core.Models.FileMetadata
        {
            Id = Guid.Parse(apiMetadata.Id),
            FileName = apiMetadata.FileName,
            OriginalPath = apiMetadata.FilePath,
            StoragePath = apiMetadata.FilePath,
            FileSize = apiMetadata.FileSize,
            MimeType = apiMetadata.MimeType,
            CreatedAt = apiMetadata.CreatedAt,
            LastModified = apiMetadata.UpdatedAt,
            Status = FileStatus.Processed // Default status
        };
    }
}