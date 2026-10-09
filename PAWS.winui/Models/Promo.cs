using System;

namespace PAWS.winui.Models;

public class PromoItem
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
    public string PromoStatus { get; set; } = string.Empty;
}

public class CreatePromoRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = string.Empty;
    public decimal? DiscountValue { get; set; }
    public DateTime? ExpiryDate { get; set; }
}