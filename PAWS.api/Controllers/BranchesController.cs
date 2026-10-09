using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.domain.Entities;
using PAWS.infrastructure.Data;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

// Any authenticated user can LIST branches (needed so Veterinarians and
// Adopters can filter pets by branch); only Administrator/Staff can
// create, edit, activate, or deactivate a branch — enforced per-action below.
[Authorize]
[ApiController]
[Route("api/branches")]
public class BranchesController : ControllerBase
{
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;
    private readonly MasterErpDbContext _masterDb;

    public BranchesController(ICurrentCompanyService currentCompany, ITenantDbContextFactory tenantFactory, MasterErpDbContext masterDb)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
        _masterDb = masterDb;
    }


    // GET /api/branches/my-plan
    // Lets a tenant's own Administrator/Staff see their company's current
    // plan, price, terms, branch usage, the informational 30-day countdown,
    // and whether the current terms have been accepted.
    [HttpGet("my-plan")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> GetMyPlan(CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        var company = await _masterDb.Companies
            .Include(c => c.SubscriptionPlan)
            .FirstOrDefaultAsync(c => c.CompanyId == companyId.Value, cancellationToken);
        if (company is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);
        var currentBranchCount = await db.Branches.CountAsync(cancellationToken);

        // 30-day cycle, purely informational: no auto-downgrade, no blocking.
        // Clamped at 0 so a tenant never sees a negative number.
        var daysElapsed = (DateTime.UtcNow - company.SubscriptionStartDate).Days;
        var remainingDays = Math.Max(0, 30 - daysElapsed);

        var accepted = CompaniesController.HasAcceptedCurrentTerms(company);

        return Ok(new MyPlanDto
        {
            SubscriptionPlan = company.SubscriptionPlan?.Name ?? string.Empty,
            BranchLimit = company.SubscriptionPlan?.BranchLimit ?? 1,
            CurrentBranchCount = currentBranchCount,
            Price = company.SubscriptionPlan?.Price ?? 0m,
            TermsAndConditions = company.SubscriptionPlan?.TermsAndConditions ?? string.Empty,
            SubscriptionStartDate = company.SubscriptionStartDate,
            RemainingDays = remainingDays,
            TermsAccepted = accepted,
            TermsAcceptedAt = accepted ? company.TermsAcceptedAt : null,
            TermsAcceptedBy = accepted ? company.TermsAcceptedByName : null
        });
    }

    // POST /api/branches/accept-terms
    // Administrator only: the Administrator speaks for the company. Records
    // an acknowledgment of the company's CURRENT plan terms. (This is a
    // simple acknowledgment record, not a legally binding e-signature.)
    [HttpPost("accept-terms")]
    [Authorize(Roles = RoleNames.Administrator)]
    public async Task<IActionResult> AcceptTerms(CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        var company = await _masterDb.Companies
            .Include(c => c.SubscriptionPlan)
            .FirstOrDefaultAsync(c => c.CompanyId == companyId.Value, cancellationToken);
        if (company is null || company.SubscriptionPlan is null) return Unauthorized();

        var name = User.FindFirst("name")?.Value
                   ?? User.FindFirst(ClaimTypes.Name)?.Value
                   ?? "Administrator";

        company.TermsAcceptedAt = DateTime.UtcNow;
        company.TermsAcceptedByName = name.Length > 200 ? name[..200] : name;
        company.TermsAcceptedPlanId = company.SubscriptionPlanId;

        await _masterDb.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto
        {
            Message = $"Thank you. The {company.SubscriptionPlan.Name} plan terms were accepted by {company.TermsAcceptedByName}."
        });
    }

    // GET /api/branches
    [HttpGet]
    public async Task<IActionResult> GetBranches(CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var branches = await db.Branches
            .OrderBy(b => b.BranchName)
            .ToListAsync(cancellationToken);

        var petCounts = await db.Pets
            .Where(p => p.IsActive)
            .GroupBy(p => p.BranchId)
            .Select(g => new { BranchId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var result = branches.Select(b => new BranchDto
        {
            BranchId = b.BranchId,
            BranchName = b.BranchName,
            Address = b.Address,
            IsActive = b.IsActive,
            CreatedAt = b.CreatedAt,
            PetCount = petCounts.FirstOrDefault(p => p.BranchId == b.BranchId)?.Count ?? 0
        }).ToList();

        return Ok(result);
    }

    // POST /api/branches
    [HttpPost]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> Create(CreateBranchRequestDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.BranchName))
        {
            return BadRequest(new { message = "Branch name is required." });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        // Enforce the branch limit tied to this company's subscription plan.
        // Looked up from the MASTER database, since plan/billing information
        // lives there, not in the tenant database this controller otherwise
        // operates on. The limit comes from the real SubscriptionPlan row.
        var company = await _masterDb.Companies
            .Include(c => c.SubscriptionPlan)
            .FirstOrDefaultAsync(c => c.CompanyId == companyId.Value, cancellationToken);
        if (company is null)
        {
            return Unauthorized();
        }

        // The company's Administrator must accept the current plan terms first.
        // This is the ONLY thing the terms acceptance blocks.
        if (!CompaniesController.HasAcceptedCurrentTerms(company))
        {
            return BadRequest(new
            {
                message = "Your company's Administrator must accept the current subscription terms " +
                          "(Branches page, View Terms) before new branches can be added."
            });
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var currentBranchCount = await db.Branches.CountAsync(cancellationToken);
        var branchLimit = company.SubscriptionPlan?.BranchLimit ?? 1;
        var planName = company.SubscriptionPlan?.Name ?? "current";

        if (currentBranchCount >= branchLimit)
        {
            return BadRequest(new
            {
                message = $"Your company's {planName} plan allows up to {branchLimit} branch(es). " +
                           "Upgrade your plan to add more."
            });
        }

        var nameTaken = await db.Branches.AnyAsync(b => b.BranchName == dto.BranchName.Trim(), cancellationToken);
        if (nameTaken)
        {
            return BadRequest(new { message = "A branch with that name already exists." });
        }

        var branch = new Branch
        {
            BranchName = dto.BranchName.Trim(),
            Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim()
        };

        db.Branches.Add(branch);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Branch created." });
    }

    // PUT /api/branches/{id}
    [HttpPut("{id:int}")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> Update(int id, UpdateBranchRequestDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.BranchName))
        {
            return BadRequest(new { message = "Branch name is required." });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var branch = await db.Branches.FirstOrDefaultAsync(b => b.BranchId == id, cancellationToken);
        if (branch is null) return NotFound();

        var nameTaken = await db.Branches.AnyAsync(
            b => b.BranchId != id && b.BranchName == dto.BranchName.Trim(), cancellationToken);
        if (nameTaken)
        {
            return BadRequest(new { message = "A branch with that name already exists." });
        }

        branch.BranchName = dto.BranchName.Trim();
        branch.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();
        branch.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Branch updated." });
    }

    // POST /api/branches/{id}/deactivate
    [HttpPost("{id:int}/deactivate")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var branch = await db.Branches.FirstOrDefaultAsync(b => b.BranchId == id, cancellationToken);
        if (branch is null) return NotFound();

        var hasActivePets = await db.Pets.AnyAsync(p => p.BranchId == id && p.IsActive, cancellationToken);
        if (hasActivePets)
        {
            return BadRequest(new { message = "This branch still has active pets assigned to it. Move them to another branch first." });
        }

        branch.IsActive = false;
        branch.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Branch deactivated." });
    }

    // POST /api/branches/{id}/activate
    [HttpPost("{id:int}/activate")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var branch = await db.Branches.FirstOrDefaultAsync(b => b.BranchId == id, cancellationToken);
        if (branch is null) return NotFound();

        branch.IsActive = true;
        branch.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Branch activated." });
    }
}