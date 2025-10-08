using System.Collections.Generic;
using OpenSearch.Client;
using OpenSearch.Net;

namespace Silo.Api.Services;

public class OpenSearchIndexingService : IOpenSearchIndexingService
{
    private readonly OpenSearchClient _client;
    private readonly string _indexName;
    private readonly ILogger<OpenSearchIndexingService> _logger;

    public OpenSearchIndexingService(OpenSearchClient client, IConfiguration configuration, ILogger<OpenSearchIndexingService> logger)
    {
        _client = client;
        _indexName = configuration.GetValue<string>("OpenSearch:IndexName") ?? "files";
        _logger = logger;
    }

    public async Task IndexFileAsync(FileMetadata fileMetadata, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.IndexAsync(fileMetadata, idx => idx
                .Index(_indexName)
                .Id(fileMetadata.Id), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("Failed to index file {FileId}: {Error}", fileMetadata.Id, response.DebugInformation);
            }
            else
            {
                _logger.LogInformation("File {FileId} indexed successfully", fileMetadata.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing file {FileId}", fileMetadata.Id);
        }
    }

    public async Task DeleteFileFromIndexAsync(string fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.DeleteAsync<FileMetadata>(fileId, d => d.Index(_indexName), cancellationToken);
            
            if (!response.IsValid)
            {
                _logger.LogError("Failed to delete file {FileId} from index: {Error}", fileId, response.DebugInformation);
            }
            else
            {
                _logger.LogInformation("File {FileId} deleted from index successfully", fileId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileId} from index", fileId);
        }
    }

    public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        // For the OpenSearch service, DeleteFileAsync can be the same as DeleteFileFromIndexAsync
        await DeleteFileFromIndexAsync(fileId, cancellationToken);
    }

    public async Task<IEnumerable<FileMetadata>> SearchFilesAsync(string query, int limit = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SearchAsync<FileMetadata>(s => s
                .Index(_indexName)
                .Query(q => q
                    .MultiMatch(mm => mm
                        .Query(query)
                        .Fields(f => f
                            .Field(fm => fm.Id)
                            .Field(fm => fm.FileName)
                            .Field(fm => fm.MimeType))))
                .Size(limit), cancellationToken);

            if (response.IsValid)
            {
                return response.Documents;
            }
            else
            {
                _logger.LogError("Search failed: {Error}", response.DebugInformation);
                return Enumerable.Empty<FileMetadata>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching files with query: {Query}", query);
            return Enumerable.Empty<FileMetadata>();
        }
    }

