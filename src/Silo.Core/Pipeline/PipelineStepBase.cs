using Microsoft.Extensions.Logging;

namespace Silo.Core.Pipeline;

public abstract class PipelineStepBase : IPipelineStep
{
    protected readonly ILogger<PipelineStepBase> _logger;
    
    protected PipelineStepBase(ILogger<PipelineStepBase> logger)
    {
        _logger = logger;
    }
    
    public abstract string Name { get; }
    public abstract int Order { get; }
    public virtual bool IsEnabled { get; protected set; } = true;
    public virtual IReadOnlyList<string> Dependencies { get; protected set; } = Array.Empty<string>();
    
    public async Task<PipelineStepResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            _logger.LogInformation("Step {StepName} is disabled, skipping", Name);
            return PipelineStepResult.Succeeded(new Dictionary<string, object> { ["Skipped"] = true });
        }
        
        try
        {
            _logger.LogInformation("Executing pipeline step: {StepName}", Name);
            
            var canExecute = await CanExecuteAsync(context, cancellationToken);
            if (!canExecute)
            {
                _logger.LogWarning("Step {StepName} cannot execute with current context", Name);
                return PipelineStepResult.Failed($"Step {Name} cannot execute with current context");
            }
            
            var result = await ExecuteInternalAsync(context, cancellationToken);
            
            if (result.Success)
            {
                _logger.LogInformation("Step {StepName} completed successfully", Name);
            }
            else
            {
                _logger.LogError("Step {StepName} failed: {ErrorMessage}", Name, result.ErrorMessage);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step {StepName} threw an exception", Name);
            return PipelineStepResult.Failed($"Step {Name} threw an exception: {ex.Message}");
        }
    }
    
    public virtual Task<bool> CanExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        // Check if all dependencies have been executed successfully
        foreach (var dependency in Dependencies)
        {
            if (!context.StepResults.ContainsKey(dependency))
            {
                return Task.FromResult(false);
            }
        }
        
        return Task.FromResult(true);
    }
    
    protected abstract Task<PipelineStepResult> ExecuteInternalAsync(PipelineContext context, CancellationToken cancellationToken);
}