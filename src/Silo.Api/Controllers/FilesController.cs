using Microsoft.AspNetCore.Mvc;
using Silo.Core.Models;
using Silo.Core.Services;

namespace Silo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class FilesController : ControllerBase
{
    private readonly IStorageService _storageService;
    private readonly ISearchService _searchService;
    private readonly IScanService _scanService;
    private readonly IFileProcessingService _fileProcessingService;
    private readonly ILogger<FilesController> _logger;

    public FilesController(
        IStorageService storageService,
        ISearchService searchService,
        IScanService scanService,
        IFileProcessingService fileProcessingService,
        ILogger<FilesController> logger)
    {
        _storageService = storageService;
        _searchService = searchService;
        _scanService = scanService;
        _fileProcessingService = fileProcessingService;
        _logger = logger;
    }

    /// <summary>
    /// Upload a new file to the system
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(1_000_000_000)] // 1GB limit
    public async Task<ActionResult<FileMetadata>> UploadFile(IFormFile file)
    {
        try
        {
            if (file.Length == 0)
                return BadRequest("File is empty");

            var fileId = Guid.NewGuid();
            var fileName = file.FileName;
            var contentType = file.ContentType;

            // Calculate checksum
            string checksum;
            using (var stream = file.OpenReadStream())
            {
                checksum = await _fileProcessingService.CalculateChecksumAsync(stream);
            }

            // Upload to storage
            string storagePath;
            using (var stream = file.OpenReadStream())
            {
                storagePath = await _storageService.UploadFileAsync(stream, $"{fileId}/{fileName}", contentType);
            }

            // Create metadata
            var metadata = new FileMetadata
            {
                Id = fileId,
                FileName = fileName,
                OriginalPath = fileName,
                StoragePath = storagePath,
                FileSize = file.Length,
                MimeType = contentType ?? "application/octet-stream",
                Checksum = checksum,
                Status = FileStatus.Pending
            };

            // Queue for processing
            await _fileProcessingService.ProcessFileAsync(fileId);

            _logger.LogInformation("File {FileName} uploaded with ID {FileId}", fileName, fileId);

            return CreatedAtAction(nameof(GetFile), new { id = fileId }, metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {FileName}", file.FileName);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Get file metadata by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FileMetadata>> GetFile(Guid id)
    {
        try
        {
            var metadata = await _fileProcessingService.GetFileMetadataAsync(id);
            if (metadata == null)
                return NotFound();

            return Ok(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving file {FileId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Download a file by ID
    /// </summary>
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> DownloadFile(Guid id)
    {
        try
        {
            var metadata = await _fileProcessingService.GetFileMetadataAsync(id);
            if (metadata == null)
                return NotFound();

            if (metadata.Status == FileStatus.Quarantined)
                return BadRequest("File is quarantined");

            var fileStream = await _storageService.DownloadFileAsync(metadata.StoragePath);
            
            return File(fileStream, metadata.MimeType, metadata.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FileId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Search files by query
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<FileMetadata>>> SearchFiles(
        [FromQuery] string query,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20)
    {
        try
        {
            var results = await _searchService.SearchAsync(query, skip, take);
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching files with query {Query}", query);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Search files by tags
    /// </summary>
    [HttpGet("search/tags")]
    public async Task<ActionResult<IEnumerable<FileMetadata>>> SearchFilesByTags(
        [FromQuery] string[] tags,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20)
    {
        try
        {
            var results = await _searchService.SearchByTagsAsync(tags, skip, take);
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching files by tags {Tags}", string.Join(", ", tags));
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Delete a file
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteFile(Guid id)
    {
        try
        {
            var metadata = await _fileProcessingService.GetFileMetadataAsync(id);
            if (metadata == null)
                return NotFound();

            // Mark as deleted
            await _fileProcessingService.UpdateFileStatusAsync(id, FileStatus.Archived);

            // Remove from storage
            await _storageService.DeleteFileAsync(metadata.StoragePath);

            // Remove from search index
            await _searchService.RemoveFromIndexAsync(id);

            _logger.LogInformation("File {FileId} deleted", id);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileId}", id);
            return StatusCode(500, "Internal server error");
        }
    }
}