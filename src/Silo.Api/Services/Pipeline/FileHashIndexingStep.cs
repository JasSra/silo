using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Silo.Core.Pipeline;
using Silo.Core.Services;

namespace Silo.Api.Services.Pipeline;

public class FileHashIndexingStep : PipelineStepBase
{
    private readonly IFileHashIndex _hashIndex;
    private static readonly IReadOnlyList<string> StepDependencies = new[] { "FileHashing" };

    public FileHashIndexingStep(IFileHashIndex hashIndex, ILogger<PipelineStepBase> logger)
        : base(logger)
    {
        _hashIndex = hashIndex;
        Dependencies = StepDependencies;
    }

    public override string Name => "FileHashIndexing";
    public override int Order => 120;
    public override IReadOnlyList<string> Dependencies { get; protected set; }

    protected override async Task<PipelineStepResult> ExecuteInternalAsync(
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        var hash = context.FileMetadata.Checksum;
        if (string.IsNullOrWhiteSpace(hash))
        {
            hash = context.GetProperty<string>("FileHash") ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(hash))
        {
            _logger.LogInformation("No hash available for file {FileId} for tenant {TenantId}, skipping hash indexing", 
                context.FileMetadata.Id, context.TenantId);
            return PipelineStepResult.Failed("File hash not available for indexing");
        }

        try
        {
            var existingIds = await _hashIndex.GetFileIdsAsync(hash, cancellationToken);
            var duplicateIds = existingIds.Where(id => id != context.FileMetadata.Id).ToArray();

            await _hashIndex.AddOrUpdateAsync(hash, context.FileMetadata.Id, cancellationToken);

            context.FileMetadata.Metadata["hash_indexed_at"] = DateTime.UtcNow;
            context.FileMetadata.Metadata["hash_duplicate_count"] = duplicateIds.Length;

            if (duplicateIds.Length > 0)
            {
                context.FileMetadata.Metadata["hash_duplicates"] = duplicateIds.Select(id => id.ToString()).ToArray();
                context.FileMetadata.Metadata["is_duplicate"] = true;
            }
            else
            {
                context.FileMetadata.Metadata["is_duplicate"] = false;
            }

            context.SetProperty("DuplicateFileIds", duplicateIds);

            var resultMetadata = new Dictionary<string, object>
            {
                ["Hash"] = hash,
                ["DuplicateCount"] = duplicateIds.Length
            };

            if (duplicateIds.Length > 0)
            {
                resultMetadata["DuplicateFileIds"] = duplicateIds.Select(id => id.ToString()).ToArray();
            }

            context.SetStepResult(Name, new
            {
                Hash = hash,
                DuplicateCount = duplicateIds.Length,
                DuplicateFileIds = duplicateIds,
                TenantId = context.TenantId
            });

            return PipelineStepResult.Succeeded(resultMetadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update hash index for file {FileId}", context.FileMetadata.Id);
            return PipelineStepResult.Failed($"Hash index update failed: {ex.Message}");
        }
    }
}
