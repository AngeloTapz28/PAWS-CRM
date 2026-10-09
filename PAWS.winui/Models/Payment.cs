using System;

namespace PAWS.winui.Models;

public class PaymentItem
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

public class PayableApplicationItem
{
    public int AdoptionApplicationId { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;
    public string PetName { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;

    public string DisplayText => $"{ApplicationNumber} \u2014 {PetName} ({ApplicantName})";
}

public class CreatePaymentRequest
{
    public int AdoptionApplicationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class SetPaymentStatusRequest
{
    public string Status { get; set; } = string.Empty;
}