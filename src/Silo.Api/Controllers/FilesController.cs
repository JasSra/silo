using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Silo.Core.Pipeline;
using Silo.Core.Services;
using Silo.Core.Models;
using Silo.Api.Services;
using Silo.Api.Models;

namespace Silo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Require authentication for all endpoints
public class FilesController : ControllerBase
{
    private readonly PipelineOrchestrator _pipelineOrchestrator;
    private readonly IStorageService _storageService;
    private readonly ISearchService _searchService;
    private readonly IOpenSearchIndexingService _openSearchService;
    private readonly ITenantContextProvider _tenantContextProvider;
    private readonly ILogger<FilesController> _logger;

    public FilesController(
        PipelineOrchestrator pipelineOrchestrator,
        IStorageService storageService,
        ISearchService searchService,
        IOpenSearchIndexingService openSearchService,
        ITenantContextProvider tenantContextProvider,
        ILogger<FilesController> logger)
    {
        _pipelineOrchestrator = pipelineOrchestrator;
        _storageService = storageService;
        _searchService = searchService;
        _openSearchService = openSearchService;
        _tenantContextProvider = tenantContextProvider;
        _logger = logger;
    }

    [HttpPost("upload")]
    [Authorize(Policy = "FilesUpload")]
    public async Task<IActionResult> UploadFile(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file provided");
        }