    public async Task<IEnumerable<FileMetadata>> AdvancedSearchFilesAsync(
        string query = "",
        IEnumerable<string>? extensions = null,
        long? minSize = null,
        long? maxSize = null,
        string? wildcardPattern = null,
        string? context = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SearchAsync<FileMetadata>(s => s
                .Index(_indexName)
                .Query(q =>
                {
                    var mustQueries = new List<Func<QueryContainerDescriptor<FileMetadata>, QueryContainer>>();

                    // Enhanced text search with partial matching
                    if (!string.IsNullOrWhiteSpace(query))
                    {
                        mustQueries.Add(qs => qs.Bool(b => b
                            .Should(
                                // Exact match (highest priority)
                                sh => sh.MultiMatch(mm => mm
                                    .Query(query)
                                    .Fields(f => f
                                        .Field(fm => fm.FileName, 2.0)
                                        .Field(fm => fm.FilePath, 1.5)
                                        .Field(fm => fm.MimeType))
                                    .Type(TextQueryType.BestFields)),
                                // Wildcard match for partial search
                                sh => sh.Wildcard(w => w
                                    .Field(fm => fm.FileName)
                                    .Value($"*{query.ToLower()}*")),
                                // Fuzzy match for typos
                                sh => sh.Fuzzy(f => f
                                    .Field(fm => fm.FileName)
                                    .Value(query)
                                    .Fuzziness(Fuzziness.Auto))
                            ).MinimumShouldMatch(1)));
                    }

                    // Wildcard pattern search on filename
                    if (!string.IsNullOrWhiteSpace(wildcardPattern))
                    {
                        mustQueries.Add(qs => qs.Wildcard(w => w
                            .Field(fm => fm.FileName)
                            .Value(wildcardPattern.ToLower())));
                    }

                    // File extension filter - Fixed logic
                    if (extensions != null && extensions.Any())
                    {
                        var extensionQueries = extensions.Select(ext => 
                            new Func<QueryContainerDescriptor<FileMetadata>, QueryContainer>(qs => 
                                qs.Wildcard(w => w
                                    .Field(fm => fm.FileName)
                                    .Value($"*.{ext.TrimStart('.').ToLower()}"))));
                        
                        mustQueries.Add(qs => qs.Bool(b => b
                            .Should(extensionQueries.ToArray())
                            .MinimumShouldMatch(1)));
                    }

                    // File size range filter
                    if (minSize.HasValue || maxSize.HasValue)
                    {
                        mustQueries.Add(qs => qs.Range(r => r
                            .Field(fm => fm.FileSize)
                            .GreaterThanOrEquals(minSize)
                            .LessThanOrEquals(maxSize)));
                    }

                    // Context/bucket search (if file path contains context)
                    if (!string.IsNullOrWhiteSpace(context))
                    {
                        mustQueries.Add(qs => qs.Wildcard(w => w
                            .Field(fm => fm.FilePath)
                            .Value($"*{context.ToLower()}*")));
                    }

                    // Combine all queries with AND logic (all must match)
                    if (mustQueries.Count > 0)
                    {
                        return q.Bool(b => b.Must(mustQueries.ToArray()));
                    }
                    else
                    {
                        return q.MatchAll();
                    }
                })
                .Size(limit), cancellationToken);

            if (response.IsValid)
            {
                return response.Documents;
            }
            else
            {
                _logger.LogError("Advanced search failed: {Error}", response.DebugInformation);
                return Enumerable.Empty<FileMetadata>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in advanced search");
            return Enumerable.Empty<FileMetadata>();
        }
    }

    public async Task<Dictionary<string, object>> GetFileStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Simple search to get total count first
            var countResponse = await _client.SearchAsync<FileMetadata>(s => s
                .Index(_indexName)
                .Size(0)
                .Query(q => q.MatchAll()), cancellationToken);

            if (!countResponse.IsValid)
            {
                _logger.LogError("Statistics count query failed: {Error}", countResponse.DebugInformation);
                return new Dictionary<string, object>();
            }

            var totalFiles = countResponse.Total;

            // Get all files for detailed statistics (limited to reasonable amount)
            var searchResponse = await _client.SearchAsync<FileMetadata>(s => s
                .Index(_indexName)
                .Size(1000) // Limit to avoid memory issues
                .Query(q => q.MatchAll()), cancellationToken);

            if (!searchResponse.IsValid)
            {
                _logger.LogError("Statistics search query failed: {Error}", searchResponse.DebugInformation);
                return new Dictionary<string, object>
                {
                    ["totalFiles"] = totalFiles,
                    ["totalSize"] = 0,
                    ["fileTypes"] = new Dictionary<string, int>(),
                    ["extensions"] = new Dictionary<string, int>()
                };
            }

            var files = searchResponse.Documents.ToList();
            
