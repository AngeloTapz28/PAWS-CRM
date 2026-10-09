using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// Links one User to one Role. This is how a user can hold several roles.
/// The pair (UserId, RoleId) will be the primary key, so the same role
/// cannot be given to the same user twice.
/// </summary>
public class RoleUser
{
    public int UserId { get; set; }
    public int RoleId { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    // The UserId of the Administrator who gave this role. Optional.
    public int? AssignedBy { get; set; }

    // Navigation properties: let C# move from a RoleUser to its User/Role.
    public User? User { get; set; }
    public Role? Role { get; set; }
    public User? AssignedByUser { get; set; }
}
