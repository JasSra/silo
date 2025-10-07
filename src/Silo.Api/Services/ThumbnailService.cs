using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using SixLabors.ImageSharp.Formats;
using Silo.Core.Pipeline;

namespace Silo.Api.Services.Pipeline;

public interface IThumbnailService
{
    Task<ThumbnailResult> GenerateThumbnailsAsync(Stream imageStream, string fileName, CancellationToken cancellationToken = default);
    Task<bool> IsImageFileAsync(string mimeType, CancellationToken cancellationToken = default);
    Task<Stream> GetThumbnailAsync(string originalPath, ThumbnailSize size, CancellationToken cancellationToken = default);
    Task DeleteThumbnailsAsync(string originalPath, CancellationToken cancellationToken = default);
}

public record ThumbnailResult(
    bool Success,
    Dictionary<ThumbnailSize, ThumbnailInfo> Thumbnails,
    string? ErrorMessage = null);

public record ThumbnailInfo(
    string StoragePath,
    int Width,
    int Height,
    long Size,
    string Format);

public enum ThumbnailSize
{
    Small,   // 150x150
    Medium,  // 300x300
    Large,   // 600x600
    Preview  // 1200x800 (for previews)
}

public class ThumbnailService : IThumbnailService
{
    private readonly ILogger<ThumbnailService> _logger;
    private readonly IMinioStorageService _storageService;
    private readonly ThumbnailConfiguration _config;

    private static readonly Dictionary<ThumbnailSize, (int width, int height)> ThumbnailSizes = new()
    {
        [ThumbnailSize.Small] = (150, 150),
        [ThumbnailSize.Medium] = (300, 300),
        [ThumbnailSize.Large] = (600, 600),
        [ThumbnailSize.Preview] = (1200, 800)
    };

    private static readonly HashSet<string> SupportedImageTypes = new()
    {
        "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp", "image/bmp", "image/tiff"
    };

    public ThumbnailService(
        ILogger<ThumbnailService> logger,
        IMinioStorageService storageService,
        ThumbnailConfiguration config)
    {
        _logger = logger;
        _storageService = storageService;
        _config = config;
    }

    public async Task<ThumbnailResult> GenerateThumbnailsAsync(
        Stream imageStream, 
        string fileName, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating thumbnails for image: {FileName}", fileName);
            
            var thumbnails = new Dictionary<ThumbnailSize, ThumbnailInfo>();
            imageStream.Position = 0;

            using var image = await Image.LoadAsync(imageStream, cancellationToken);
            var originalWidth = image.Width;
            var originalHeight = image.Height;

            _logger.LogDebug("Original image dimensions: {Width}x{Height}", originalWidth, originalHeight);

            foreach (var size in _config.GenerateSizes)
            {
                try
                {
                    var (targetWidth, targetHeight) = ThumbnailSizes[size];
                    var thumbnailInfo = await GenerateSingleThumbnailAsync(
                        image, fileName, size, targetWidth, targetHeight, cancellationToken);
                    
                    if (thumbnailInfo != null)
                    {
                        thumbnails[size] = thumbnailInfo;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate {Size} thumbnail for {FileName}", size, fileName);
                }
            }

            var success = thumbnails.Any();
            var errorMessage = success ? null : "No thumbnails were generated successfully";

            _logger.LogInformation("Generated {Count} thumbnails for image: {FileName}", thumbnails.Count, fileName);
            return new ThumbnailResult(success, thumbnails, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating thumbnails for image: {FileName}", fileName);
            return new ThumbnailResult(false, new Dictionary<ThumbnailSize, ThumbnailInfo>(), ex.Message);
        }
    }

    private async Task<ThumbnailInfo?> GenerateSingleThumbnailAsync(
        Image sourceImage,
        string fileName,
        ThumbnailSize size,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken)
    {
        try
        {
            // Calculate dimensions maintaining aspect ratio
            var (newWidth, newHeight) = CalculateThumbnailDimensions(
                sourceImage.Width, sourceImage.Height, targetWidth, targetHeight);

            using var thumbnail = sourceImage.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(newWidth, newHeight),
                Mode = _config.ResizeMode,
                Sampler = _config.ResampleAlgorithm
            }));

            // Generate storage path
            var storagePath = GenerateThumbnailPath(fileName, size);
            
            // Save thumbnail
            using var thumbnailStream = new MemoryStream();
            var encoder = GetEncoder(_config.OutputFormat, _config.Quality);
            await thumbnail.SaveAsync(thumbnailStream, encoder, cancellationToken);
            
            thumbnailStream.Position = 0;
            
            // Upload to storage
            await _storageService.UploadFileAsync(
                _config.ThumbnailBucket,
                storagePath,
                thumbnailStream,
                GetMimeType(_config.OutputFormat),
                cancellationToken);

            _logger.LogDebug("Generated {Size} thumbnail for {FileName}: {Width}x{Height}, {Size}KB",
                size, fileName, newWidth, newHeight, thumbnailStream.Length / 1024);