            var stats = new Dictionary<string, object>
            {
                ["totalFiles"] = totalFiles,
                ["totalSize"] = files.Sum(f => f.FileSize),
                ["fileTypes"] = files.GroupBy(f => f.MimeType ?? "unknown")
                                   .ToDictionary(g => g.Key, g => g.Count()),
                ["extensions"] = files.GroupBy(f => 
                                   {
                                       var fileName = f.FileName ?? "";
                                       var lastDot = fileName.LastIndexOf('.');
                                       return lastDot >= 0 ? fileName.Substring(lastDot + 1).ToLower() : "no-extension";
                                   })
                                   .ToDictionary(g => g.Key, g => g.Count()),
                ["sizeStats"] = new
                {
                    min = files.Any() ? files.Min(f => f.FileSize) : 0,
                    max = files.Any() ? files.Max(f => f.FileSize) : 0,
                    avg = files.Any() ? files.Average(f => f.FileSize) : 0,
                    sum = files.Sum(f => f.FileSize),
                    count = files.Count
                },
                ["filesByDate"] = files.GroupBy(f => f.CreatedAt.Date.ToString("yyyy-MM-dd"))
                                      .ToDictionary(g => g.Key, g => g.Count())
            };

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file statistics");
            return new Dictionary<string, object>
            {
                ["totalFiles"] = 0,
                ["totalSize"] = 0,
                ["fileTypes"] = new Dictionary<string, int>(),
                ["extensions"] = new Dictionary<string, int>()
            };
        }
    }

    public async Task<bool> IndexExistsAsync(string indexName, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Indices.ExistsAsync(indexName, ct: cancellationToken);
            return response.Exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if index {IndexName} exists", indexName);
            return false;
        }
    }

    public async Task CreateIndexAsync(string indexName, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Indices.CreateAsync(indexName, c => c
                .Map<FileMetadata>(m => m
                    .Properties(p => p
                        .Text(t => t.Name(n => n.FileName).Analyzer("standard"))
                        .Keyword(k => k.Name(n => n.MimeType))
                        .Number(n => n.Name(nm => nm.FileSize))
                        .Text(t => t.Name(n => n.FilePath))
                        .Date(d => d.Name(n => n.CreatedAt))
                        .Date(d => d.Name(n => n.UpdatedAt))
                    )
                ), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("Failed to create index {IndexName}: {Error}", indexName, response.DebugInformation);
            }
            else
            {
                _logger.LogInformation("Index {IndexName} created successfully", indexName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating index {IndexName}", indexName);
        }
    }

    public async Task RefreshIndexAsync(string indexName, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Indices.RefreshAsync(indexName, ct: cancellationToken);
            
            if (!response.IsValid)
            {
                _logger.LogError("Failed to refresh index {IndexName}: {Error}", indexName, response.DebugInformation);
            }
            else
            {
                _logger.LogInformation("Index {IndexName} refreshed successfully", indexName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing index {IndexName}", indexName);
        }
    }

    public async Task<Silo.Core.Models.FileMetadata?> GetFileByIdAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Retrieving file by ID: {FileId}", fileId);

            var response = await _client.GetAsync<FileMetadata>(fileId.ToString(), g => g.Index(_indexName), cancellationToken);

            if (!response.IsValid)
            {
                if (response.Found == false)
                {
                    _logger.LogInformation("File not found in index: {FileId}", fileId);
                    return null;
                }
                
                _logger.LogError("Failed to retrieve file {FileId}: {Error}", fileId, response.DebugInformation);
                return null;
            }

            if (!response.Found || response.Source == null)
            {
                _logger.LogInformation("File not found: {FileId}", fileId);
                return null;
            }

            // Convert from OpenSearch FileMetadata to Core FileMetadata
            var openSearchFile = response.Source;
            var metadata = openSearchFile.Metadata != null
                ? new Dictionary<string, object>(openSearchFile.Metadata)
                : new Dictionary<string, object>();

            var checksum = metadata.TryGetValue("checksum", out var checksumValue) && checksumValue is string checksumString
                ? checksumString
                : metadata.TryGetValue("hash_sha256", out var hashValue) && hashValue is string hashString
                    ? hashString
                    : string.Empty;

            var coreFileMetadata = new Silo.Core.Models.FileMetadata
            {
                Id = Guid.TryParse(openSearchFile.Id, out var parsedId) ? parsedId : Guid.Empty,
                FileName = openSearchFile.FileName,
                OriginalPath = openSearchFile.FilePath,
                StoragePath = openSearchFile.FilePath,
                FileSize = openSearchFile.FileSize,
                MimeType = openSearchFile.MimeType,
                Checksum = checksum,
                Status = Silo.Core.Models.FileStatus.Indexed, // Assume indexed if in index
                CreatedAt = openSearchFile.CreatedAt,
                LastModified = openSearchFile.UpdatedAt,
                Tags = new List<string>(), // Default empty
                Metadata = metadata
            };

            _logger.LogInformation("Successfully retrieved file: {FileId}", fileId);
            return coreFileMetadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving file by ID: {FileId}", fileId);
            return null;
        }
    }
}
