using System.ComponentModel.DataAnnotations;

namespace PAWS.api.Dtos;

public class PetDto
{
    public int PetId { get; set; }
    public int PetCategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int BreedId { get; set; }
    public string BreedName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;

    public string PetName { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public int? Age { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public string? Description { get; set; }
    public string? ImagePath { get; set; }

    public decimal? AdoptionFee { get; set; }
    public string? AdoptionFeeType { get; set; }

    public string Status { get; set; } = string.Empty;
    public bool IsVetApproved { get; set; }
    public bool IsAdminApproved { get; set; }
    public string? VetNotes { get; set; }

    public string? AddedByName { get; set; }
    public string? VetApprovedByName { get; set; }
    public string? AdminApprovedByName { get; set; }

    public DateTime? ListedAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePetRequestDto
{
    [Required]
    public int PetCategoryId { get; set; }

    [Required]
    public int BreedId { get; set; }

    [Required]
    public int BranchId { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string PetName { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Gender { get; set; }

    [Range(0, 100)]
    public int? Age { get; set; }

    [StringLength(20)]
    public string? Size { get; set; }

    [StringLength(50)]
    public string? Color { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    // Must start with http:// or https:// (checked in the controller, since
    // that rule depends on more than one attribute and reads better as code).
    [StringLength(500)]
    public string? ImagePath { get; set; }

    public decimal? AdoptionFee { get; set; }

    [StringLength(20)]
    public string? AdoptionFeeType { get; set; }
}

public class UpdatePetRequestDto
{
    [Required]
    public int PetCategoryId { get; set; }

    [Required]
    public int BreedId { get; set; }

    [Required]
    public int BranchId { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string PetName { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Gender { get; set; }

    [Range(0, 100)]
    public int? Age { get; set; }

    [StringLength(20)]
    public string? Size { get; set; }

    [StringLength(50)]
    public string? Color { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(500)]
    public string? ImagePath { get; set; }

    public decimal? AdoptionFee { get; set; }

    [StringLength(20)]
    public string? AdoptionFeeType { get; set; }
}

public class RejectPetRequestDto
{
    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string VetNotes { get; set; } = string.Empty;
}

public class SetPetStatusRequestDto
{
    // Must be "Under Treatment" or "Unavailable" — checked in the controller
    // against PetStatuses, since [AllowedValues] would duplicate that list here.
    [Required]
    public string Status { get; set; } = string.Empty;
}