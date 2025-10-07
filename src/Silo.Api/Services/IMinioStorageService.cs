namespace Silo.Api.Services;

public interface IMinioStorageService
{
    Task<string> UploadFileAsync(string bucketName, string fileName, Stream stream, string? contentType = null, CancellationToken cancellationToken = default);
    Task<Stream> DownloadFileAsync(string bucketName, string fileName, CancellationToken cancellationToken = default);
    Task<bool> DeleteFileAsync(string bucketName, string fileName, CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string bucketName, string fileName, CancellationToken cancellationToken = default);
    Task<bool> BucketExistsAsync(string bucketName, CancellationToken cancellationToken = default);
    Task CreateBucketAsync(string bucketName, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> ListBucketsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> ListFilesAsync(string bucketName, string? prefix = null, CancellationToken cancellationToken = default);
}