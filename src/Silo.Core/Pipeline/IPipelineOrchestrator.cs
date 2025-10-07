namespace Silo.Core.Pipeline;

public interface IPipelineOrchestrator
{
    Task<PipelineExecutionResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetEnabledStepsAsync(CancellationToken cancellationToken = default);
    Task EnableStepAsync(string stepName, CancellationToken cancellationToken = default);
    Task DisableStepAsync(string stepName, CancellationToken cancellationToken = default);
}

public record PipelineExecutionResult(
    bool Success,
    IReadOnlyList<PipelineStepExecutionResult> StepResults,
    string? ErrorMessage = null)
{
    public static PipelineExecutionResult Succeeded(IReadOnlyList<PipelineStepExecutionResult> stepResults) 
        => new(true, stepResults);
    
    public static PipelineExecutionResult Failed(string errorMessage, IReadOnlyList<PipelineStepExecutionResult> stepResults) 
        => new(false, stepResults, errorMessage);
}

public record PipelineStepExecutionResult(
    string StepName,
    bool Success,
    TimeSpan Duration,
    string? ErrorMessage = null,
    IDictionary<string, object>? Metadata = null);

public class PipelineConfiguration
{
    public Dictionary<string, bool> StepEnabledStatus { get; set; } = new();
    public int MaxConcurrentSteps { get; set; } = 3;
    public TimeSpan StepTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public bool ContinueOnStepFailure { get; set; } = false;
    public string[] CriticalSteps { get; set; } = Array.Empty<string>();
}