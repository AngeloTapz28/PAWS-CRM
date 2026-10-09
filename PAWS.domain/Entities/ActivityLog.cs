using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// The audit trail. Each row records one significant action in the system.
/// It can point to a record in ANY table using ModelType and ModelId,
/// so no separate log table is needed for each entity.
/// </summary>
public class ActivityLog
{
    public int ActivityLogId { get; set; }

    // Optional: the user who did the action. It is empty if the user was
    // deleted later, so the log entry survives.
    public int? UserId { get; set; }

    // What happened, for example "Created Pet" or "Approved Application".
    public string Action { get; set; } = string.Empty;

    // Which kind of record was affected, for example "Pet" or "Payment".
    // ModelType and ModelId are plain values, not foreign keys.
    public string? ModelType { get; set; }
    public int? ModelId { get; set; }

    public string? Description { get; set; }

    // The record's values before and after the change, stored as JSON text.
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property (optional).
    public User? User { get; set; }
}
