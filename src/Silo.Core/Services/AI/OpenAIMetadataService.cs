using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Silo.Core.Services.AI;

public class OpenAIMetadataService : IAIMetadataService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAIConfiguration _config;
    private readonly ILogger<OpenAIMetadataService> _logger;

    public string ProviderName => "OpenAI";

    public OpenAIMetadataService(
        HttpClient httpClient,
        IOptions<AIConfiguration> aiConfig,
        ILogger<OpenAIMetadataService> logger)
    {
        _httpClient = httpClient;
        _config = aiConfig.Value.OpenAI ?? throw new ArgumentException("OpenAI configuration is required");
        _logger = logger;

        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }
    }

    public Task<bool> IsConfiguredAsync()
    {
        return Task.FromResult(!string.IsNullOrEmpty(_config.ApiKey));
    }

    public async Task<AIMetadataResult> ExtractMetadataAsync(AIMetadataRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Extracting metadata for file: {FileName}", request.FileName);

            var isImageFile = IsImageFile(request.MimeType);
            var model = isImageFile ? _config.Model : _config.TextModel;

            var messages = new List<object>();
            
            if (isImageFile && request.HasContent)
            {
                messages.Add(CreateVisionMessage(request));
            }
            else if (request.HasContent)
            {
                messages.Add(CreateTextMessage(request));
            }
            else
            {
                messages.Add(CreateFileNameOnlyMessage(request));
            }

            var requestBody = new
            {
                model = model,
                messages = messages,
                max_tokens = _config.MaxTokens,
                temperature = _config.Temperature,
                response_format = new { type = "json_object" }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_config.BaseUrl}/chat/completions", content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("OpenAI API error: {StatusCode} - {Error}", response.StatusCode, error);
                return AIMetadataResult.Failed($"OpenAI API error: {response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseJson);

            if (openAIResponse?.Choices?.FirstOrDefault()?.Message?.Content == null)
            {
                return AIMetadataResult.Failed("No response content from OpenAI");
            }

            var extractedMetadata = ParseMetadataResponse(openAIResponse.Choices.First().Message.Content);
            
            _logger.LogInformation("Successfully extracted metadata for file: {FileName}", request.FileName);
            return extractedMetadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting metadata for file: {FileName}", request.FileName);
            return AIMetadataResult.Failed($"Extraction failed: {ex.Message}");
        }
    }

    private object CreateVisionMessage(AIMetadataRequest request)
    {
        var base64Image = request.FileContent != null 
            ? Convert.ToBase64String(request.FileContent)
            : ConvertStreamToBase64(request.FileStream!);

        var mimeType = request.MimeType;
        var dataUrl = $"data:{mimeType};base64,{base64Image}";

        return new
        {
            role = "user",
            content = new object[]
            {
                new
                {
                    type = "text",
                    text = GetMetadataExtractionPrompt(request, true)
                },
                new
                {
                    type = "image_url",
                    image_url = new { url = dataUrl }
                }
            }
        };
    }

    private object CreateTextMessage(AIMetadataRequest request)
    {
        var textContent = request.FileContent != null 
            ? Encoding.UTF8.GetString(request.FileContent)
            : ReadStreamAsText(request.FileStream!);

        // Truncate if too long
        if (textContent.Length > 8000)
        {
            textContent = textContent.Substring(0, 8000) + "... [truncated]";
        }

        return new
        {
            role = "user",
            content = GetMetadataExtractionPrompt(request, false) + "\n\nFile content:\n" + textContent
        };
    }

    private object CreateFileNameOnlyMessage(AIMetadataRequest request)
    {
        return new
        {
            role = "user",
            content = GetMetadataExtractionPrompt(request, false)
        };
    }

    private string GetMetadataExtractionPrompt(AIMetadataRequest request, bool hasVisualContent)
    {
        var contentDescription = hasVisualContent ? "visual content" : "content";
        
        return $@"Analyze the {contentDescription} and extract metadata in JSON format. 

File information:
- Name: {request.FileName}
- Type: {request.MimeType}
- Size: {request.FileSize} bytes

Please provide a JSON response with this structure:
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

Focus on extracting meaningful metadata that would help with search and organization.";
    }

    private AIMetadataResult ParseMetadataResponse(string responseContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseContent);
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

    private record OpenAIResponse(Choice[] Choices);
    private record Choice(Message Message);
    private record Message(string Content);
}