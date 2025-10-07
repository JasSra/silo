using Microsoft.Extensions.Logging;
using Silo.Core.Pipeline;
using Silo.Core.Services;

namespace Silo.Api.Services.Pipeline;

public class FileIndexingStep : PipelineStepBase
{
    private readonly ISearchService _searchService;

    public FileIndexingStep(ISearchService searchService, ILogger<PipelineStepBase> logger) : base(logger)
    {
        _searchService = searchService;
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
            _logger.LogInformation("Indexing file {FileName} for search", context.FileMetadata.FileName);

            // Update file metadata with additional properties
            context.FileMetadata.LastModified = DateTime.UtcNow;
            context.FileMetadata.Status = Silo.Core.Models.FileStatus.Indexed;

            // Enrich with AI metadata if available
            EnrichWithAIMetadata(context);

            await _searchService.IndexFileAsync(context.FileMetadata);

            _logger.LogInformation("Successfully indexed file {FileName} with ID {DocumentId}", 
                context.FileMetadata.FileName, context.FileMetadata.Id);

            context.SetStepResult(Name, new
            {
                DocumentId = context.FileMetadata.Id,
                IndexedAt = DateTime.UtcNow,
                IndexName = "files",
                HasAIMetadata = context.HasProperty("AIExtractedMetadata")
            });

            return PipelineStepResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index file {FileName}", context.FileMetadata.FileName);
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