namespace PAWS.api.Dtos;

public class CompanyDto
{
    public int CompanyId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int SubscriptionPlanId { get; set; }
    public string SubscriptionPlan { get; set; } = string.Empty;   // the plan's NAME
    public int BranchLimit { get; set; }
    public DateTime SubscriptionStartDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ServerName { get; set; }
    public string? DatabaseName { get; set; }
    public string? CredentialKey { get; set; }
    public bool? DatabaseIsActive { get; set; }

    // Terms acknowledgment (true only if the CURRENT terms of the CURRENT plan were accepted).
    public bool TermsAccepted { get; set; }
    public DateTime? TermsAcceptedAt { get; set; }
    public string? TermsAcceptedBy { get; set; }
}

public class SetSubscriptionPlanRequestDto
{
    public int SubscriptionPlanId { get; set; }
}

public class MyPlanDto
{
    public string SubscriptionPlan { get; set; } = string.Empty;
    public int BranchLimit { get; set; }
    public int CurrentBranchCount { get; set; }

    public decimal Price { get; set; }                       // PHP per 30-day cycle
    public string TermsAndConditions { get; set; } = string.Empty;
    public DateTime SubscriptionStartDate { get; set; }
    public int RemainingDays { get; set; }                   // informational only, never below 0

    // New: terms acknowledgment status for this company.
    public bool TermsAccepted { get; set; }
    public DateTime? TermsAcceptedAt { get; set; }
    public string? TermsAcceptedBy { get; set; }
}

public class UpdateCompanyRequestDto
{
    public string CompanyName { get; set; } = string.Empty;
}

public class SystemStatsDto
{
    public int TotalCompanies { get; set; }
    public int ActiveCompanies { get; set; }
    public int TotalUsersAcrossAllCompanies { get; set; }
    public List<CompanyUserCountDto> UsersByCompany { get; set; } = new();
}

public class CompanyUserCountDto
{
    public string CompanyCode { get; set; } = string.Empty;
    public int UserCount { get; set; }
}

public class SuperAdminBiDto
{
    public List<CompanyMetricDto> PetsByCompany { get; set; } = new();
    public List<CompanyMetricDto> UsersByCompany { get; set; } = new();
    public List<CompanyMetricDto> CompletedAdoptionsByCompany { get; set; } = new();
    public List<CompanyMetricDto> IncomeByCompany { get; set; } = new();
    public List<CompanyMetricDto> BranchesByCompany { get; set; } = new();
}

public class CompanyMetricDto
{
    public string CompanyCode { get; set; } = string.Empty;
    public decimal Value { get; set; }
}