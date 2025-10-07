using Microsoft.Extensions.Logging;
using Silo.Core.Pipeline;

namespace Silo.Api.Services.Pipeline;

public class ThumbnailGenerationStep : PipelineStepBase
{
    private readonly ThumbnailService _thumbnailService;

    public ThumbnailGenerationStep(ThumbnailService thumbnailService, ILogger<PipelineStepBase> logger) : base(logger)
    {
        _thumbnailService = thumbnailService;
    }

    public override string Name => "ThumbnailGeneration";
    public override int Order => 300;

    protected override async Task<PipelineStepResult> ExecuteInternalAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        if (context.FileStream == null || string.IsNullOrEmpty(context.FileMetadata.FileName))
        {
            return PipelineStepResult.Failed("No file stream or name provided for thumbnail generation");
        }

        var contentType = context.FileMetadata.MimeType;
        
        // Check if file is an image
        if (string.IsNullOrEmpty(contentType) || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Skipping thumbnail generation for non-image file {FileName} (ContentType: {ContentType})", 
                context.FileMetadata.FileName, contentType);
            return PipelineStepResult.Succeeded(new Dictionary<string, object> { ["Skipped"] = "File is not an image" });
        }

        try
        {
            _logger.LogInformation("Generating thumbnails for image {FileName}", context.FileMetadata.FileName);

            // Reset stream position for thumbnail generation
            if (context.FileStream.CanSeek)
            {
                context.FileStream.Position = 0;
            }

            var thumbnailResult = await _thumbnailService.GenerateThumbnailsAsync(
                context.FileStream,
                context.FileMetadata.FileName,
                cancellationToken);

            _logger.LogInformation("Successfully generated {Count} thumbnails for image {FileName}", 
                thumbnailResult.Thumbnails.Count, context.FileMetadata.FileName);

            context.SetStepResult(Name, new
            {
                ThumbnailPaths = thumbnailResult.Thumbnails,
                ThumbnailCount = thumbnailResult.Thumbnails.Count,
                ThumbnailsGeneratedAt = DateTime.UtcNow
            });

            return PipelineStepResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate thumbnails for image {FileName}", context.FileMetadata.FileName);
            return PipelineStepResult.Failed($"Thumbnail generation failed: {ex.Message}");
        }
    }
}