using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// A payment recorded against an adoption application, such as an
/// adoption fee or a donation. Only Staff and Admin record payments.
/// </summary>
public class Payment
{
    public int PaymentId { get; set; }

    // A unique readable reference such as "PAY-2026-00001".
    // The API generates it when the payment is saved (Phase 19).
    // It will be made unique in Phase 5.
    public string ReferenceNumber { get; set; } = string.Empty;

    // REQUIRED: the business rule says every payment must be linked
    // to an adoption application.
    public int AdoptionApplicationId { get; set; }

    // The UserId of the person paying (usually the adopter).
    public int? PayerId { get; set; }

    // The UserId of the Staff/Admin who recorded the payment.
    public int? RecordedBy { get; set; }

    // Allowed values: "Adoption Fee", "Donation", "Medical Fee", "Other".
    public string Type { get; set; } = "Adoption Fee";

    // Money value. Its exact SQL precision, decimal(18,2), is set in Phase 5.
    public decimal Amount { get; set; }

    // Currency code, for example "PHP".
    public string Currency { get; set; } = "PHP";

    // Allowed values: "Cash", "GCash", "Maya", "Bank Transfer", "Credit Card".
    public string Method { get; set; } = "Cash";

    // Allowed values: "Pending", "Completed", "Failed", "Refunded".
    public string Status { get; set; } = "Pending";

    // File path of the uploaded receipt or screenshot. Optional.
    public string? ProofOfPayment { get; set; }

    public string? Notes { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Soft delete: keeps financial history in the database.
    public DateTime? DeletedAt { get; set; }

    // Navigation properties.
    public AdoptionApplication? AdoptionApplication { get; set; }
    public User? Payer { get; set; }
    public User? RecordedByUser { get; set; }
}
