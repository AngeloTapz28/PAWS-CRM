using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// A pet available (or becoming available) for adoption.
/// This is the central entity of the system.
/// </summary>
public class Pet
{
    public int PetId { get; set; }

    // Foreign keys to the category, breed and branch.
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

    // Money value. Its exact SQL precision, decimal(18,2), is set in Phase 5.
    public decimal? AdoptionFee { get; set; }

    // Allowed values: "Fixed", "Donation", "Free".
    public string? AdoptionFeeType { get; set; }

    // Allowed values: "Pending", "Available", "Under Treatment",
    // "Adopted", "Unavailable".
    // A new pet starts as "Pending" until it has been examined and approved.
    public string Status { get; set; } = "Pending";

    // Both are UserIds. Both are optional here.
    public int? AddedBy { get; set; }
    public int? VetApprovedBy { get; set; }

    // The Administrator/Staff who gave the LISTING approval (Phase 16).
    // Set together with IsAdminApproved, never independently.
    public int? AdminApprovedBy { get; set; }

    // Approval flags used by the pet approval workflow.
    // IsVetApproved: medical fitness, set by a Veterinarian.
    // IsAdminApproved: business/listing decision, set by Administrator/Staff.
    // A pet's Status is only ever "Available" when BOTH are true; Status is
    // always recomputed from these two flags, never set to Available directly.
    public bool IsVetApproved { get; set; }
    public bool IsAdminApproved { get; set; }

    // Written by a Veterinarian when rejecting/deferring approval, explaining why.
    // Cleared automatically the next time a Veterinarian approves.
    public string? VetNotes { get; set; }

    // When the pet became visible to adopters.
    public DateTime? ListedAt { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties.
    public PetCategory? PetCategory { get; set; }
    public Breed? Breed { get; set; }
    public Branch? Branch { get; set; }

    // These three all point to the User class and are usually different people.
    public User? AddedByUser { get; set; }
    public User? VetApprovedByUser { get; set; }
    public User? AdminApprovedByUser { get; set; }
}