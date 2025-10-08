using Silo.Core.Models;

namespace Silo.Core.Pipeline;

public interface IPipelineStep
{
    string Name { get; }
    int Order { get; }
    bool IsEnabled { get; }
    IReadOnlyList<string> Dependencies { get; }
    
    Task<PipelineStepResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default);
    Task<bool> CanExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default);
}

public record PipelineStepResult(
    bool Success,
    string? ErrorMessage = null,
    IDictionary<string, object>? Metadata = null)
{
    public static PipelineStepResult Succeeded(IDictionary<string, object>? metadata = null) 
        => new(true, null, metadata);
    
    public static PipelineStepResult Failed(string errorMessage, IDictionary<string, object>? metadata = null) 
        => new(false, errorMessage, metadata);
}

public class PipelineContext
{
    public required FileMetadata FileMetadata { get; init; }
    public required Stream FileStream { get; init; }
    public required Guid TenantId { get; init; }
    public Dictionary<string, object> StepResults { get; init; } = new();
    public Dictionary<string, object> Properties { get; init; } = new();
    
    public T? GetStepResult<T>(string stepName) where T : class
    {
        return StepResults.TryGetValue(stepName, out var result) ? result as T : null;
    }
    
    public void SetStepResult(string stepName, object result)
    {
        StepResults[stepName] = result;
    }
    
    public T? GetProperty<T>(string key) where T : class
    {
        return Properties.TryGetValue(key, out var value) ? value as T : null;
    }
    
    public bool HasProperty(string key)
    {
        return Properties.ContainsKey(key);
    }
    
    public void SetProperty(string key, object value)
    {
        Properties[key] = value;
    }
}