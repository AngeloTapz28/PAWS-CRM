using System;

namespace PAWS.winui.Models;

public class PetItem
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

public class CreatePetRequest
{
    public int PetCategoryId { get; set; }
    public int BreedId { get; set; }
    public int BranchId { get; set; }
    public string PetName { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public int? Age { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public string? Description { get; set; }
    public string? ImagePath { get; set; }
    public decimal? AdoptionFee { get; set; }
    public string? AdoptionFeeType { get; set; }
}

public class UpdatePetRequest
{
    public int PetCategoryId { get; set; }
    public int BreedId { get; set; }
    public int BranchId { get; set; }
    public string PetName { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public int? Age { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public string? Description { get; set; }
    public string? ImagePath { get; set; }
    public decimal? AdoptionFee { get; set; }
    public string? AdoptionFeeType { get; set; }
}

public class RejectPetRequest
{
    public string VetNotes { get; set; } = string.Empty;
}

public class SetPetStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

public class BranchItem
{
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int PetCount { get; set; }
}

public class CreateBranchRequest
{
    public string BranchName { get; set; } = string.Empty;
    public string? Address { get; set; }
}

public class UpdateBranchRequest
{
    public string BranchName { get; set; } = string.Empty;
    public string? Address { get; set; }
}