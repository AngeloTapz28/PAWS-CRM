using System;

namespace PAWS.domain.Entities;

/// <summary>
/// A promotional offer given to one customer (adopter), typically as a
/// retention action. Staff/Administrator create it; Staff honors the
/// discount manually when the customer actually adopts, then marks it
/// Redeemed here. Expired is calculated from ExpiryDate, never stored.
/// </summary>
public class Promo
{
    public int PromoId { get; set; }

    // The customer (adopter) this promo was given to.
    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Allowed values: "Percentage", "FixedAmount", "Other".
    public string DiscountType { get; set; } = "Other";

    // Meaning depends on DiscountType: 20 for 20%, 100 for ₱100 off.
    // Null when DiscountType is "Other" (a non-monetary perk).
    public decimal? DiscountValue { get; set; }

    public DateTime? ExpiryDate { get; set; }

    // Manual action by Staff/Admin once the customer actually uses it.
    public DateTime? RedeemedAt { get; set; }
    public int? RedeemedBy { get; set; }

    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties.
    public User? User { get; set; }
    public User? CreatedByUser { get; set; }
    public User? RedeemedByUser { get; set; }
}