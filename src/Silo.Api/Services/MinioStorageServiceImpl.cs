using Minio;
using Minio.DataModel.Args;

namespace Silo.Api.Services;

public class MinioStorageServiceImpl : IMinioStorageService
{
    private readonly IMinioClient _minioClient;
    private readonly ILogger<MinioStorageServiceImpl> _logger;

    public MinioStorageServiceImpl(IMinioClient minioClient, ILogger<MinioStorageServiceImpl> logger)
    {
        _minioClient = minioClient;
        _logger = logger;
    }

    public async Task<string> UploadFileAsync(string bucketName, string fileName, Stream stream, string? contentType = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure bucket exists
            await EnsureBucketExistsAsync(bucketName, cancellationToken);

            // Upload file
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(fileName)
                .WithStreamData(stream)
                .WithObjectSize(stream.Length);

            if (!string.IsNullOrEmpty(contentType))
            {
                putObjectArgs = putObjectArgs.WithContentType(contentType);
            }

            await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
            
            _logger.LogInformation("Successfully uploaded file {FileName} to bucket {BucketName}", fileName, bucketName);
            return fileName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {FileName} to bucket {BucketName}", fileName, bucketName);
            throw;
        }
    }

    public async Task<Stream> DownloadFileAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            var stream = new MemoryStream();
            var getObjectArgs = new GetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(fileName)
                .WithCallbackStream((streamData) => streamData.CopyTo(stream));

            await _minioClient.GetObjectAsync(getObjectArgs, cancellationToken);
            stream.Position = 0;
            
            _logger.LogInformation("Successfully downloaded file {FileName} from bucket {BucketName}", fileName, bucketName);
            return stream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {FileName} from bucket {BucketName}", fileName, bucketName);
            throw;
        }
    }

    public async Task<bool> DeleteFileAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            var removeObjectArgs = new RemoveObjectArgs()
                .WithBucket(bucketName)
                .WithObject(fileName);

            await _minioClient.RemoveObjectAsync(removeObjectArgs, cancellationToken);
            
            _logger.LogInformation("Successfully deleted file {FileName} from bucket {BucketName}", fileName, bucketName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file {FileName} from bucket {BucketName}", fileName, bucketName);
            return false;
        }
    }

    public async Task<bool> FileExistsAsync(string bucketName, string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            var statObjectArgs = new StatObjectArgs()
                .WithBucket(bucketName)
                .WithObject(fileName);

            await _minioClient.StatObjectAsync(statObjectArgs, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> BucketExistsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        try
        {
            var bucketExistsArgs = new BucketExistsArgs()
                .WithBucket(bucketName);

            return await _minioClient.BucketExistsAsync(bucketExistsArgs, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if bucket {BucketName} exists", bucketName);
            return false;
        }
    }

    public async Task CreateBucketAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        try
        {
            var bucketExists = await BucketExistsAsync(bucketName, cancellationToken);
            if (!bucketExists)
            {
                var makeBucketArgs = new MakeBucketArgs()
                    .WithBucket(bucketName);

                await _minioClient.MakeBucketAsync(makeBucketArgs, cancellationToken);
                _logger.LogInformation("Successfully created bucket {BucketName}", bucketName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create bucket {BucketName}", bucketName);
            throw;
        }
    }

    public async Task<IEnumerable<string>> ListBucketsAsync(CancellationToken cancellationToken = default)
    {
        var buckets = await _minioClient.ListBucketsAsync(cancellationToken);
        return buckets.Buckets.Select(b => b.Name);
    }

    public async Task<IEnumerable<string>> ListFilesAsync(string bucketName, string? prefix = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var files = new List<string>();
            
            var listObjectsArgs = new ListObjectsArgs()
                .WithBucket(bucketName);
                
            if (!string.IsNullOrEmpty(prefix))
            {
                listObjectsArgs = listObjectsArgs.WithPrefix(prefix);
            }

            await foreach (var item in _minioClient.ListObjectsEnumAsync(listObjectsArgs, cancellationToken))
            {
                files.Add(item.Key);
            }

            return files;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list files in bucket {BucketName} with prefix {Prefix}", bucketName, prefix);
            throw;
        }
    }

    private async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken)
    {
        var bucketExists = await BucketExistsAsync(bucketName, cancellationToken);
        if (!bucketExists)
        {
            await CreateBucketAsync(bucketName, cancellationToken);
        }
    }
}