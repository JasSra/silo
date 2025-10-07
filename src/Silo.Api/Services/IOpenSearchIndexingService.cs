namespace Silo.Api.Services;

public interface IOpenSearchIndexingService
{
    Task IndexFileAsync(FileMetadata fileMetadata, CancellationToken cancellationToken = default);
    Task DeleteFileFromIndexAsync(string fileId, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string fileId, CancellationToken cancellationToken = default);
    Task<IEnumerable<FileMetadata>> SearchFilesAsync(string query, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<FileMetadata>> AdvancedSearchFilesAsync(
        string query = "",
        IEnumerable<string>? extensions = null,
        long? minSize = null,
        long? maxSize = null,
        string? wildcardPattern = null,
        string? context = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
    Task<Dictionary<string, object>> GetFileStatisticsAsync(CancellationToken cancellationToken = default);
    Task<bool> IndexExistsAsync(string indexName, CancellationToken cancellationToken = default);
    Task CreateIndexAsync(string indexName, CancellationToken cancellationToken = default);
    Task RefreshIndexAsync(string indexName, CancellationToken cancellationToken = default);
    Task<Silo.Core.Models.FileMetadata?> GetFileByIdAsync(Guid fileId, CancellationToken cancellationToken = default);
}

public class FileMetadata
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}