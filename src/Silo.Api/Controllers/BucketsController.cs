using Microsoft.AspNetCore.Mvc;
using Silo.Api.Services;

namespace Silo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BucketsController : ControllerBase
{
    private readonly IMinioStorageService _minioStorageService;
    private readonly ILogger<BucketsController> _logger;

    public BucketsController(IMinioStorageService minioStorageService, ILogger<BucketsController> logger)
    {
        _minioStorageService = minioStorageService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> ListBuckets(CancellationToken cancellationToken = default)
    {
        try
        {
            var buckets = await _minioStorageService.ListBucketsAsync(cancellationToken);
            
            return Ok(new
            {
                Success = true,
                Buckets = buckets.Select(bucket => new
                {
                    Name = bucket,
                    Type = GetBucketType(bucket),
                    Description = GetBucketDescription(bucket)
                }),
                Count = buckets.Count(),
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing buckets");
            return StatusCode(500, new
            {
                Success = false,
                Error = "An error occurred while listing buckets"
            });
        }
    }

    [HttpGet("{bucketName}/status")]
    public async Task<IActionResult> GetBucketStatus(string bucketName, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await _minioStorageService.BucketExistsAsync(bucketName, cancellationToken);
            
            if (!exists)
            {
                return NotFound(new
                {
                    Success = false,
                    BucketName = bucketName,
                    Exists = false,
                    Message = "Bucket does not exist"
                });
            }

            var files = await _minioStorageService.ListFilesAsync(bucketName, null, cancellationToken);
            var fileCount = files.Count();

            return Ok(new
            {
                Success = true,
                BucketName = bucketName,
                Exists = true,
                FileCount = fileCount,
                Type = GetBucketType(bucketName),
                Description = GetBucketDescription(bucketName),
                Files = files.Take(10), // Show first 10 files as preview
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bucket status for {BucketName}", bucketName);
            return StatusCode(500, new
            {
                Success = false,
                Error = "An error occurred while checking bucket status"
            });
        }
    }

    [HttpPost("{bucketName}")]
    public async Task<IActionResult> CreateBucket(string bucketName, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if bucket already exists
            var exists = await _minioStorageService.BucketExistsAsync(bucketName, cancellationToken);
            if (exists)
            {
                return Conflict(new
                {
                    Success = false,
                    BucketName = bucketName,
                    Message = "Bucket already exists"
                });
            }

            await _minioStorageService.CreateBucketAsync(bucketName, cancellationToken);
            
            _logger.LogInformation("Created bucket {BucketName}", bucketName);
            
            return Ok(new
            {
                Success = true,
                BucketName = bucketName,
                Message = "Bucket created successfully",
                CreatedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating bucket {BucketName}", bucketName);
            return StatusCode(500, new
            {
                Success = false,
                Error = "An error occurred while creating the bucket"
            });
        }
    }

    [HttpGet("{bucketName}/files")]
    public async Task<IActionResult> ListFiles(
        string bucketName, 
        string? prefix = null, 
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await _minioStorageService.BucketExistsAsync(bucketName, cancellationToken);
            if (!exists)
            {
                return NotFound(new
                {
                    Success = false,
                    BucketName = bucketName,
                    Message = "Bucket does not exist"
                });
            }

            var files = await _minioStorageService.ListFilesAsync(bucketName, prefix, cancellationToken);
            var limitedFiles = files.Take(limit);

            return Ok(new
            {
                Success = true,
                BucketName = bucketName,
                Prefix = prefix,
                Files = limitedFiles,
                Count = limitedFiles.Count(),
                TotalFiles = files.Count(),
                Limit = limit,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files in bucket {BucketName}", bucketName);
            return StatusCode(500, new
            {
                Success = false,
                Error = "An error occurred while listing files"
            });
        }
    }

    [HttpGet("config")]
    public IActionResult GetBucketConfiguration()
    {
        var bucketConfig = new
        {
            Success = true,
            DefaultBuckets = new
            {
                Files = new { Name = "files", Purpose = "Main file storage", AutoCreate = true },
                Thumbnails = new { Name = "thumbnails", Purpose = "Generated thumbnails", AutoCreate = true },
                Versions = new { Name = "versions", Purpose = "File version history", AutoCreate = true }
            },
            BucketNamingRules = new[]
            {
                "Bucket names must be 3-63 characters long",
                "Can contain only lowercase letters, numbers, dots, and hyphens",
                "Must start and end with a letter or number",
                "Cannot contain consecutive dots or hyphens"
            },
            MaxFileSize = "5TB per file",
            StreamingSupport = true,
            Timestamp = DateTime.UtcNow
        };

        return Ok(bucketConfig);
    }

    private static string GetBucketType(string bucketName)
    {
        return bucketName.ToLower() switch
        {
            "files" => "Primary",
            "thumbnails" => "Generated",
            "versions" => "Versioning",
            _ => "Custom"
        };
    }

    private static string GetBucketDescription(string bucketName)
    {
        return bucketName.ToLower() switch
        {
            "files" => "Main file storage bucket for uploaded documents, images, and other files",
            "thumbnails" => "Auto-generated thumbnails for images and previews",
            "versions" => "File version history and backup storage",
            _ => "Custom bucket for specialized storage needs"
        };
    }
}