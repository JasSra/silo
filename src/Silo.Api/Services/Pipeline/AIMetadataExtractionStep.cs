using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Silo.Core.Pipeline;
using Silo.Core.Services;
using Silo.Core.Services.AI;

namespace Silo.Api.Services.Pipeline;

public class AIMetadataExtractionStep : PipelineStepBase
{
    private readonly IAIMetadataServiceFactory _aiServiceFactory;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly AIConfiguration _aiConfig;

    public AIMetadataExtractionStep(
        IAIMetadataServiceFactory aiServiceFactory,
        IBackgroundJobClient backgroundJobClient,
        IOptions<AIConfiguration> aiConfig,
        ILogger<PipelineStepBase> logger) : base(logger)
    {
        _aiServiceFactory = aiServiceFactory;
        _backgroundJobClient = backgroundJobClient;
        _aiConfig = aiConfig.Value;
    }

    public override string Name => "AIMetadataExtraction";
    public override int Order => 350; // After ThumbnailGeneration (300), before FileIndexing (400)

    public override bool IsEnabled => _aiServiceFactory.IsAIEnabled;

    protected override async Task<PipelineStepResult> ExecuteInternalAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        // Skip if AI is not enabled or configured
        if (!_aiServiceFactory.IsAIEnabled)
        {
            _logger.LogInformation("AI metadata extraction is disabled, skipping step");
            return PipelineStepResult.Succeeded(new Dictionary<string, object>
            {
                ["skipped"] = true,
                ["reason"] = "AI not enabled"
            });
        }

        // Check if file type is supported
        if (!IsSupportedFileType(context.FileMetadata.MimeType))
        {
            _logger.LogInformation("File type {MimeType} not supported for AI analysis, skipping", context.FileMetadata.MimeType);
            return PipelineStepResult.Succeeded(new Dictionary<string, object>
            {
                ["skipped"] = true,
                ["reason"] = "Unsupported file type"
            });
        }

        // Check file size limit
        if (context.FileMetadata.FileSize > _aiConfig.MaxFileSizeForAnalysis)
        {
            _logger.LogInformation("File size {FileSize} exceeds AI analysis limit {MaxSize}, skipping", 
                context.FileMetadata.FileSize, _aiConfig.MaxFileSizeForAnalysis);
            return PipelineStepResult.Succeeded(new Dictionary<string, object>
            {
                ["skipped"] = true,
                ["reason"] = "File too large"
            });
        }

        try
        {
            var aiService = await _aiServiceFactory.GetServiceAsync();
            if (aiService == null)
            {
                _logger.LogWarning("AI service is not available, skipping AI metadata extraction");
                return PipelineStepResult.Succeeded(new Dictionary<string, object>
                {
                    ["skipped"] = true,
                    ["reason"] = "AI service not available"
                });
            }

            // For small files or critical processing, do it synchronously
            if (context.FileMetadata.FileSize < 1024 * 1024 || // Files smaller than 1MB
                context.HasProperty("ProcessAISynchronously"))
            {
                return await ProcessAISynchronously(aiService, context, cancellationToken);
            }
            else
            {
                // Queue for background processing
                return QueueForBackgroundProcessing(context);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AI metadata extraction step for file {FileName}", context.FileMetadata.FileName);
            
            // Don't fail the entire pipeline for AI errors
            return PipelineStepResult.Succeeded(new Dictionary<string, object>
            {
                ["skipped"] = true,
                ["reason"] = "AI processing error",
                ["error"] = ex.Message
            });
        }
    }

    private async Task<PipelineStepResult> ProcessAISynchronously(
        IAIMetadataService aiService, 
        PipelineContext context, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing AI metadata extraction synchronously for {FileName}", context.FileMetadata.FileName);

        // Get file content if available
        byte[]? fileContent = null;
        if (context.FileStream != null && context.FileStream.CanSeek)
        {
            context.FileStream.Position = 0;
            using var ms = new MemoryStream();
            await context.FileStream.CopyToAsync(ms, cancellationToken);
            fileContent = ms.ToArray();
            context.FileStream.Position = 0; // Reset for other steps
        }

        var request = new AIMetadataRequest(
            context.FileMetadata.FileName,
            context.FileMetadata.StoragePath,
            context.FileMetadata.MimeType,
            context.FileMetadata.FileSize,
            FileContent: fileContent);

        var result = await aiService.ExtractMetadataAsync(request, cancellationToken);

        if (result.Success)
        {
            // Store extracted metadata in context for the indexing step
            context.SetProperty("AIExtractedMetadata", result.ExtractedMetadata);
            context.SetProperty("AITags", result.Tags);
            context.SetProperty("AICategory", result.Category);
            context.SetProperty("AIDescription", result.Description);
            context.SetProperty("AIConfidence", result.ConfidenceScore);

            _logger.LogInformation("Successfully extracted AI metadata for {FileName} with {TagCount} tags", 
                context.FileMetadata.FileName, result.Tags.Length);

            return PipelineStepResult.Succeeded(new Dictionary<string, object>
            {
                ["processed"] = true,
                ["processing_mode"] = "synchronous",
                ["provider"] = aiService.ProviderName,
                ["tags_count"] = result.Tags.Length,
                ["category"] = result.Category ?? "unknown",
                ["confidence"] = result.ConfidenceScore ?? 0.0
            });
        }
        else
        {
            _logger.LogWarning("AI metadata extraction failed for {FileName}: {Error}", 
                context.FileMetadata.FileName, result.ErrorMessage);

            return PipelineStepResult.Succeeded(new Dictionary<string, object>
            {
                ["skipped"] = true,
                ["reason"] = "AI processing failed",
                ["error"] = result.ErrorMessage
            });
        }
    }

    private PipelineStepResult QueueForBackgroundProcessing(PipelineContext context)
    {
        _logger.LogInformation("Queueing AI metadata extraction for background processing: {FileName}", context.FileMetadata.FileName);

        // Queue the job for background processing
        var jobId = _backgroundJobClient.Enqueue<AIMetadataBackgroundJob>(
            job => job.ProcessFileMetadataAsync(
                context.FileMetadata.Id,
                context.FileMetadata.StoragePath,
                context.FileMetadata.FileName,
                context.FileMetadata.MimeType,
                context.FileMetadata.FileSize,
                CancellationToken.None));

        // Store job ID in context
        context.SetProperty("AIProcessingJobId", jobId);

        _logger.LogInformation("Queued AI metadata extraction job {JobId} for file {FileName}", jobId, context.FileMetadata.FileName);

        return PipelineStepResult.Succeeded(new Dictionary<string, object>
        {
            ["processed"] = true,
            ["processing_mode"] = "background",
            ["job_id"] = jobId,
            ["status"] = "queued"
        });
    }

    private bool IsSupportedFileType(string mimeType)
    {
        return _aiConfig.SupportedMimeTypes.Contains(mimeType, StringComparer.OrdinalIgnoreCase) ||
               _aiConfig.SupportedMimeTypes.Any(supported => 
                   mimeType.StartsWith(supported.Split('/')[0] + "/", StringComparison.OrdinalIgnoreCase));
    }
}