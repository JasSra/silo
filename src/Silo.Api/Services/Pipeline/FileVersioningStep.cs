using Microsoft.Extensions.Logging;
using Silo.Core.Pipeline;
using Silo.Core.Models;

namespace Silo.Api.Services.Pipeline;

public class FileVersioningStep : PipelineStepBase
{
    private readonly IFileVersioningService _versioningService;

    public FileVersioningStep(IFileVersioningService versioningService, ILogger<PipelineStepBase> logger) : base(logger)
    {
        _versioningService = versioningService;
    }

    public override string Name => "FileVersioning";
    public override int Order => 500;

    protected override async Task<PipelineStepResult> ExecuteInternalAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        if (context.FileStream == null || string.IsNullOrEmpty(context.FileMetadata.FileName))
        {
            return PipelineStepResult.Failed("No file stream or name provided for versioning");
        }

        try
        {
            _logger.LogInformation("Creating version for file {FileName}", context.FileMetadata.FileName);

            // Reset stream position for versioning
            if (context.FileStream.CanSeek)
            {
                context.FileStream.Position = 0;
            }

            var versionRequest = new CreateVersionRequest(
                context.FileMetadata.StoragePath,
                context.FileStream,
                "Initial version created during upload",
                VersionType.Normal,
                new Dictionary<string, string>
                {
                    ["ContentType"] = context.FileMetadata.MimeType,
                    ["UploadedAt"] = context.FileMetadata.CreatedAt.ToString("O"),
                    ["FileSize"] = context.FileMetadata.FileSize.ToString()
                });

            var versionResult = await _versioningService.CreateVersionAsync(versionRequest, null, cancellationToken);

            _logger.LogInformation("Successfully created version {VersionId} for file {FileName}", 
                versionResult.Id, context.FileMetadata.FileName);

            context.SetStepResult(Name, new
            {
                VersionId = versionResult.Id,
                VersionNumber = versionResult.VersionNumber,
                VersionedAt = versionResult.CreatedAt,
                Checksum = versionResult.Checksum
            });

            return PipelineStepResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create version for file {FileName}", context.FileMetadata.FileName);
            return PipelineStepResult.Failed($"Versioning failed: {ex.Message}");
        }
    }
}