        try
        {
            _logger.LogInformation("Starting file upload for {FileName} ({FileSize} bytes)", file.FileName, file.Length);

            using var fileStream = file.OpenReadStream();
            
            // Create FileMetadata for the pipeline context
            var fileMetadata = new Silo.Core.Models.FileMetadata
            {
                Id = Guid.NewGuid(),
                FileName = file.FileName,
                OriginalPath = file.FileName,
                StoragePath = $"{Guid.NewGuid()}/{file.FileName}",
                FileSize = file.Length,
                MimeType = file.ContentType ?? "application/octet-stream",
                Checksum = string.Empty, // Will be calculated in the pipeline
                Status = Silo.Core.Models.FileStatus.Processing,
                CreatedAt = DateTime.UtcNow
            };

            var context = new PipelineContext
            {
                FileMetadata = fileMetadata,
                FileStream = fileStream,
                TenantId = _tenantContextProvider.GetCurrentTenantId()
            };

            context.SetProperty("ContentType", file.ContentType ?? "application/octet-stream");
            context.SetProperty("FileSize", file.Length);
            context.SetProperty("UploadedAt", DateTime.UtcNow);

            var result = await _pipelineOrchestrator.ExecuteAsync(context, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("Successfully processed file {FileName}", file.FileName);
                return Ok(new
                {
                    Success = true,
                    FileName = file.FileName,
                    FileId = fileMetadata.Id,
                    ProcessedAt = DateTime.UtcNow,
                    StepsExecuted = result.StepResults.Count,
                    StepResults = result.StepResults.Select(sr => new
                    {
                        StepName = sr.StepName,
                        Success = sr.Success,
                        Duration = sr.Duration,
                        ErrorMessage = sr.ErrorMessage
                    })
                });
            }
            else
            {
                _logger.LogWarning("File processing failed for {FileName}: {ErrorMessage}", file.FileName, result.ErrorMessage);
                return BadRequest(new
                {
                    Success = false,
                    FileName = file.FileName,
                    Error = result.ErrorMessage,
                    StepResults = result.StepResults.Select(sr => new
                    {
                        StepName = sr.StepName,
                        Success = sr.Success,
                        Duration = sr.Duration,
                        ErrorMessage = sr.ErrorMessage
                    })
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during file upload for {FileName}", file.FileName);
            return StatusCode(500, new
            {
                Success = false,
                Error = "An unexpected error occurred during file processing"
            });
        }
    }

    [HttpGet("pipeline/status")]
    public IActionResult GetPipelineStatus()
    {
        return Ok(new
        {
            PipelineEnabled = true,
            AvailableSteps = new[]
            {
                "FileHashing",
                "MalwareScanning",
                "FileHashIndexing",
                "FileStorage", 
                "ThumbnailGeneration",
                "AIMetadataExtraction",
                "FileIndexing",
                "FileVersioning"
            },
            Timestamp = DateTime.UtcNow
        });
    }

    [HttpGet("search")]
    [Authorize(Policy = "FilesRead")]
    public async Task<IActionResult> SearchFiles(string query, int page = 1, int size = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var results = await _searchService.SearchAsync(query, (page - 1) * size, size);
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching files with query: {Query}", query);
            return StatusCode(500, "An error occurred while searching files");
        }
    }

    [HttpGet("{fileName}/download")]
    [Authorize(Policy = "FilesDownload")]
    public async Task<IActionResult> DownloadFile(string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            var stream = await _storageService.DownloadFileAsync(fileName);
            if (stream == null)
            {
                return NotFound();
            }

            return File(stream, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file: {FileName}", fileName);
            return StatusCode(500, "An error occurred while downloading the file");
        }
    }

    [HttpGet("download/{fileId:guid}")]
    public async Task<IActionResult> DownloadFileById(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading file by ID: {FileId}", fileId);

            // First get the file metadata to get the storage path and original filename
            var fileMetadata = await _openSearchService.GetFileByIdAsync(fileId, cancellationToken);
            if (fileMetadata == null)
            {
                return NotFound(new { Error = "File not found", FileId = fileId });
            }

            // Download from storage using the storage path
            var stream = await _storageService.DownloadFileAsync(fileMetadata.StoragePath ?? fileMetadata.FileName);
            if (stream == null)
            {
                return NotFound(new { Error = "File data not found in storage", FileId = fileId });
            }

            // Use original filename for download
            var fileName = fileMetadata.FileName ?? $"file_{fileId}";
            var contentType = fileMetadata.MimeType ?? "application/octet-stream";

            return File(stream, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file by ID: {FileId}", fileId);
            return StatusCode(500, new { Error = "An error occurred while downloading the file", FileId = fileId });
        }
    }

    [HttpGet("download/stream/{fileId:guid}")]
    public async Task<IActionResult> DownloadFileStreamById(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Streaming download for file ID: {FileId}", fileId);

            // Get file metadata
            var fileMetadata = await _openSearchService.GetFileByIdAsync(fileId, cancellationToken);
            if (fileMetadata == null)
            {
                return NotFound(new { Error = "File not found", FileId = fileId });
            }

            // Get the stream from storage
            var stream = await _storageService.DownloadFileAsync(fileMetadata.StoragePath ?? fileMetadata.FileName);
            if (stream == null)
            {
                return NotFound(new { Error = "File data not found in storage", FileId = fileId });
            }

            var fileName = fileMetadata.FileName ?? $"file_{fileId}";
            var contentType = fileMetadata.MimeType ?? "application/octet-stream";

            // Set headers for streaming large files
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
            Response.Headers["Content-Length"] = fileMetadata.FileSize.ToString();
            
            return new FileStreamResult(stream, contentType)
            {
                FileDownloadName = fileName,
                EnableRangeProcessing = true // Enables partial content for large files
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming download for file ID: {FileId}", fileId);
            return StatusCode(500, new { Error = "An error occurred while streaming the file", FileId = fileId });
        }
    }

    [HttpGet("metadata/{fileId:guid}")]
    public async Task<IActionResult> GetFileMetadata(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Retrieving metadata for file ID: {FileId}", fileId);

            var fileMetadata = await _openSearchService.GetFileByIdAsync(fileId, cancellationToken);
            if (fileMetadata == null)
            {
                return NotFound(new { Error = "File not found", FileId = fileId });
            }

            return Ok(new
            {
                Success = true,
                Metadata = new
                {
                    Id = fileMetadata.Id,
                    FileName = fileMetadata.FileName,
                    OriginalPath = fileMetadata.OriginalPath,
                    StoragePath = fileMetadata.StoragePath,
                    FileSize = fileMetadata.FileSize,
                    MimeType = fileMetadata.MimeType,
                    Checksum = fileMetadata.Checksum,
                    Status = fileMetadata.Status.ToString(),
                    CreatedAt = fileMetadata.CreatedAt,
                    LastModified = fileMetadata.LastModified,
                    ProcessedAt = fileMetadata.ProcessedAt,
                    Tags = fileMetadata.Tags,
                    Metadata = fileMetadata.Metadata,
                    Categories = fileMetadata.Categories,
                    Description = fileMetadata.Description,
                    Version = fileMetadata.Version
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving metadata for file ID: {FileId}", fileId);
            return StatusCode(500, new { Error = "An error occurred while retrieving file metadata", FileId = fileId });
        }
    }

    [HttpPost("metadata/batch")]
    public async Task<IActionResult> GetBatchFileMetadata([FromBody] BatchMetadataRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (request?.FileIds == null || !request.FileIds.Any())
            {
                return BadRequest(new { Error = "No file IDs provided" });
            }

            if (request.FileIds.Count() > 100)
            {
                return BadRequest(new { Error = "Maximum 100 file IDs allowed per batch request" });
            }

            _logger.LogInformation("Retrieving metadata for {Count} files", request.FileIds.Count());

            var metadataList = new List<object>();
            var notFoundIds = new List<Guid>();

            foreach (var fileId in request.FileIds)
            {
                try
                {
                    var fileMetadata = await _openSearchService.GetFileByIdAsync(fileId, cancellationToken);
                    if (fileMetadata != null)
                    {
                        metadataList.Add(new
                        {
                            Id = fileMetadata.Id,
                            FileName = fileMetadata.FileName,
                            OriginalPath = fileMetadata.OriginalPath,
                            StoragePath = fileMetadata.StoragePath,
                            FileSize = fileMetadata.FileSize,
                            MimeType = fileMetadata.MimeType,
                            Checksum = fileMetadata.Checksum,
                            Status = fileMetadata.Status.ToString(),
                            CreatedAt = fileMetadata.CreatedAt,
                            LastModified = fileMetadata.LastModified,
                            ProcessedAt = fileMetadata.ProcessedAt,
                            Tags = fileMetadata.Tags,
                            Metadata = fileMetadata.Metadata,
                            Categories = fileMetadata.Categories,
                            Description = fileMetadata.Description,
                            Version = fileMetadata.Version
                        });
                    }
                    else
                    {
                        notFoundIds.Add(fileId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error retrieving metadata for file ID: {FileId}", fileId);
                    notFoundIds.Add(fileId);
                }
            }

            return Ok(new
            {
                Success = true,
                Metadata = metadataList,
                RequestedCount = request.FileIds.Count(),
                FoundCount = metadataList.Count,
                NotFoundIds = notFoundIds,
                RetrievedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch metadata retrieval");
            return StatusCode(500, new { Error = "An error occurred while retrieving batch file metadata" });
        }
    }

    [HttpGet("search/advanced")]
    public async Task<IActionResult> AdvancedSearchFiles(
        string? query = null,
        string? extensions = null,
        long? minSize = null,
        long? maxSize = null,
        string? wildcard = null,
        string? context = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Advanced search request: query={Query}, extensions={Extensions}, minSize={MinSize}, maxSize={MaxSize}, wildcard={Wildcard}, context={Context}, limit={Limit}", 
                query, extensions, minSize, maxSize, wildcard, context, limit);

            // Parse extensions parameter (comma-separated list)
            IEnumerable<string>? extensionList = null;
            if (!string.IsNullOrWhiteSpace(extensions))
            {
                extensionList = extensions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(ext => ext.Trim())
                    .Where(ext => !string.IsNullOrEmpty(ext));
            }

            var results = await _openSearchService.AdvancedSearchFilesAsync(
                query: query ?? string.Empty,
                extensions: extensionList,
                minSize: minSize,
                maxSize: maxSize,
                wildcardPattern: wildcard,
                context: context,
                limit: limit,
                cancellationToken: cancellationToken);

            return Ok(new
            {
                Success = true,
                Results = results.Select(file => new
                {
                    Id = file.Id,
                    FileName = file.FileName,
                    FilePath = file.FilePath,
                    FileSize = file.FileSize,
                    MimeType = file.MimeType,
                    CreatedAt = file.CreatedAt,
                    UpdatedAt = file.UpdatedAt
                }),
                Count = results.Count(),
                SearchCriteria = new
                {
                    Query = query,
                    Extensions = extensionList?.ToArray(),
                    MinSize = minSize,
                    MaxSize = maxSize,
                    WildcardPattern = wildcard,
                    Context = context,
                    Limit = limit
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in advanced search with parameters: query={Query}, extensions={Extensions}", query, extensions);
            return StatusCode(500, new
            {
                Success = false,
                Error = "An error occurred while performing advanced search"
            });
        }
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> GetFileStatistics(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Retrieving file statistics");

            var statistics = await _openSearchService.GetFileStatisticsAsync(cancellationToken);

            return Ok(new
            {
                Success = true,
                Statistics = statistics,
                GeneratedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving file statistics");
            return StatusCode(500, new
            {
                Success = false,
                Error = "An error occurred while retrieving file statistics"
            });
        }
    }
}
