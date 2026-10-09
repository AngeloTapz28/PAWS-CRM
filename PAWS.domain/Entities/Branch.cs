using System;

namespace PAWS.domain.Entities;

/// <summary>
/// One physical location belonging to this tenant. Every tenant starts
/// with exactly one auto-created "Main Branch" (seeded by the migration,
/// with all pre-existing pets assigned to it); Staff/Admin can create more
/// branches afterward and reassign pets between them freely.
/// </summary>
public class Branch
{
    public int BranchId { get; set; }

    public string BranchName { get; set; } = string.Empty;
    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}