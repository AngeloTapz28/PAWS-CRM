using System;
using System.Collections.Generic;

namespace PAWS.winui.Models;

public class CompanyItem
{
    public int CompanyId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int SubscriptionPlanId { get; set; }
    public string SubscriptionPlan { get; set; } = string.Empty;
    public int BranchLimit { get; set; }
    public DateTime SubscriptionStartDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ServerName { get; set; }
    public string? DatabaseName { get; set; }
    public string? CredentialKey { get; set; }
    public bool? DatabaseIsActive { get; set; }

    // Terms acknowledgment (matches the API's CompanyDto).
    public bool TermsAccepted { get; set; }
    public DateTime? TermsAcceptedAt { get; set; }
    public string? TermsAcceptedBy { get; set; }
}

public class UpdateCompanyRequest
{
    public string CompanyName { get; set; } = string.Empty;
}

// No longer used: the Change Plan popup now sends { subscriptionPlanId }.
public class SetSubscriptionPlanRequest
{
    public string SubscriptionPlan { get; set; } = string.Empty;
}

public class MyPlan
{
    public string SubscriptionPlan { get; set; } = string.Empty;
    public int BranchLimit { get; set; }
    public int CurrentBranchCount { get; set; }

    public decimal Price { get; set; }
    public string TermsAndConditions { get; set; } = string.Empty;
    public DateTime SubscriptionStartDate { get; set; }
    public int RemainingDays { get; set; }

    // Terms acknowledgment (matches the API's MyPlanDto).
    public bool TermsAccepted { get; set; }
    public DateTime? TermsAcceptedAt { get; set; }
    public string? TermsAcceptedBy { get; set; }
}

public class SystemStats
{
    public int TotalCompanies { get; set; }
    public int ActiveCompanies { get; set; }
    public int TotalUsersAcrossAllCompanies { get; set; }
    public List<CompanyUserCount> UsersByCompany { get; set; } = new();
}

public class CompanyUserCount
{
    public string CompanyCode { get; set; } = string.Empty;
    public int UserCount { get; set; }
}
public class SuperAdminBi
{
    public List<CompanyMetric> PetsByCompany { get; set; } = new();
    public List<CompanyMetric> UsersByCompany { get; set; } = new();
    public List<CompanyMetric> CompletedAdoptionsByCompany { get; set; } = new();
    public List<CompanyMetric> IncomeByCompany { get; set; } = new();
    public List<CompanyMetric> BranchesByCompany { get; set; } = new();
}

public class CompanyMetric
{
    public string CompanyCode { get; set; } = string.Empty;
    public decimal Value { get; set; }
}