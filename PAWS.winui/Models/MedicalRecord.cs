using System;

namespace PAWS.winui.Models;

public class MedicalRecordItem
{
    public int MedicalRecordId { get; set; }
    public int PetId { get; set; }
    public string PetName { get; set; } = string.Empty;
    public int VetId { get; set; }
    public string VetName { get; set; } = string.Empty;
    public DateTime ExaminationDate { get; set; }
    public string? Diagnosis { get; set; }
    public string? Symptoms { get; set; }
    public string? Treatment { get; set; }
    public string? Medications { get; set; }
    public decimal? WeightAtExam { get; set; }
    public string HealthStatus { get; set; } = string.Empty;
    public bool FitForAdoption { get; set; }
    public string? Notes { get; set; }
    public DateTime? NextCheckupDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateMedicalRecordRequest
{
    public int PetId { get; set; }
    public DateTime ExaminationDate { get; set; }
    public string? Diagnosis { get; set; }
    public string? Symptoms { get; set; }
    public string? Treatment { get; set; }
    public string? Medications { get; set; }
    public decimal? WeightAtExam { get; set; }
    public string HealthStatus { get; set; } = string.Empty;
    public bool FitForAdoption { get; set; }
    public string? Notes { get; set; }
    public DateTime? NextCheckupDate { get; set; }
}

public class UpdateMedicalRecordRequest
{
    public DateTime ExaminationDate { get; set; }
    public string? Diagnosis { get; set; }
    public string? Symptoms { get; set; }
    public string? Treatment { get; set; }
    public string? Medications { get; set; }
    public decimal? WeightAtExam { get; set; }
    public string HealthStatus { get; set; } = string.Empty;
    public bool FitForAdoption { get; set; }
    public string? Notes { get; set; }
    public DateTime? NextCheckupDate { get; set; }
}