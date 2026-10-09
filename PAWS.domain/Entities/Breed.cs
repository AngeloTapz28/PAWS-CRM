using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// A specific breed that belongs to one PetCategory,
/// for example "Labrador Retriever" under Dog.
/// </summary>
public class Breed
{
    public int BreedId { get; set; }

    // Foreign key: which category this breed belongs to.
    public int PetCategoryId { get; set; }

    public string BreedName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation property: lets C# reach the category object directly.
    public PetCategory? PetCategory { get; set; }
}
