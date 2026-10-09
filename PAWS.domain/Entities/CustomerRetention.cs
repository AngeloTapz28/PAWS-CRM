using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// Staff-entered retention information about ONE customer (adopter).
/// It stores only what staff type: notes, the next follow-up date and the
/// last contact. The retention STATUS (Active, At Risk, Inactive) is NOT
/// stored here. It is calculated from real adoption, payment and customer
/// service data by a service (Phase 22), so it never goes out of date.
/// </summary>
public class CustomerRetention
{
    public int CustomerRetentionId { get; set; }

    // The customer (adopter) this row is about.
    // It must reference an existing User.
    public int UserId { get; set; }

    // Staff comments about the customer.
    public string? Notes { get; set; }

    // When staff plan to follow up next.
    public DateTime? NextFollowUpDate { get; set; }

    // When staff last contacted the customer, and who did it.
    public DateTime? LastContactedAt { get; set; }
    public int? LastContactedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties. Both point to User:
    // the customer, and the staff member who last contacted them.
    public User? User { get; set; }
    public User? LastContactedByUser { get; set; }
}
