using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// A role name. We will add exactly five rows later (seed data):
/// Administrator, Staff, Veterinarian, Adopter, Super Admin.
/// </summary>
public class Role
{
    public int RoleId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
