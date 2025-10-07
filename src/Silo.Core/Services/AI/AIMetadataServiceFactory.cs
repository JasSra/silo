using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Silo.Core.Services.AI;

public interface IAIMetadataServiceFactory
{
    Task<IAIMetadataService?> GetServiceAsync();
    bool IsAIEnabled { get; }
}

public class AIMetadataServiceFactory : IAIMetadataServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AIConfiguration _config;
    private readonly ILogger<AIMetadataServiceFactory> _logger;

    public bool IsAIEnabled => _config.Enabled && _config.Provider != AIProvider.None;

    public AIMetadataServiceFactory(
        IServiceProvider serviceProvider,
        IOptions<AIConfiguration> config,
        ILogger<AIMetadataServiceFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<IAIMetadataService?> GetServiceAsync()
    {
        if (!IsAIEnabled)
        {
            _logger.LogDebug("AI metadata extraction is disabled");
            return null;
        }

        try
        {
            IAIMetadataService service = _config.Provider switch
            {
                AIProvider.OpenAI => _serviceProvider.GetRequiredService<OpenAIMetadataService>(),
                AIProvider.Ollama => _serviceProvider.GetRequiredService<OllamaMetadataService>(),
                _ => throw new InvalidOperationException($"Unsupported AI provider: {_config.Provider}")
            };

            // Check if the service is properly configured
            if (!await service.IsConfiguredAsync())
            {
                _logger.LogWarning("AI service {Provider} is not properly configured", service.ProviderName);
                return null;
            }

            return service;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get AI metadata service for provider: {Provider}", _config.Provider);
            return null;
        }
    }
}