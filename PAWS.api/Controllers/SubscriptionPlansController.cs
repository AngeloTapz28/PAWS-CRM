using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.domain.Constants;
using PAWS.domain.Entities;
using PAWS.infrastructure.Data;

namespace PAWS.api.Controllers;

// Super Admin only. Plans live in the MASTER database, so this controller
// uses MasterErpDbContext directly and never touches a tenant database.
[Authorize(Roles = RoleNames.SuperAdmin)]
[ApiController]
[Route("api/subscription-plans")]
public class SubscriptionPlansController : ControllerBase
{
    private readonly MasterErpDbContext _masterDb;

    public SubscriptionPlansController(MasterErpDbContext masterDb)
    {
        _masterDb = masterDb;
    }

    // GET /api/subscription-plans
    // Lists ALL plans (active and inactive) so Super Admin can reactivate one.
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var plans = await _masterDb.SubscriptionPlans
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var counts = await _masterDb.Companies
            .GroupBy(c => c.SubscriptionPlanId)
            .Select(g => new { PlanId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var result = plans
            .Select(p => ToDto(p, counts.FirstOrDefault(c => c.PlanId == p.SubscriptionPlanId)?.Count ?? 0))
            .ToList();

        return Ok(result);
    }

    // GET /api/subscription-plans/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var plan = await _masterDb.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.SubscriptionPlanId == id, cancellationToken);
        if (plan is null) return NotFound();

        var companyCount = await _masterDb.Companies
            .CountAsync(c => c.SubscriptionPlanId == id, cancellationToken);

        return Ok(ToDto(plan, companyCount));
    }

    // POST /api/subscription-plans
    [HttpPost]
    public async Task<IActionResult> Create(CreateSubscriptionPlanRequestDto request, CancellationToken cancellationToken)
    {
        var error = Validate(request.Name, request.Price, request.BranchLimit, request.TermsAndConditions);
        if (error is not null) return BadRequest(new { message = error });

        var name = request.Name.Trim();

        var nameTaken = await _masterDb.SubscriptionPlans
            .AnyAsync(p => p.Name == name, cancellationToken);
        if (nameTaken)
        {
            return BadRequest(new { message = "A subscription plan with that name already exists." });
        }

        var now = DateTime.UtcNow;

        var plan = new SubscriptionPlan
        {
            Name = name,
            Price = request.Price,
            BranchLimit = request.BranchLimit,
            TermsAndConditions = request.TermsAndConditions.Trim(),
            TermsUpdatedAt = now,
            IsActive = true,
            CreatedAt = now
        };

        _masterDb.SubscriptionPlans.Add(plan);
        await _masterDb.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(plan, 0));
    }

    // PUT /api/subscription-plans/{id}
    // Companies on this plan see the new price/limit/terms immediately,
    // because they all point at this one row. If the TERMS TEXT changed,
    // TermsUpdatedAt moves to now, which makes every company on this plan
    // accept the new terms again. Re-saving identical text changes nothing.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateSubscriptionPlanRequestDto request, CancellationToken cancellationToken)
    {
        var error = Validate(request.Name, request.Price, request.BranchLimit, request.TermsAndConditions);
        if (error is not null) return BadRequest(new { message = error });

        var plan = await _masterDb.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.SubscriptionPlanId == id, cancellationToken);
        if (plan is null) return NotFound();

        var name = request.Name.Trim();

        var nameTaken = await _masterDb.SubscriptionPlans
            .AnyAsync(p => p.SubscriptionPlanId != id && p.Name == name, cancellationToken);
        if (nameTaken)
        {
            return BadRequest(new { message = "A subscription plan with that name already exists." });
        }

        var newTerms = request.TermsAndConditions.Trim();
        var termsChanged = !string.Equals(plan.TermsAndConditions?.Trim(), newTerms, StringComparison.Ordinal);

        plan.Name = name;
        plan.Price = request.Price;
        plan.BranchLimit = request.BranchLimit;
        plan.TermsAndConditions = newTerms;

        if (termsChanged)
        {
            plan.TermsUpdatedAt = DateTime.UtcNow;
        }

        await _masterDb.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto
        {
            Message = termsChanged
                ? $"{plan.Name} plan updated. Companies on this plan must accept the new terms."
                : $"{plan.Name} plan updated."
        });
    }

    // POST /api/subscription-plans/{id}/deactivate
    // Blocked while any company still uses the plan, the same idea as
    // a branch that cannot be deactivated while it has active pets.
    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        var plan = await _masterDb.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.SubscriptionPlanId == id, cancellationToken);
        if (plan is null) return NotFound();

        var companiesUsingPlan = await _masterDb.Companies
            .CountAsync(c => c.SubscriptionPlanId == id, cancellationToken);
        if (companiesUsingPlan > 0)
        {
            return BadRequest(new
            {
                message = $"{companiesUsingPlan} company(ies) are still on the {plan.Name} plan. " +
                          "Move them to another plan first."
            });
        }

        plan.IsActive = false;
        await _masterDb.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = $"{plan.Name} plan deactivated." });
    }

    // POST /api/subscription-plans/{id}/activate
    [HttpPost("{id:int}/activate")]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
    {
        var plan = await _masterDb.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.SubscriptionPlanId == id, cancellationToken);
        if (plan is null) return NotFound();

        plan.IsActive = true;
        await _masterDb.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = $"{plan.Name} plan activated." });
    }

    // Shared validation for create and update. Returns an error message, or null if valid.
    private static string? Validate(string name, decimal price, int branchLimit, string terms)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Plan name is required.";
        if (name.Trim().Length > 100) return "Plan name must be 100 characters or fewer.";
        if (price < 0) return "Price cannot be negative.";
        if (price > 9_999_999m) return "Price is too large.";
        if (branchLimit < 1) return "Branch limit must be at least 1.";
        if (string.IsNullOrWhiteSpace(terms)) return "Terms and conditions are required.";
        return null;
    }

    private static SubscriptionPlanDto ToDto(SubscriptionPlan p, int companyCount) => new()
    {
        SubscriptionPlanId = p.SubscriptionPlanId,
        Name = p.Name,
        Price = p.Price,
        BranchLimit = p.BranchLimit,
        TermsAndConditions = p.TermsAndConditions,
        IsActive = p.IsActive,
        CreatedAt = p.CreatedAt,
        CompanyCount = companyCount
    };
}