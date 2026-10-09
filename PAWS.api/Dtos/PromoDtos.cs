using System.ComponentModel.DataAnnotations;

namespace PAWS.api.Dtos;

public class PromoDto
{
    public int PromoId { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = string.Empty;
    public decimal? DiscountValue { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public string? RedeemedByName { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Calculated, never stored: "Active", "Redeemed", "Expired".
    public string PromoStatus { get; set; } = string.Empty;
}

public class CreatePromoRequestDto
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    // Must be one of: "Percentage", "FixedAmount", "Other".
    [Required]
    public string DiscountType { get; set; } = string.Empty;

    public decimal? DiscountValue { get; set; }

    public DateTime? ExpiryDate { get; set; }
}