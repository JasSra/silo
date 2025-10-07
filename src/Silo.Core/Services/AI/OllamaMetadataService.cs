using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Silo.Core.Services.AI;

public class OllamaMetadataService : IAIMetadataService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaConfiguration _config;
    private readonly ILogger<OllamaMetadataService> _logger;

    public string ProviderName => "Ollama";

    public OllamaMetadataService(
        HttpClient httpClient,
        IOptions<AIConfiguration> aiConfig,
        ILogger<OllamaMetadataService> logger)
    {
        _httpClient = httpClient;
        _config = aiConfig.Value.Ollama ?? throw new ArgumentException("Ollama configuration is required");
        _logger = logger;
    }

    public async Task<bool> IsConfiguredAsync()
    {
        try
        {
            // Check if Ollama is available by hitting the version endpoint
            var response = await _httpClient.GetAsync($"{_config.BaseUrl}/api/version");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<AIMetadataResult> ExtractMetadataAsync(AIMetadataRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Extracting metadata for file: {FileName} using Ollama", request.FileName);

            var isImageFile = IsImageFile(request.MimeType);
            var model = isImageFile ? _config.VisionModel : _config.Model;

            // First check if the model is available
            if (!await IsModelAvailable(model, cancellationToken))
            {
                _logger.LogWarning("Model {Model} is not available in Ollama", model);
                return AIMetadataResult.Failed($"Model {model} is not available");
            }

            var prompt = GetMetadataExtractionPrompt(request, isImageFile);
            var ollamaRequest = new OllamaRequest
            {
                Model = model,
                Prompt = prompt,
                Stream = false,
                Options = new OllamaOptions
                {
                    Temperature = _config.Temperature,
                    NumPredict = _config.MaxTokens
                }
            };

            // Add image if it's an image file
            if (isImageFile && request.HasContent)
            {
                var base64Image = request.FileContent != null 
                    ? Convert.ToBase64String(request.FileContent)
                    : ConvertStreamToBase64(request.FileStream!);
                ollamaRequest.Images = new[] { base64Image };
            }

            var json = JsonSerializer.Serialize(ollamaRequest, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_config.BaseUrl}/api/generate", content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Ollama API error: {StatusCode} - {Error}", response.StatusCode, error);
                return AIMetadataResult.Failed($"Ollama API error: {response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseJson, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            });

            if (string.IsNullOrEmpty(ollamaResponse?.Response))
            {
                return AIMetadataResult.Failed("No response content from Ollama");
            }

            var extractedMetadata = ParseMetadataResponse(ollamaResponse.Response);
            
            _logger.LogInformation("Successfully extracted metadata for file: {FileName}", request.FileName);
            return extractedMetadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting metadata for file: {FileName}", request.FileName);
            return AIMetadataResult.Failed($"Extraction failed: {ex.Message}");
        }
    }

    private async Task<bool> IsModelAvailable(string model, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_config.BaseUrl}/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var tags = JsonSerializer.Deserialize<OllamaTagsResponse>(json, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            });

            return tags?.Models?.Any(m => m.Name.StartsWith(model)) == true;
        }
        catch
        {
            return false;
        }
    }

    private string GetMetadataExtractionPrompt(AIMetadataRequest request, bool hasVisualContent)
    {
        var contentDescription = hasVisualContent ? "image" : "content";
        var fileContentPrompt = "";

        if (!hasVisualContent && request.HasContent)
        {
            var textContent = request.FileContent != null 
                ? Encoding.UTF8.GetString(request.FileContent)
                : ReadStreamAsText(request.FileStream!);

            // Truncate if too long
            if (textContent.Length > 6000)
            {
                textContent = textContent.Substring(0, 6000) + "... [truncated]";
            }

            fileContentPrompt = $"\n\nFile content:\n{textContent}";
        }
        
        return $@"Analyze the {contentDescription} and extract metadata in JSON format. 

File information:
- Name: {request.FileName}
- Type: {request.MimeType}
- Size: {request.FileSize} bytes{fileContentPrompt}

Please provide a JSON response with this exact structure:
{{
  ""category"": ""document/image/data/code/other"",
  ""description"": ""Brief description of the file"",
  ""tags"": [""tag1"", ""tag2"", ""tag3""],
  ""metadata"": {{
    ""language"": ""detected language (if applicable)"",
    ""pages"": ""number of pages (if applicable)"",
    ""author"": ""detected author (if applicable)"",
    ""created_date"": ""detected creation date (if applicable)"",
    ""title"": ""detected title (if applicable)"",
    ""subject"": ""detected subject/topic"",
    ""keywords"": [""keyword1"", ""keyword2""],
    ""sentiment"": ""positive/negative/neutral (for text)"",
    ""format_version"": ""detected format version (if applicable)""
  }},
  ""confidence"": 0.85
}}

Focus on extracting meaningful metadata that would help with search and organization. Respond ONLY with valid JSON, no additional text.";
    }

    private AIMetadataResult ParseMetadataResponse(string responseContent)
    {
        try
        {
            // Try to extract JSON from the response (in case there's extra text)
            var jsonStart = responseContent.IndexOf('{');
            var jsonEnd = responseContent.LastIndexOf('}');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonContent = responseContent.Substring(jsonStart, jsonEnd - jsonStart + 1);
                
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;

                var metadata = new Dictionary<string, object>();
                var tags = new List<string>();

                if (root.TryGetProperty("metadata", out var metadataElement))
                {
                    foreach (var prop in metadataElement.EnumerateObject())
                    {
                        metadata[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString()!,
                            JsonValueKind.Number => prop.Value.GetDouble(),
                            JsonValueKind.True or JsonValueKind.False => prop.Value.GetBoolean(),
                            JsonValueKind.Array => prop.Value.EnumerateArray().Select(x => x.GetString()).ToArray(),
                            _ => prop.Value.ToString()
                        };
                    }
                }

                if (root.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
                {
                    tags.AddRange(tagsElement.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x))!);
                }

                var category = root.TryGetProperty("category", out var categoryElement) ? categoryElement.GetString() : null;
                var description = root.TryGetProperty("description", out var descElement) ? descElement.GetString() : null;
                var confidence = root.TryGetProperty("confidence", out var confElement) ? (double?)confElement.GetDouble() : null;

                return AIMetadataResult.Succeeded(metadata, tags.ToArray(), category, description, confidence);
            }
            else
            {
                _logger.LogWarning("No JSON found in Ollama response: {Response}", responseContent);
                return AIMetadataResult.Failed("Invalid JSON response from Ollama");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse metadata response: {Response}", responseContent);
            return AIMetadataResult.Failed($"Failed to parse response: {ex.Message}");
        }
    }

    private static bool IsImageFile(string mimeType)
    {
        return mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ConvertStreamToBase64(Stream stream)
    {
        if (stream.CanSeek)
            stream.Position = 0;
        
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static string ReadStreamAsText(Stream stream)
    {
        if (stream.CanSeek)
            stream.Position = 0;
        
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private class OllamaRequest
    {
        public string Model { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public bool Stream { get; set; } = false;
        public string[]? Images { get; set; }
        public OllamaOptions? Options { get; set; }
    }

    private class OllamaOptions
    {
        public double Temperature { get; set; }
        public int NumPredict { get; set; }
    }

    private class OllamaResponse
    {
        public string Response { get; set; } = string.Empty;
        public bool Done { get; set; }
    }

    private class OllamaTagsResponse
    {
        public OllamaModel[]? Models { get; set; }
    }

    private class OllamaModel
    {
        public string Name { get; set; } = string.Empty;
    }
}