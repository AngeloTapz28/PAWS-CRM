using System;

namespace PAWS.winui.Models;

public class SubscriptionPlanItem
{
    public int SubscriptionPlanId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int BranchLimit { get; set; }          // int.MaxValue means unlimited
    public string TermsAndConditions { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CompanyCount { get; set; }
}

public class CreateSubscriptionPlanRequest
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int BranchLimit { get; set; }
    public string TermsAndConditions { get; set; } = string.Empty;
}

public class UpdateSubscriptionPlanRequest
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int BranchLimit { get; set; }
    public string TermsAndConditions { get; set; } = string.Empty;
}