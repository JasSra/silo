namespace Silo.Core.Services.AI;

public interface IAIMetadataService
{
    Task<AIMetadataResult> ExtractMetadataAsync(AIMetadataRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsConfiguredAsync();
    string ProviderName { get; }
}

public record AIMetadataRequest(
    string FileName,
    string FilePath,
    string MimeType,
    long FileSize,
    Stream? FileStream = null,
    byte[]? FileContent = null)
{
    public bool HasContent => FileStream != null || FileContent != null;
}

public record AIMetadataResult(
    bool Success,
    IDictionary<string, object> ExtractedMetadata,
    string[] Tags,
    string? Category = null,
    string? Description = null,
    string? ErrorMessage = null,
    double? ConfidenceScore = null)
{
    public static AIMetadataResult Failed(string errorMessage) 
        => new(false, new Dictionary<string, object>(), Array.Empty<string>(), ErrorMessage: errorMessage);
    
    public static AIMetadataResult Succeeded(
        IDictionary<string, object> metadata,
        string[] tags,
        string? category = null,
        string? description = null,
        double? confidence = null) 
        => new(true, metadata, tags, category, description, null, confidence);
}

public enum AIProvider
{
    None,
    OpenAI,
    Ollama,
    AzureOpenAI
}

public class AIConfiguration
{
    public AIProvider Provider { get; set; } = AIProvider.None;
    public bool Enabled { get; set; } = false;
    public OpenAIConfiguration? OpenAI { get; set; }
    public OllamaConfiguration? Ollama { get; set; }
    public AzureOpenAIConfiguration? AzureOpenAI { get; set; }
    public int MaxFileSizeForAnalysis { get; set; } = 10 * 1024 * 1024; // 10MB
    public string[] SupportedMimeTypes { get; set; } = 
    {
        "text/plain", "text/csv", "text/json", "text/xml",
        "application/pdf", "application/json", "application/xml",
        "image/jpeg", "image/png", "image/gif", "image/bmp",
        "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };
}

public class OpenAIConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4-vision-preview";
    public string TextModel { get; set; } = "gpt-4";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.1;
}

public class OllamaConfiguration
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3";
    public string VisionModel { get; set; } = "llava";
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.1;
}

public class AzureOpenAIConfiguration
{
    public string Endpoint { get; set; } = string.Empty; // e.g., https://your-resource.openai.azure.com
    public string ApiKey { get; set; } = string.Empty;
    public string Deployment { get; set; } = "gpt-4o"; // for text/vision
    public string TextDeployment { get; set; } = "gpt-4o-mini";
    public string ApiVersion { get; set; } = "2024-06-01";
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.1;
}