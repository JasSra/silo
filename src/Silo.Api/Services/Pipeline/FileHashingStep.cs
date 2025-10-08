using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Silo.Core.Pipeline;

namespace Silo.Api.Services.Pipeline;

public class FileHashingStep : PipelineStepBase
{
    private const string HashAlgorithmName = "SHA256";

    public FileHashingStep(ILogger<PipelineStepBase> logger) : base(logger)
    {
    }

    public override string Name => "FileHashing";
    public override int Order => 50;

    protected override async Task<PipelineStepResult> ExecuteInternalAsync(
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        if (context.FileStream == null)
        {
            return PipelineStepResult.Failed("No file stream provided for hashing");
        }

        try
        {
            if (context.FileStream.CanSeek)
            {
                context.FileStream.Position = 0;
            }

            var hash = await ComputeHashAsync(context.FileStream, cancellationToken);

            context.FileMetadata.Checksum = hash;
            context.FileMetadata.Metadata["hash_algorithm"] = HashAlgorithmName;
            context.FileMetadata.Metadata[$"hash_{HashAlgorithmName.ToLowerInvariant()}"] = hash;

            context.SetProperty("FileHash", hash);
            context.SetProperty("FileHashAlgorithm", HashAlgorithmName);

            // Reset stream position for downstream steps
            if (context.FileStream.CanSeek)
            {
                context.FileStream.Position = 0;
            }

            var metadata = new Dictionary<string, object>
            {
                ["Checksum"] = hash,
                ["Algorithm"] = HashAlgorithmName
            };

            context.SetStepResult(Name, new
            {
                Hash = hash,
                Algorithm = HashAlgorithmName
            });

            return PipelineStepResult.Succeeded(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute hash for file {FileName}", context.FileMetadata.FileName);
            return PipelineStepResult.Failed($"Hashing failed: {ex.Message}");
        }
    }

    private static async Task<string> ComputeHashAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes);
    }
}
