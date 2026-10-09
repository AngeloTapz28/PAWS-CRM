using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// The top level of the pet taxonomy, such as Dog, Cat or Bird.
/// Each category contains many breeds (see Breed).
/// </summary>
public class PetCategory
{
    public int PetCategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public string? Description { get; set; }

    // Soft delete: set to false instead of deleting the row,
    // so existing pets that use this category are never broken.
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
