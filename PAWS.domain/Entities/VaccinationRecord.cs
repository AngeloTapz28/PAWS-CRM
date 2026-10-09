using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// One vaccine given to one pet.
/// The user who administered it is a veterinarian (User + Veterinarian role).
/// </summary>
public class VaccinationRecord
{
    public int VaccinationRecordId { get; set; }

    public int PetId { get; set; }

    // The UserId of the person who gave the vaccine.
    public int AdministeredBy { get; set; }

    public string VaccineName { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? BatchNumber { get; set; }

    public DateTime DateAdministered { get; set; }

    // Used later to show which vaccinations are due or overdue.
    public DateTime? NextDueDate { get; set; }

    public bool IsBooster { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties.
    public Pet? Pet { get; set; }
    public User? AdministeredByUser { get; set; }
}
