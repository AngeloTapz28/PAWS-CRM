using System;

namespace PAWS.winui.Models;

public class AdoptionApplicationItem
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

public class SubmitAdoptionApplicationRequest
{
    public int PetId { get; set; }
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
    public decimal? DonationAmount { get; set; }
}

public class SetUnderReviewRequest
{
    public string? Remarks { get; set; }
}

public class ScheduleInterviewRequest
{
    public DateTime InterviewDate { get; set; }
    public string? InterviewNotes { get; set; }
}

public class ApproveApplicationRequest
{
    public string? Remarks { get; set; }
}

public class RejectApplicationRequest
{
    public string RejectionReason { get; set; } = string.Empty;
}

public class CompleteApplicationRequest
{
    public string? Remarks { get; set; }
}