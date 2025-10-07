using Silo.Core.Pipeline;
using System.Net.Sockets;
using System.Text;

namespace Silo.Api.Services.Pipeline;

public interface IClamAvService
{
    Task<ScanResult> ScanFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task UpdateDefinitionsAsync(CancellationToken cancellationToken = default);
}

public record ScanResult(
    bool IsClean,
    string? ThreatName = null,
    string? ScanDetails = null)
{
    public static ScanResult Clean(string? details = null) => new(true, null, details);
    public static ScanResult Infected(string threatName, string? details = null) => new(false, threatName, details);
    public static ScanResult Error(string errorDetails) => new(false, "SCAN_ERROR", errorDetails);
}

public class ClamAvService : IClamAvService
{
    private readonly ILogger<ClamAvService> _logger;
    private readonly ClamAvConfiguration _config;
    
    public ClamAvService(ILogger<ClamAvService> logger, ClamAvConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task<ScanResult> ScanFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting ClamAV scan for file: {FileName}", fileName);
            
            if (!await IsAvailableAsync(cancellationToken))
            {
                _logger.LogWarning("ClamAV service is not available");
                return ScanResult.Error("ClamAV service is not available");
            }
            
            // Read file content into memory
            fileStream.Position = 0;
            var fileContent = new byte[fileStream.Length];
            await fileStream.ReadAsync(fileContent, 0, fileContent.Length, cancellationToken);
            
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(_config.Host, _config.Port);
            
            using var networkStream = tcpClient.GetStream();
            
            // Send INSTREAM command
            var instreamCommand = Encoding.ASCII.GetBytes("zINSTREAM\0");
            await networkStream.WriteAsync(instreamCommand, 0, instreamCommand.Length, cancellationToken);
            
            // Send file size as 4 bytes in network byte order
            var fileSizeBytes = BitConverter.GetBytes((uint)fileContent.Length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(fileSizeBytes);
            await networkStream.WriteAsync(fileSizeBytes, 0, 4, cancellationToken);
            
            // Send file content
            await networkStream.WriteAsync(fileContent, 0, fileContent.Length, cancellationToken);
            
            // Send end marker (4 zero bytes)
            var endMarker = new byte[4];
            await networkStream.WriteAsync(endMarker, 0, 4, cancellationToken);
            
            // Read response
            var buffer = new byte[1024];
            var bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            var response = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim('\0');
            
            _logger.LogInformation("ClamAV scan completed for {FileName}: {Result}", fileName, response);
            
            // Parse ClamAV response: "stream: OK" or "stream: Win.Trojan.Something FOUND"
            if (response.Contains(": OK"))
            {
                return ScanResult.Clean($"File {fileName} is clean");
            }
            
            if (response.Contains(" FOUND"))
            {
                var parts = response.Split(' ');
                if (parts.Length >= 2)
                {
                    var threatName = parts[1];
                    return ScanResult.Infected(threatName, $"File {fileName} contains threat: {threatName}");
                }
            }
            
            return ScanResult.Error($"Unexpected scan result format: {response}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning file {FileName} with ClamAV", fileName);
            return ScanResult.Error($"Scan error: {ex.Message}");
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(_config.Host, _config.Port);
            
            using var networkStream = tcpClient.GetStream();
            
            // Send PING command
            var pingCommand = Encoding.ASCII.GetBytes("zPING\0");
            await networkStream.WriteAsync(pingCommand, 0, pingCommand.Length, cancellationToken);
            
            // Read response
            var buffer = new byte[256];
            var bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            var response = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim('\0');
            
            return response.Contains("PONG");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ClamAV health check failed");
            return false;
        }
    }

    public async Task UpdateDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Updating ClamAV virus definitions");
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(_config.Host, _config.Port);
            
            using var networkStream = tcpClient.GetStream();
            
            // Send RELOAD command
            var reloadCommand = Encoding.ASCII.GetBytes("zRELOAD\0");
            await networkStream.WriteAsync(reloadCommand, 0, reloadCommand.Length, cancellationToken);
            
            // Read response
            var buffer = new byte[256];
            var bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            var response = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim('\0');
            
            if (response.Contains("RELOADING"))
            {
                _logger.LogInformation("ClamAV definitions reload initiated successfully");
            }
            else
            {
                _logger.LogWarning("Failed to initiate ClamAV definitions reload: {Response}", response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating ClamAV definitions");
        }
    }
}

public class ClamAvConfiguration
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3310;
    public bool QuarantineInfectedFiles { get; set; } = true;
    public string QuarantinePath { get; set; } = "/data/quarantine";
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromMinutes(5);
}