using Microsoft.Extensions.Logging;
using Silo.Core.Pipeline;
using Silo.Core.Services;

namespace Silo.Api.Services.Pipeline;

public class FileStorageStep : PipelineStepBase
{
    private readonly IStorageService _storageService;

    public FileStorageStep(IStorageService storageService, ILogger<PipelineStepBase> logger) : base(logger)
    {
        _storageService = storageService;
    }

    public override string Name => "FileStorage";
    public override int Order => 200;

    protected override async Task<PipelineStepResult> ExecuteInternalAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        if (context.FileStream == null)
        {
            return PipelineStepResult.Failed("No file stream provided");
        }

        try
        {
            _logger.LogInformation("Storing file {FileName} in primary storage", context.FileMetadata.FileName);

            var fileName = context.FileMetadata.StoragePath;
            var contentType = context.FileMetadata.MimeType;

            // Reset stream position for storage
            if (context.FileStream.CanSeek)
            {
                context.FileStream.Position = 0;
            }

            await _storageService.UploadFileAsync(context.FileStream, fileName, contentType);

            _logger.LogInformation("Successfully stored file {FileName}", context.FileMetadata.FileName);

            context.SetStepResult(Name, new
            {
                StoragePath = fileName,
                StoredAt = DateTime.UtcNow,
                ContentType = contentType
            });

            context.FileMetadata.Status = Silo.Core.Models.FileStatus.Indexed;
            context.FileMetadata.ProcessedAt = DateTime.UtcNow;

            return PipelineStepResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store file {FileName}", context.FileMetadata.FileName);
            return PipelineStepResult.Failed($"Storage failed: {ex.Message}");
        }
    }
}