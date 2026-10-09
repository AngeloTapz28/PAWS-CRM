using System;

namespace PAWS.winui.Models;

public class CustomerRetentionItem
{
    public int? CustomerRetentionId { get; set; }
    public int UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime? NextFollowUpDate { get; set; }
    public DateTime? LastContactedAt { get; set; }
    public string? LastContactedByName { get; set; }
    public string RetentionStatus { get; set; } = string.Empty;
}

public class SaveCustomerRetentionRequest
{
    public string? Notes { get; set; }
    public DateTime? NextFollowUpDate { get; set; }
}