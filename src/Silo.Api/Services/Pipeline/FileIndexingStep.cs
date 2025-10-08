using Microsoft.Extensions.Logging;
using Silo.Core.Pipeline;
using Silo.Core.Services;

namespace Silo.Api.Services.Pipeline;

public class FileIndexingStep : PipelineStepBase
{
    private readonly Silo.Api.Services.TenantOpenSearchIndexingService _tenantSearchService;

    public FileIndexingStep(Silo.Api.Services.TenantOpenSearchIndexingService tenantSearchService, ILogger<PipelineStepBase> logger) : base(logger)
    {
        _tenantSearchService = tenantSearchService;
    }

    public override string Name => "FileIndexing";
    public override int Order => 400;

    protected override async Task<PipelineStepResult> ExecuteInternalAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(context.FileMetadata.FileName))
        {
            return PipelineStepResult.Failed("No file name provided for indexing");
        }

        try
        {
            _logger.LogInformation("Indexing file {FileName} for tenant {TenantId} in search", 
                context.FileMetadata.FileName, context.TenantId);

            // Update file metadata with additional properties
            context.FileMetadata.LastModified = DateTime.UtcNow;
            context.FileMetadata.Status = Silo.Core.Models.FileStatus.Indexed;

            // Enrich with AI metadata if available
            EnrichWithAIMetadata(context);

            await _tenantSearchService.IndexFileAsync(context.TenantId, context.FileMetadata, cancellationToken);

            _logger.LogInformation("Successfully indexed file {FileName} with ID {DocumentId} for tenant {TenantId}", 
                context.FileMetadata.FileName, context.FileMetadata.Id, context.TenantId);

            context.SetStepResult(Name, new
            {
                DocumentId = context.FileMetadata.Id,
                IndexedAt = DateTime.UtcNow,
                IndexName = $"tenant-{context.TenantId}-files",
                TenantId = context.TenantId,
                HasAIMetadata = context.HasProperty("AIExtractedMetadata")
            });

            return PipelineStepResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index file {FileName} for tenant {TenantId}", 
                context.FileMetadata.FileName, context.TenantId);
            return PipelineStepResult.Failed($"Failed to index file: {ex.Message}");
        }
    }

    private void EnrichWithAIMetadata(PipelineContext context)
    {
        try
        {
            if (context.HasProperty("AIExtractedMetadata"))
            {
                var aiMetadata = context.GetProperty<Dictionary<string, object>>("AIExtractedMetadata");
                if (aiMetadata != null)
                {
                    foreach (var kvp in aiMetadata)
                    {
                        context.FileMetadata.Metadata[$"ai_{kvp.Key}"] = kvp.Value;
                    }
                }
            }

            if (context.HasProperty("AITags"))
            {
                var tags = context.GetProperty<string[]>("AITags");
                if (tags != null && tags.Length > 0)
                {
                    context.FileMetadata.Metadata["ai_tags"] = tags;
                }
            }

            if (context.HasProperty("AICategory"))
            {
                var category = context.GetProperty<string>("AICategory");
                if (!string.IsNullOrEmpty(category))
                {
                    context.FileMetadata.Metadata["ai_category"] = category;
                }
            }

            if (context.HasProperty("AIDescription"))
            {
                var description = context.GetProperty<string>("AIDescription");
                if (!string.IsNullOrEmpty(description))
                {
                    context.FileMetadata.Metadata["ai_description"] = description;
                }
            }

            if (context.HasProperty("AIConfidence"))
            {
                var confidenceObj = context.GetProperty<object>("AIConfidence");
                if (confidenceObj is double confidence)
                {
                    context.FileMetadata.Metadata["ai_confidence"] = confidence;
                }
            }

            _logger.LogDebug("Enriched file metadata with AI-extracted information for {FileName}", 
                context.FileMetadata.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich metadata with AI information for {FileName}", 
                context.FileMetadata.FileName);
        }
    }
}