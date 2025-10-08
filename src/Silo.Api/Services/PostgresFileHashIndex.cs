using System;
using System.Collections.Generic;
using System.Threading;
using Npgsql;
using Silo.Core.Services;

namespace Silo.Api.Services;

public class PostgresFileHashIndex : IFileHashIndex, IAsyncDisposable
{
    private const string TableName = "file_hash_index";

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresFileHashIndex> _logger;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _schemaInitialized;

    public PostgresFileHashIndex(NpgsqlDataSource dataSource, ILogger<PostgresFileHashIndex> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<Guid>> GetFileIdsAsync(string hash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return Array.Empty<Guid>();
        }

        await EnsureSchemaAsync(cancellationToken);

        var ids = new List<Guid>();

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT file_id FROM {TableName} WHERE hash = @hash";
            command.Parameters.AddWithValue("hash", hash);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    ids.Add(reader.GetGuid(0));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read file hash index for hash {Hash}", hash);
            throw;
        }

        return ids;
    }

    public async Task AddOrUpdateAsync(string hash, Guid fileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return;
        }

        await EnsureSchemaAsync(cancellationToken);

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                INSERT INTO {TableName} (hash, file_id, created_at)
                VALUES (@hash, @fileId, NOW())
                ON CONFLICT (hash, file_id) DO NOTHING;";

            command.Parameters.AddWithValue("hash", hash);
            command.Parameters.AddWithValue("fileId", fileId);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert hash index for file {FileId}", fileId);
            throw;
        }
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaInitialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaInitialized)
            {
                return;
            }

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {TableName} (
                    hash VARCHAR(128) NOT NULL,
                    file_id UUID NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    PRIMARY KEY (hash, file_id)
                );
                CREATE INDEX IF NOT EXISTS idx_{TableName}_hash ON {TableName}(hash);";

            await command.ExecuteNonQueryAsync(cancellationToken);
            _schemaInitialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure hash index schema");
            throw;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _initializationLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
