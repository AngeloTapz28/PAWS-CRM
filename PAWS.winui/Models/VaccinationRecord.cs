using System;

namespace PAWS.winui.Models;

public class VaccinationRecordItem
{
    public int VaccinationRecordId { get; set; }
    public int PetId { get; set; }
    public string PetName { get; set; } = string.Empty;
    public int AdministeredBy { get; set; }
    public string AdministeredByName { get; set; } = string.Empty;
    public string VaccineName { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime DateAdministered { get; set; }
    public DateTime? NextDueDate { get; set; }
    public bool IsBooster { get; set; }
    public string? Notes { get; set; }
    public string DueStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateVaccinationRecordRequest
{
    public int PetId { get; set; }
    public string VaccineName { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime DateAdministered { get; set; }
    public DateTime? NextDueDate { get; set; }
    public bool IsBooster { get; set; }
    public string? Notes { get; set; }
}

public class UpdateVaccinationRecordRequest
{
    public string VaccineName { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime DateAdministered { get; set; }
    public DateTime? NextDueDate { get; set; }
    public bool IsBooster { get; set; }
    public string? Notes { get; set; }
}