            return new ThumbnailInfo(
                storagePath,
                newWidth,
                newHeight,
                thumbnailStream.Length,
                _config.OutputFormat.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate {Size} thumbnail for {FileName}", size, fileName);
            return null;
        }
    }

    public Task<bool> IsImageFileAsync(string mimeType, CancellationToken cancellationToken = default)
    {
        var isSupported = SupportedImageTypes.Contains(mimeType.ToLowerInvariant());
        return Task.FromResult(isSupported);
    }

    public async Task<Stream> GetThumbnailAsync(
        string originalPath, 
        ThumbnailSize size, 
        CancellationToken cancellationToken = default)
    {
        var thumbnailPath = GenerateThumbnailPath(originalPath, size);
        
        try
        {
            return await _storageService.DownloadFileAsync(_config.ThumbnailBucket, thumbnailPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve {Size} thumbnail for {OriginalPath}", size, originalPath);
            throw new FileNotFoundException($"Thumbnail not found: {thumbnailPath}");
        }
    }

    public async Task DeleteThumbnailsAsync(string originalPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting thumbnails for: {OriginalPath}", originalPath);

        var deleteTasks = Enum.GetValues<ThumbnailSize>()
            .Select(size => DeleteSingleThumbnailAsync(originalPath, size, cancellationToken))
            .ToArray();

        try
        {
            await Task.WhenAll(deleteTasks);
            _logger.LogInformation("Successfully deleted thumbnails for: {OriginalPath}", originalPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting some thumbnails for: {OriginalPath}", originalPath);
        }
    }

    private async Task DeleteSingleThumbnailAsync(
        string originalPath, 
        ThumbnailSize size, 
        CancellationToken cancellationToken)
    {
        try
        {
            var thumbnailPath = GenerateThumbnailPath(originalPath, size);
            await _storageService.DeleteFileAsync(_config.ThumbnailBucket, thumbnailPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete {Size} thumbnail for {OriginalPath}", size, originalPath);
        }
    }

    private (int width, int height) CalculateThumbnailDimensions(
        int originalWidth, 
        int originalHeight, 
        int targetWidth, 
        int targetHeight)
    {
        if (_config.ResizeMode == ResizeMode.Stretch)
        {
            return (targetWidth, targetHeight);
        }

        var widthRatio = (double)targetWidth / originalWidth;
        var heightRatio = (double)targetHeight / originalHeight;
        var ratio = Math.Min(widthRatio, heightRatio);

        var newWidth = (int)(originalWidth * ratio);
        var newHeight = (int)(originalHeight * ratio);

        return (newWidth, newHeight);
    }

    private string GenerateThumbnailPath(string originalPath, ThumbnailSize size)
    {
        var directory = Path.GetDirectoryName(originalPath)?.Replace('\\', '/') ?? "";
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
        var extension = GetFileExtension(_config.OutputFormat);
        
        return $"thumbnails/{directory}/{fileNameWithoutExt}_{size.ToString().ToLower()}{extension}";
    }

    private IImageEncoder GetEncoder(ThumbnailFormat format, int quality)
    {
        return format switch
        {
            ThumbnailFormat.Jpeg => new JpegEncoder { Quality = quality },
            ThumbnailFormat.Png => new PngEncoder(),
            ThumbnailFormat.Webp => new WebpEncoder { Quality = quality },
            _ => new JpegEncoder { Quality = quality }
        };
    }

    private string GetMimeType(ThumbnailFormat format)
    {
        return format switch
        {
            ThumbnailFormat.Jpeg => "image/jpeg",
            ThumbnailFormat.Png => "image/png",
            ThumbnailFormat.Webp => "image/webp",
            _ => "image/jpeg"
        };
    }

    private string GetFileExtension(ThumbnailFormat format)
    {
        return format switch
        {
            ThumbnailFormat.Jpeg => ".jpg",
            ThumbnailFormat.Png => ".png",
            ThumbnailFormat.Webp => ".webp",
            _ => ".jpg"
        };
    }
}

public enum ThumbnailFormat
{
    Jpeg,
    Png,
    Webp
}

public class ThumbnailConfiguration
{
    public bool EnableThumbnailGeneration { get; set; } = true;
    public ThumbnailSize[] GenerateSizes { get; set; } = { ThumbnailSize.Small, ThumbnailSize.Medium, ThumbnailSize.Large };
    public ThumbnailFormat OutputFormat { get; set; } = ThumbnailFormat.Jpeg;
    public int Quality { get; set; } = 85; // For JPEG and WebP
    public ResizeMode ResizeMode { get; set; } = ResizeMode.Max;
    public IResampler ResampleAlgorithm { get; set; } = KnownResamplers.Lanczos3;
    public string ThumbnailBucket { get; set; } = "thumbnails";
    public bool DeleteThumbnailsOnFileDelete { get; set; } = true;
    public int MaxImageSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB
}