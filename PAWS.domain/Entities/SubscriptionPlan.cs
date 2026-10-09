using System;

namespace PAWS.domain.Entities;

/// <summary>
/// A subscription plan a Super Admin can offer to companies (tenants).
/// Lives in the MASTER database. Soft-deleted through IsActive,
/// the same pattern as Company.IsActive.
/// </summary>
public class SubscriptionPlan
{
    public int SubscriptionPlanId { get; set; }

    // Unique. Super Admin can create any plan name, not only Free/Basic/Premium.
    public string Name { get; set; } = string.Empty;

    // PHP per month. 0 means free.
    public decimal Price { get; set; }

    // Maximum active branches a company on this plan may have.
    // int.MaxValue is used to mean "unlimited".
    public int BranchLimit { get; set; }

    public string TermsAndConditions { get; set; } = string.Empty;

    // Set whenever the terms text changes. A company's acceptance only counts
    // if it happened on or after this moment.
    public DateTime TermsUpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}