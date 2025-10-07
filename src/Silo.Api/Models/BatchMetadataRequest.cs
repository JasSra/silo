using System.ComponentModel.DataAnnotations;

namespace Silo.Api.Models;

public class BatchMetadataRequest
{
    [Required]
    public IEnumerable<Guid> FileIds { get; set; } = new List<Guid>();
}