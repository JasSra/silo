using System.Diagnostics;
using Silo.Core.Pipeline;

namespace Silo.Api.Services;

public class PipelineOrchestrator : IPipelineOrchestrator
{
    private readonly ILogger<PipelineOrchestrator> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly PipelineConfiguration _configuration;
    private readonly List<IPipelineStep> _steps;

    public PipelineOrchestrator(
        ILogger<PipelineOrchestrator> logger,
        IServiceProvider serviceProvider,
        PipelineConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _steps = new List<IPipelineStep>();
        
        LoadSteps();
    }

    public async Task<PipelineExecutionResult> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting pipeline execution for file: {FileName}", context.FileMetadata.FileName);
        
        var stepResults = new List<PipelineStepExecutionResult>();
        var enabledSteps = await GetOrderedEnabledStepsAsync();
        
        foreach (var step in enabledSteps)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Pipeline execution cancelled");
                return PipelineExecutionResult.Failed("Pipeline execution cancelled", stepResults);
            }
            
            var stepResult = await ExecuteStepWithTimeout(step, context, cancellationToken);
            stepResults.Add(stepResult);
            
            // Store step result in context for dependent steps
            context.SetStepResult(step.Name, stepResult);
            
            if (!stepResult.Success)
            {
                _logger.LogError("Step {StepName} failed: {ErrorMessage}", step.Name, stepResult.ErrorMessage);
                
                // Check if this is a critical step or if we should stop on failure
                if (_configuration.CriticalSteps.Contains(step.Name) || !_configuration.ContinueOnStepFailure)
                {
                    _logger.LogError("Critical step failed or configured to stop on failure. Aborting pipeline.");
                    return PipelineExecutionResult.Failed($"Pipeline failed at step {step.Name}", stepResults);
                }
                
                _logger.LogWarning("Non-critical step failed, continuing pipeline execution");
            }
        }
        
        _logger.LogInformation("Pipeline execution completed successfully");
        return PipelineExecutionResult.Succeeded(stepResults);
    }
    
    private async Task<PipelineStepExecutionResult> ExecuteStepWithTimeout(
        IPipelineStep step, 
        PipelineContext context, 
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_configuration.StepTimeout);
            
            var result = await step.ExecuteAsync(context, timeoutCts.Token);
            stopwatch.Stop();
            
            return new PipelineStepExecutionResult(
                step.Name,
                result.Success,
                stopwatch.Elapsed,
                result.ErrorMessage,
                result.Metadata);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new PipelineStepExecutionResult(
                step.Name,
                false,
                stopwatch.Elapsed,
                "Step was cancelled");
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new PipelineStepExecutionResult(
                step.Name,
                false,
                stopwatch.Elapsed,
                $"Step timed out after {_configuration.StepTimeout}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error executing step {StepName}", step.Name);
            return new PipelineStepExecutionResult(
                step.Name,
                false,
                stopwatch.Elapsed,
                $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<string>> GetEnabledStepsAsync(CancellationToken cancellationToken = default)
    {
        var enabledSteps = await GetOrderedEnabledStepsAsync();
        return enabledSteps.Select(s => s.Name).ToList();
    }

    public Task EnableStepAsync(string stepName, CancellationToken cancellationToken = default)
    {
        _configuration.StepEnabledStatus[stepName] = true;
        _logger.LogInformation("Enabled pipeline step: {StepName}", stepName);
        return Task.CompletedTask;
    }

    public Task DisableStepAsync(string stepName, CancellationToken cancellationToken = default)
    {
        _configuration.StepEnabledStatus[stepName] = false;
        _logger.LogInformation("Disabled pipeline step: {StepName}", stepName);
        return Task.CompletedTask;
    }
    
    private void LoadSteps()
    {
        var stepTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IPipelineStep).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
        
        foreach (var stepType in stepTypes)
        {
            try
            {
                var step = (IPipelineStep)_serviceProvider.GetRequiredService(stepType);
                _steps.Add(step);
                _logger.LogInformation("Loaded pipeline step: {StepName} (Order: {Order})", step.Name, step.Order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load pipeline step: {StepType}", stepType.Name);
            }
        }
    }
    
    private async Task<List<IPipelineStep>> GetOrderedEnabledStepsAsync()
    {
        var enabledSteps = _steps.Where(step => 
        {
            var isEnabled = _configuration.StepEnabledStatus.TryGetValue(step.Name, out var enabled) 
                ? enabled 
                : step.IsEnabled;
            return isEnabled;
        }).ToList();
        
        // Topological sort to respect dependencies
        var sortedSteps = TopologicalSort(enabledSteps);
        return sortedSteps;
    }
    
    private List<IPipelineStep> TopologicalSort(List<IPipelineStep> steps)
    {
        var stepsByName = steps.ToDictionary(s => s.Name);
        var result = new List<IPipelineStep>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();
        
        foreach (var step in steps.OrderBy(s => s.Order))
        {
            if (!visited.Contains(step.Name))
            {
                Visit(step, stepsByName, visited, visiting, result);
            }
        }
        
        return result;
    }
    
    private void Visit(
        IPipelineStep step, 
        Dictionary<string, IPipelineStep> stepsByName,
        HashSet<string> visited, 
        HashSet<string> visiting, 
        List<IPipelineStep> result)
    {
        if (visiting.Contains(step.Name))
        {
            throw new InvalidOperationException($"Circular dependency detected involving step {step.Name}");
        }
        
        if (visited.Contains(step.Name))
        {
            return;
        }
        
        visiting.Add(step.Name);
        
        foreach (var dependency in step.Dependencies)
        {
            if (stepsByName.TryGetValue(dependency, out var dependentStep))
            {
                Visit(dependentStep, stepsByName, visited, visiting, result);
            }
        }
        
        visiting.Remove(step.Name);
        visited.Add(step.Name);
        result.Add(step);
    }
}