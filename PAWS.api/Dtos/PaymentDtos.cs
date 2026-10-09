using System.ComponentModel.DataAnnotations;

namespace PAWS.api.Dtos;

public class PaymentDto
{
    public int PaymentId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public int AdoptionApplicationId { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? BranchName { get; set; }
}

public class CreatePaymentRequestDto
{
    [Required] public int AdoptionApplicationId { get; set; }
    [Required] public string Type { get; set; } = string.Empty;
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }
    [Required] public string Method { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class SetPaymentStatusRequestDto
{
    [Required] public string Status { get; set; } = string.Empty;
}

public class PayableApplicationDto
{
    public int AdoptionApplicationId { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;
    public string PetName { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;

    // The pet's adoption fee. Null means the pet has no fee set.
    public decimal? AdoptionFee { get; set; }
}