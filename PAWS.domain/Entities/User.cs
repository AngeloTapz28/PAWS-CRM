using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// One account in the system. Adopters, Staff, Administrators,
/// Veterinarians and the Super Admin are all Users. What each person
/// can do is decided by their roles (see RoleUser).
/// </summary>
public class User
{
    public int UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    // Must be unique. The unique rule is added in Phase 5 (DbContext).
    public string Email { get; set; } = string.Empty;

    // This will store a HASHED password (added in Phase 15).
    // It must never hold a real, readable password.
    public string Password { get; set; } = string.Empty;

    public string? Phone { get; set; }
    public string? Address { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Avatar { get; set; }

    // Allowed values: "Active", "Inactive", "Suspended".
    // A suspended user must not be allowed to log in.
    public string Status { get; set; } = "Active";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
