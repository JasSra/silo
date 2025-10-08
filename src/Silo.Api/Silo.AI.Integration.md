# AI Integrations

This repository now supports multiple AI providers for file metadata extraction:

- OpenAI (chat/completions with vision)
- Azure OpenAI (chat/completions with vision)
- Ollama (local LLMs, optional vision)

## Configure

App settings section `AI` controls provider selection and options. Example (Development):

```json
{
  "AI": {
    "Enabled": true,
    "Provider": "AzureOpenAI", // One of: None, OpenAI, AzureOpenAI, Ollama
    "MaxFileSizeForAnalysis": 10485760,
    "SupportedMimeTypes": ["text/plain", "application/pdf", "image/jpeg", "image/png"],
    "AzureOpenAI": {
      "Endpoint": "https://your-azure-openai.openai.azure.com",
      "ApiKey": "${AZURE_OPENAI_API_KEY}",
      "Deployment": "gpt-4o",
      "TextDeployment": "gpt-4o-mini",
      "ApiVersion": "2024-06-01",
      "MaxTokens": 1000,
      "Temperature": 0.1
    }
  }
}
```

OpenAI example:

```json
{
  "AI": {
    "Enabled": true,
    "Provider": "OpenAI",
    "OpenAI": {
      "ApiKey": "${OPENAI_API_KEY}",
      "Model": "gpt-4.1-mini",
      "TextModel": "gpt-4o-mini",
      "BaseUrl": "https://api.openai.com/v1"
    }
  }
}
```

Ollama example:

```json
{
  "AI": {
    "Enabled": true,
    "Provider": "Ollama",
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "Model": "llama3",
      "VisionModel": "llava"
    }
  }
}
```

## Behavior

- Pipeline step `AIMetadataExtraction` selects the configured provider at runtime via `IAIMetadataServiceFactory`.
- For small files (<1 MB) it runs synchronously in-pipeline; for larger files it enqueues a Hangfire job.
- The AI service returns:
  - tags: string[]
  - category, description, confidence
  - metadata: key/value dictionary (includes provider name as `provider`)

## Notes

- Ensure your chosen provider can handle the MIME types and file sizes you're sending.
- Vision support is used for image MIME types by providers that support it.
- For Azure OpenAI, set Endpoint, ApiKey, and your deployment names.
