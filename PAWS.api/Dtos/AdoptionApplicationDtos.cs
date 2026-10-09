using System.ComponentModel.DataAnnotations;

namespace PAWS.api.Dtos;

public class AdoptionApplicationDto
{
    public int AdoptionApplicationId { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;

    public int PetId { get; set; }
    public string PetName { get; set; } = string.Empty;
    public string PetStatus { get; set; } = string.Empty;

    public int UserId { get; set; }
    public string ApplicantName { get; set; } = string.Empty;

    public int? ReviewedBy { get; set; }
    public string? ReviewerName { get; set; }

    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Occupation { get; set; }

    public string? HousingType { get; set; }
    public string? HomeDescription { get; set; }
    public bool HasYard { get; set; }

    public bool HasOtherPets { get; set; }
    public string? OtherPetsDetails { get; set; }

    public bool HasChildren { get; set; }
    public string? ChildrenDetails { get; set; }

    public string? WorkingHours { get; set; }
    public string? ReasonForAdopting { get; set; }
    public string? PetExperience { get; set; }

    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }

    public string ApplicationStatus { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public string? RejectionReason { get; set; }

    public DateTime AppliedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? InterviewDate { get; set; }
    public string? InterviewNotes { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class SubmitAdoptionApplicationRequestDto
{
    [Required]
    public int PetId { get; set; }

    [StringLength(200)]
    public string? FullName { get; set; }

    [StringLength(255)]
    [EmailAddress]
    public string? Email { get; set; }

    [StringLength(20)]
    public string? Phone { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? Occupation { get; set; }

    [StringLength(50)]
    public string? HousingType { get; set; }

    [StringLength(1000)]
    public string? HomeDescription { get; set; }

    public bool HasYard { get; set; }

    public bool HasOtherPets { get; set; }
    [StringLength(1000)]
    public string? OtherPetsDetails { get; set; }

    public bool HasChildren { get; set; }
    [StringLength(1000)]
    public string? ChildrenDetails { get; set; }

    [StringLength(100)]
    public string? WorkingHours { get; set; }

    [StringLength(2000)]
    public string? ReasonForAdopting { get; set; }

    [StringLength(2000)]
    public string? PetExperience { get; set; }

    [StringLength(200)]
    public string? EmergencyContactName { get; set; }

    [StringLength(20)]
    public string? EmergencyContactPhone { get; set; }

    [StringLength(50)]
    public string? EmergencyContactRelationship { get; set; }

    // Optional. When present, a linked Payment (Type: Donation, Status:
    // Pending) is created alongside the application for Staff/Admin to
    // confirm via the existing Payments page.
    public decimal? DonationAmount { get; set; }
}

public class SetUnderReviewRequestDto
{
    [StringLength(1000)]
    public string? Remarks { get; set; }
}

public class ScheduleInterviewRequestDto
{
    [Required]
    public DateTime InterviewDate { get; set; }

    [StringLength(2000)]
    public string? InterviewNotes { get; set; }
}

public class ApproveApplicationRequestDto
{
    [StringLength(1000)]
    public string? Remarks { get; set; }
}

public class RejectApplicationRequestDto
{
    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string RejectionReason { get; set; } = string.Empty;
}

public class CompleteApplicationRequestDto
{
    [StringLength(1000)]
    public string? Remarks { get; set; }
}