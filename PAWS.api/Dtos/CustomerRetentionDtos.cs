using System.ComponentModel.DataAnnotations;

namespace PAWS.api.Dtos;

public class CustomerRetentionDto
{
    public int? CustomerRetentionId { get; set; }
    public int UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;

    public string? Notes { get; set; }
    public DateTime? NextFollowUpDate { get; set; }
    public DateTime? LastContactedAt { get; set; }
    public string? LastContactedByName { get; set; }

    // Calculated, never stored: "Not Scheduled", "Overdue", "Due Soon", "On Track".
    public string RetentionStatus { get; set; } = string.Empty;
}

public class SaveCustomerRetentionRequestDto
{
    [StringLength(2000)]
    public string? Notes { get; set; }

    public DateTime? NextFollowUpDate { get; set; }
}