using Minio;
using Minio.DataModel.Args;
using Silo.Core.Services;
using System.Security.Cryptography;
using System.Text;

namespace Silo.Api.Services;

public class MinioStorageService : IStorageService
{
    private readonly IMinioClient _minioClient;
    private readonly string _bucketName;
    private readonly ILogger<MinioStorageService> _logger;

    public MinioStorageService(IMinioClient minioClient, IConfiguration configuration, ILogger<MinioStorageService> logger)
    {
        _minioClient = minioClient;
        _bucketName = configuration.GetValue<string>("MinIO:BucketName") ?? "files";
        _logger = logger;
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string? contentType = null)
    {
        try
        {
            // Ensure bucket exists
            var bucketExists = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucketName));
            if (!bucketExists)
            {
                await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucketName));
            }

            var objectName = $"{DateTime.UtcNow:yyyy/MM/dd}/{fileName}";
            
            await _minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectName)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType(contentType ?? "application/octet-stream"));

            _logger.LogInformation("File {FileName} uploaded to {ObjectName}", fileName, objectName);
            return objectName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {FileName}", fileName);
            throw;
        }
    }

    public async Task<Stream> DownloadFileAsync(string filePath)
    {
        try
        {
            var stream = new MemoryStream();
            
            await _minioClient.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(filePath)
                .WithCallbackStream(s => s.CopyTo(stream)));

            stream.Position = 0;
            return stream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FilePath}", filePath);
            throw;
        }
    }

    public async Task<bool> DeleteFileAsync(string filePath)
    {
        try
        {
            await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(filePath));

            _logger.LogInformation("File {FilePath} deleted", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FilePath}", filePath);
            return false;
        }
    }

    public async Task<bool> FileExistsAsync(string filePath)
    {
        try
        {
            await _minioClient.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(filePath));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<long> GetFileSizeAsync(string filePath)
    {
        try
        {
            var stat = await _minioClient.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(filePath));
            return stat.Size;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file size for {FilePath}", filePath);
            throw;
        }
    }

    public async Task<string> GetFileUrlAsync(string filePath, TimeSpan? expiry = null)
    {
        try
        {
            var expiryTime = expiry ?? TimeSpan.FromHours(1);
            var url = await _minioClient.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(filePath)
                .WithExpiry((int)expiryTime.TotalSeconds));

            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating URL for file {FilePath}", filePath);
            throw;
        }
    }

    public async Task<IEnumerable<string>> ListFilesAsync(string? prefix = null)
    {
        try
        {
            var files = new List<string>();
            
            await foreach (var item in _minioClient.ListObjectsAsync(new ListObjectsArgs()
                .WithBucket(_bucketName)
                .WithPrefix(prefix)))
            {
                files.Add(item.Key);
            }

            return files;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files with prefix {Prefix}", prefix);
            throw;
        }
    }

    public async Task<string> MoveFileAsync(string sourcePath, string destinationPath)
    {
        try
        {
            // Copy to new location
            await _minioClient.CopyObjectAsync(new CopyObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(destinationPath)
                .WithCopyObjectSource(new CopySourceObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(sourcePath)));

            // Delete original
            await DeleteFileAsync(sourcePath);

            _logger.LogInformation("File moved from {SourcePath} to {DestinationPath}", sourcePath, destinationPath);
            return destinationPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving file from {SourcePath} to {DestinationPath}", sourcePath, destinationPath);
            throw;
        }
    }

    public async Task<string> CopyFileAsync(string sourcePath, string destinationPath)
    {
        try
        {
            await _minioClient.CopyObjectAsync(new CopyObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(destinationPath)
                .WithCopyObjectSource(new CopySourceObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(sourcePath)));

            _logger.LogInformation("File copied from {SourcePath} to {DestinationPath}", sourcePath, destinationPath);
            return destinationPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying file from {SourcePath} to {DestinationPath}", sourcePath, destinationPath);
            throw;
        }
    }
}