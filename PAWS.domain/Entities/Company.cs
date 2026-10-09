using System;

namespace PAWS.domain.Entities;

/// <summary>
/// One pet adoption organization (a tenant). Each Company has its own
/// separate tenant database. This entity lives in the MASTER database.
/// </summary>
public class Company
{
    public int CompanyId { get; set; }

    // A short unique code such as "PAWS-A". Made unique in MasterErpDbContext.
    public string CompanyCode { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Real foreign key to SubscriptionPlans. Defaults to 1 (the seeded "Free"
    // plan) so any code that creates a Company without choosing a plan
    // (bootstrap, seeding) keeps the old "default is Free" behavior.
    public int SubscriptionPlanId { get; set; } = 1;

    public SubscriptionPlan? SubscriptionPlan { get; set; }

    // Reset to now every time Super Admin assigns or changes the plan.
    // Used for the informational 30-day "remaining days" countdown.
    public DateTime SubscriptionStartDate { get; set; } = DateTime.UtcNow;

    // ----- Terms and conditions acknowledgment record -----
    // All three are null until the company's Administrator accepts.
    // TermsAcceptedPlanId is a plain number (not a foreign key) on purpose:
    // it only records WHICH plan's terms were accepted.
    public DateTime? TermsAcceptedAt { get; set; }
    public string? TermsAcceptedByName { get; set; }
    public int? TermsAcceptedPlanId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}