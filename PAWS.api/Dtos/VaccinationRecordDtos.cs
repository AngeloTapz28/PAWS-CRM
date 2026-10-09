using System.ComponentModel.DataAnnotations;

namespace PAWS.api.Dtos;

public class VaccinationRecordDto
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

    // Calculated, never stored: "Upcoming", "Overdue", "None Scheduled".
    public string DueStatus { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateVaccinationRecordRequestDto
{
    [Required] public int PetId { get; set; }
    [Required][StringLength(200, MinimumLength = 1)] public string VaccineName { get; set; } = string.Empty;
    [StringLength(200)] public string? Manufacturer { get; set; }
    [StringLength(100)] public string? BatchNumber { get; set; }
    [Required] public DateTime DateAdministered { get; set; }
    public DateTime? NextDueDate { get; set; }
    public bool IsBooster { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
}

public class UpdateVaccinationRecordRequestDto
{
    [Required][StringLength(200, MinimumLength = 1)] public string VaccineName { get; set; } = string.Empty;
    [StringLength(200)] public string? Manufacturer { get; set; }
    [StringLength(100)] public string? BatchNumber { get; set; }
    [Required] public DateTime DateAdministered { get; set; }
    public DateTime? NextDueDate { get; set; }
    public bool IsBooster { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
}