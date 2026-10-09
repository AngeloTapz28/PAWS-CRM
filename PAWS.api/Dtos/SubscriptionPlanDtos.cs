namespace PAWS.api.Dtos;

public class SubscriptionPlanDto
{
    public int SubscriptionPlanId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int BranchLimit { get; set; }          // int.MaxValue (2147483647) means unlimited
    public string TermsAndConditions { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CompanyCount { get; set; }         // how many companies currently use this plan
}

public class CreateSubscriptionPlanRequestDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int BranchLimit { get; set; }
    public string TermsAndConditions { get; set; } = string.Empty;
}

public class UpdateSubscriptionPlanRequestDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int BranchLimit { get; set; }
    public string TermsAndConditions { get; set; } = string.Empty;
}