using Microsoft.Extensions.Logging;
using Silo.Core.Pipeline;
using Silo.Core.Services;

namespace Silo.Api.Services.Pipeline;

public class FileStorageStep : PipelineStepBase
{
    private readonly ITenantStorageService _tenantStorageService;

    public FileStorageStep(ITenantStorageService tenantStorageService, ILogger<PipelineStepBase> logger) : base(logger)
    {
        _tenantStorageService = tenantStorageService;
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
            _logger.LogInformation("Storing file {FileName} for tenant {TenantId} in primary storage", 
                context.FileMetadata.FileName, context.TenantId);

            var fileName = context.FileMetadata.StoragePath;
            var contentType = context.FileMetadata.MimeType;

            // Reset stream position for storage
            if (context.FileStream.CanSeek)
            {
                context.FileStream.Position = 0;
            }

            await _tenantStorageService.UploadFileAsync(context.TenantId, fileName, context.FileStream, contentType, cancellationToken);

            _logger.LogInformation("Successfully stored file {FileName} for tenant {TenantId}", 
                context.FileMetadata.FileName, context.TenantId);

            context.SetStepResult(Name, new
            {
                StoragePath = fileName,
                StoredAt = DateTime.UtcNow,
                ContentType = contentType,
                TenantId = context.TenantId
            });

            context.FileMetadata.Status = Silo.Core.Models.FileStatus.Indexed;
            context.FileMetadata.ProcessedAt = DateTime.UtcNow;

            return PipelineStepResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store file {FileName} for tenant {TenantId}", 
                context.FileMetadata.FileName, context.TenantId);
            return PipelineStepResult.Failed($"Storage failed: {ex.Message}");
        }
    }
}