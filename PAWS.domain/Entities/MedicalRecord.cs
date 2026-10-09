using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// A veterinary examination of one pet.
/// The veterinarian is a User who has the Veterinarian role.
/// </summary>
public class MedicalRecord
{
    public int MedicalRecordId { get; set; }

    public int PetId { get; set; }

    // The UserId of the veterinarian who did the examination.
    public int VetId { get; set; }

    public DateTime ExaminationDate { get; set; }

    public string? Diagnosis { get; set; }
    public string? Symptoms { get; set; }
    public string? Treatment { get; set; }
    public string? Medications { get; set; }

    // Weight in kilograms at the time of the exam.
    // Its exact SQL precision is set in Phase 5.
    public decimal? WeightAtExam { get; set; }

    // Allowed values: "Excellent", "Good", "Fair", "Poor", "Critical".
    public string HealthStatus { get; set; } = "Good";

    // The veterinarian must clear the pet on purpose, so this starts false.
    // When it becomes true, Staff are notified (Phase 20).
    public bool FitForAdoption { get; set; }

    public string? Notes { get; set; }
    public DateTime? NextCheckupDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Soft delete, so health history is never permanently lost.
    public DateTime? DeletedAt { get; set; }

    // Navigation properties.
    public Pet? Pet { get; set; }
    public User? Vet { get; set; }
}
