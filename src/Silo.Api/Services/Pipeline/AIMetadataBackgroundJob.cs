using Hangfire;
using Microsoft.Extensions.Logging;
using Silo.Core.Services;
using Silo.Core.Services.AI;

namespace Silo.Api.Services.Pipeline;

public class AIMetadataBackgroundJob
{
    private readonly IAIMetadataServiceFactory _aiServiceFactory;
    private readonly IStorageService _storageService;
    private readonly ISearchService _searchService;
    private readonly ILogger<AIMetadataBackgroundJob> _logger;

    public AIMetadataBackgroundJob(
        IAIMetadataServiceFactory aiServiceFactory,
        IStorageService storageService,
        ISearchService searchService,
        ILogger<AIMetadataBackgroundJob> logger)
    {
        _aiServiceFactory = aiServiceFactory;
        _storageService = storageService;
        _searchService = searchService;
        _logger = logger;
    }

    [Queue("ai-processing")]
    public async Task ProcessFileMetadataAsync(
        Guid fileId,
        string storagePath,
        string fileName,
        string mimeType,
        long fileSize,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting background AI metadata extraction for file {FileId} - {FileName}", fileId, fileName);

        try
        {
            var aiService = await _aiServiceFactory.GetServiceAsync();
            if (aiService == null)
            {
                _logger.LogWarning("AI service not available for background processing of file {FileId}", fileId);
                return;
            }

            // Get file content from storage
            byte[]? fileContent = null;
            try
            {
                        using var fileStream = await _storageService.DownloadFileAsync(storagePath);
                if (fileStream != null)
                {
                    using var ms = new MemoryStream();
                    await fileStream.CopyToAsync(ms, cancellationToken);
                    fileContent = ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve file content from storage for AI processing: {StoragePath}", storagePath);
                return;
            }

            var request = new AIMetadataRequest(
                fileName,
                storagePath,
                mimeType,
                fileSize,
                FileContent: fileContent);

            var result = await aiService.ExtractMetadataAsync(request, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("Successfully extracted AI metadata for file {FileId} with {TagCount} tags", 
                    fileId, result.Tags.Length);

                // Update the search index with AI-extracted metadata
                await UpdateSearchIndexWithAIMetadata(fileId, result, cancellationToken);

                // Schedule follow-up jobs if needed
                await ScheduleFollowUpJobs(fileId, result, cancellationToken);
            }
            else
            {
                _logger.LogWarning("AI metadata extraction failed for file {FileId}: {Error}", fileId, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during background AI metadata processing for file {FileId}", fileId);
            throw; // Re-throw to let Hangfire handle retries
        }
    }

    private async Task UpdateSearchIndexWithAIMetadata(Guid fileId, AIMetadataResult result, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Updating search index with AI metadata for file {FileId}", fileId);

            // Get the existing file metadata
            var fileMetadata = await _searchService.GetByIdAsync(fileId);
            if (fileMetadata == null)
            {
                _logger.LogWarning("File metadata not found in search index for file {FileId}", fileId);
                return;
            }

            // Add AI metadata to the file metadata
            fileMetadata.Metadata["ai_processed"] = true;
            fileMetadata.Metadata["ai_provider"] = result.ExtractedMetadata.TryGetValue("provider", out var provider) ? provider : "unknown";
            fileMetadata.Metadata["ai_confidence"] = result.ConfidenceScore ?? 0.0;
            fileMetadata.Metadata["ai_category"] = result.Category ?? string.Empty;
            fileMetadata.Metadata["ai_description"] = result.Description ?? string.Empty;
            fileMetadata.Metadata["ai_tags"] = result.Tags;
            fileMetadata.Metadata["ai_processed_at"] = DateTime.UtcNow;

            // Add all extracted metadata with ai_ prefix
            foreach (var kvp in result.ExtractedMetadata)
            {
                fileMetadata.Metadata[$"ai_{kvp.Key}"] = kvp.Value;
            }

            // Re-index the file with updated metadata
            await _searchService.IndexFileAsync(fileMetadata);

            _logger.LogInformation("Successfully updated search index with AI metadata for file {FileId}", fileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update search index with AI metadata for file {FileId}", fileId);
            // Don't re-throw - we don't want to fail the entire job for indexing issues
        }
    }

    private async Task ScheduleFollowUpJobs(Guid fileId, AIMetadataResult result, CancellationToken cancellationToken)
    {
        try
        {
            // Example: If confidence is low, we might want to try with a different model
            if (result.ConfidenceScore < 0.5)
            {
                _logger.LogInformation("Low confidence AI result for file {FileId}, considering alternative processing", fileId);
                // Could schedule a job with different AI settings or human review
            }

            // Example: If certain categories are detected, trigger specialized processing
            if (result.Category == "document" && result.ExtractedMetadata.ContainsKey("pages"))
            {
                _logger.LogInformation("Document with pages detected for file {FileId}, considering OCR processing", fileId);
                // Could schedule OCR or document parsing jobs
            }

            await Task.CompletedTask; // Placeholder for actual implementation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scheduling follow-up jobs for file {FileId}", fileId);
            // Don't re-throw - follow-up jobs are optional
        }
    }

    [Queue("ai-processing")]
    public async Task ReprocessFileMetadataAsync(
        Guid fileId,
        string storagePath,
        string fileName,
        string mimeType,
        long fileSize,
        string reason,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Reprocessing AI metadata for file {FileId} - {FileName}, reason: {Reason}", 
            fileId, fileName, reason);

        // Same logic as ProcessFileMetadataAsync but with different logging and possibly different settings
        await ProcessFileMetadataAsync(fileId, storagePath, fileName, mimeType, fileSize, cancellationToken);
    }
}