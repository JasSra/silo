using Microsoft.AspNetCore.Mvc;
using Silo.Core.Models;
using Silo.Core.Services;

namespace Silo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class BackupController : ControllerBase
{
    private readonly IBackupService _backupService;
    private readonly ILogger<BackupController> _logger;

    public BackupController(IBackupService backupService, ILogger<BackupController> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new backup job
    /// </summary>
    [HttpPost("jobs")]
    public async Task<ActionResult<BackupJob>> CreateBackupJob([FromBody] BackupJob backupJob)
    {
        try
        {
            var jobId = await _backupService.ScheduleBackupAsync(backupJob);
            var createdJob = await _backupService.GetBackupJobAsync(jobId);
            
            return CreatedAtAction(nameof(GetBackupJob), new { id = jobId }, createdJob);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating backup job");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Get backup job by ID
    /// </summary>
    [HttpGet("jobs/{id:guid}")]
    public async Task<ActionResult<BackupJob>> GetBackupJob(Guid id)
    {
        try
        {
            var job = await _backupService.GetBackupJobAsync(id);
            if (job == null)
                return NotFound();

            return Ok(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving backup job {JobId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Get all active backup jobs
    /// </summary>
    [HttpGet("jobs")]
    public async Task<ActionResult<IEnumerable<BackupJob>>> GetActiveBackupJobs()
    {
        try
        {
            var jobs = await _backupService.GetActiveBackupJobsAsync();
            return Ok(jobs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active backup jobs");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Execute a backup job immediately
    /// </summary>
    [HttpPost("jobs/{id:guid}/execute")]
    public async Task<ActionResult<BackupResult>> ExecuteBackupJob(Guid id)
    {
        try
        {
            var result = await _backupService.ExecuteBackupAsync(id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing backup job {JobId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Cancel a running backup job
    /// </summary>
    [HttpPost("jobs/{id:guid}/cancel")]
    public async Task<IActionResult> CancelBackupJob(Guid id)
    {
        try
        {
            var cancelled = await _backupService.CancelBackupAsync(id);
            if (!cancelled)
                return BadRequest("Unable to cancel backup job");

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling backup job {JobId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Delete a backup job
    /// </summary>
    [HttpDelete("jobs/{id:guid}")]
    public async Task<IActionResult> DeleteBackupJob(Guid id)
    {
        try
        {
            var deleted = await _backupService.DeleteBackupJobAsync(id);
            if (!deleted)
                return NotFound();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting backup job {JobId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Get backup result by job ID
    /// </summary>
    [HttpGet("jobs/{id:guid}/result")]
    public async Task<ActionResult<BackupResult>> GetBackupResult(Guid id)
    {
        try
        {
            var result = await _backupService.GetBackupResultAsync(id);
            if (result == null)
                return NotFound();

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving backup result for job {JobId}", id);
            return StatusCode(500, "Internal server error");
        }
    }
}