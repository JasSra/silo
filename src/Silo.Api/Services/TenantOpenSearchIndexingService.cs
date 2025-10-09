using OpenSearch.Client;
using Microsoft.Extensions.Logging;
using Silo.Core.Models;

namespace Silo.Api.Services;

/// <summary>
/// Tenant-aware OpenSearch indexing service
/// </summary>
public class TenantOpenSearchIndexingService
{
    private readonly OpenSearchClient _client;
    private readonly ILogger<TenantOpenSearchIndexingService> _logger;

    public TenantOpenSearchIndexingService(
        OpenSearchClient client,
        ILogger<TenantOpenSearchIndexingService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Get tenant-specific index name
    /// </summary>
    public string GetTenantIndexName(Guid tenantId, string indexType = "files")
    {
        // Pattern: tenant-{tenantId}-files
        return $"tenant-{tenantId:N}-{indexType}";
    }

    /// <summary>
    /// Initialize indexes for a new tenant
    /// </summary>
    public async Task InitializeTenantIndexesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing OpenSearch indexes for tenant {TenantId}", tenantId);

        var indexName = GetTenantIndexName(tenantId, "files");

        try
        {
            var existsResponse = await _client.Indices.ExistsAsync(indexName, ct: cancellationToken);

            if (!existsResponse.Exists)
            {
                var createResponse = await _client.Indices.CreateAsync(indexName, c => c
                    .Map<Silo.Core.Models.FileMetadata>(m => m
                        .AutoMap()
                    )
                    .Settings(s => s
                        .NumberOfShards(1)
                        .NumberOfReplicas(0)
                    ), cancellationToken);

                if (!createResponse.IsValid)
                {
                    _logger.LogError("Failed to create index {IndexName}: {Error}", indexName, createResponse.DebugInformation);
                    throw new InvalidOperationException($"Failed to create index {indexName}");
                }

                _logger.LogInformation("Created index {IndexName} for tenant {TenantId}", indexName, tenantId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize indexes for tenant {TenantId}", tenantId);
            throw;
        }
    }

    /// <summary>
    /// Index a file for a specific tenant
    /// </summary>
    public async Task IndexFileAsync(Guid tenantId, Silo.Core.Models.FileMetadata fileMetadata, CancellationToken cancellationToken = default)
    {
        var indexName = GetTenantIndexName(tenantId, "files");

        try
        {
            // Ensure index exists
            await EnsureIndexExistsAsync(tenantId, cancellationToken);

            var response = await _client.IndexAsync(fileMetadata, i => i
                .Index(indexName)
                .Id(fileMetadata.Id.ToString()), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("Failed to index file {FileId}: {Error}", fileMetadata.Id, response.DebugInformation);
                throw new InvalidOperationException($"Failed to index file {fileMetadata.Id}");
            }

            _logger.LogInformation("Indexed file {FileId} for tenant {TenantId}", fileMetadata.Id, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index file {FileId} for tenant {TenantId}", fileMetadata.Id, tenantId);
            throw;
        }
    }

    /// <summary>
    /// Search files for a specific tenant
    /// </summary>
    public async Task<IEnumerable<FileMetadata>> SearchFilesAsync(
        Guid tenantId,
        string query,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var indexName = GetTenantIndexName(tenantId, "files");

        try
        {
            var response = await _client.SearchAsync<FileMetadata>(s => s
                .Index(indexName)
                .Query(q => q
                    .QueryString(qs => qs
                        .Query(query)
                        .DefaultField("*")
                    )
                )
                .From(skip)
                .Size(take), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("Search failed for tenant {TenantId}: {Error}", tenantId, response.DebugInformation);
                return Enumerable.Empty<FileMetadata>();
            }

            return response.Documents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search files for tenant {TenantId}", tenantId);
            return Enumerable.Empty<FileMetadata>();
        }
    }

    /// <summary>
    /// Delete file from index
    /// </summary>
    public async Task DeleteFileAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var indexName = GetTenantIndexName(tenantId, "files");

        try
        {
            var response = await _client.DeleteAsync<FileMetadata>(fileId.ToString(), d => d
                .Index(indexName), cancellationToken);

            if (!response.IsValid && response.Result != Result.NotFound)
            {
                _logger.LogError("Failed to delete file {FileId} from index: {Error}", fileId, response.DebugInformation);
            }

            _logger.LogInformation("Deleted file {FileId} from index for tenant {TenantId}", fileId, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file {FileId} from index for tenant {TenantId}", fileId, tenantId);
        }
    }

    /// <summary>
    /// Delete all indexes for a tenant
    /// </summary>
    public async Task DeleteTenantIndexesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Deleting all indexes for tenant {TenantId}", tenantId);

        var indexName = GetTenantIndexName(tenantId, "files");

        try
        {
            var response = await _client.Indices.DeleteAsync(indexName, ct: cancellationToken);

            if (!response.IsValid && !response.ServerError.Error.Type.Equals("index_not_found_exception"))
            {
                _logger.LogError("Failed to delete index {IndexName}: {Error}", indexName, response.DebugInformation);
            }
            else
            {
                _logger.LogInformation("Deleted index {IndexName} for tenant {TenantId}", indexName, tenantId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete indexes for tenant {TenantId}", tenantId);
        }
    }

    private async Task EnsureIndexExistsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var indexName = GetTenantIndexName(tenantId, "files");
        var existsResponse = await _client.Indices.ExistsAsync(indexName, ct: cancellationToken);

        if (!existsResponse.Exists)
        {
            await InitializeTenantIndexesAsync(tenantId, cancellationToken);
        }
    }
}
