using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.domain.Constants;
using PAWS.infrastructure.Data;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

// Super Admin only. This controller reads/writes the MASTER database
// directly — it never touches any tenant database except in GetSystemStats,
// where it deliberately queries across ALL tenants at once, since that is
// exactly the kind of platform-wide view only a Super Admin should see.
[Authorize(Roles = RoleNames.SuperAdmin)]
[ApiController]
[Route("api/companies")]
public class CompaniesController : ControllerBase
{
    private readonly MasterErpDbContext _masterDb;
    private readonly ITenantDbContextFactory _tenantFactory;

    public CompaniesController(MasterErpDbContext masterDb, ITenantDbContextFactory tenantFactory)
    {
        _masterDb = masterDb;
        _tenantFactory = tenantFactory;
    }

    // GET /api/companies
    [HttpGet]
    public async Task<IActionResult> GetCompanies(CancellationToken cancellationToken)
    {
        var companies = await _masterDb.Companies
            .Include(c => c.SubscriptionPlan)
            .OrderBy(c => c.CompanyCode)
            .ToListAsync(cancellationToken);

        var databases = await _masterDb.CompanyDatabases.ToListAsync(cancellationToken);

        var result = companies.Select(c => ToDto(c, databases.FirstOrDefault(d => d.CompanyId == c.CompanyId))).ToList();

        return Ok(result);
    }

    // GET /api/companies/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCompany(int id, CancellationToken cancellationToken)
    {
        var company = await _masterDb.Companies
            .Include(c => c.SubscriptionPlan)
            .FirstOrDefaultAsync(c => c.CompanyId == id, cancellationToken);
        if (company is null) return NotFound();

        var database = await _masterDb.CompanyDatabases.FirstOrDefaultAsync(d => d.CompanyId == id, cancellationToken);

        return Ok(ToDto(company, database));
    }

    // PUT /api/companies/{id}
    // Updates only CompanyName. CompanyCode is never editable here (it is used
    // in every login request and throughout the system), and IsActive is
    // governed exclusively by the dedicated activate/deactivate actions below.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateCompany(int id, UpdateCompanyRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName))
        {
            return BadRequest(new { message = "Company name is required." });
        }

        var company = await _masterDb.Companies.FirstOrDefaultAsync(c => c.CompanyId == id, cancellationToken);
        if (company is null) return NotFound();

        company.CompanyName = request.CompanyName.Trim();
        await _masterDb.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Company updated." });
    }

    // POST /api/companies/{id}/subscription-plan
    // Assigns a company to one of the real SubscriptionPlans, restarts its
    // 30-day cycle, and clears any earlier terms acceptance (the company is
    // now under different terms). The plan must exist and be active.
    [HttpPost("{id:int}/subscription-plan")]
    public async Task<IActionResult> SetSubscriptionPlan(int id, SetSubscriptionPlanRequestDto request, CancellationToken cancellationToken)
    {
        var plan = await _masterDb.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.SubscriptionPlanId == request.SubscriptionPlanId, cancellationToken);

        if (plan is null)
        {
            return BadRequest(new { message = "That subscription plan does not exist." });
        }

        if (!plan.IsActive)
        {
            return BadRequest(new { message = $"The {plan.Name} plan is inactive and cannot be assigned." });
        }

        var company = await _masterDb.Companies.FirstOrDefaultAsync(c => c.CompanyId == id, cancellationToken);
        if (company is null) return NotFound();

        company.SubscriptionPlanId = plan.SubscriptionPlanId;
        company.SubscriptionStartDate = DateTime.UtcNow;

        company.TermsAcceptedAt = null;
        company.TermsAcceptedByName = null;
        company.TermsAcceptedPlanId = null;

        await _masterDb.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto
        {
            Message = $"{company.CompanyCode}'s plan set to {plan.Name}. The company must accept the plan's terms again."
        });
    }

    // POST /api/companies/{id}/activate
    [HttpPost("{id:int}/activate")]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
    {
        var company = await _masterDb.Companies.FirstOrDefaultAsync(c => c.CompanyId == id, cancellationToken);
        if (company is null) return NotFound();

        company.IsActive = true;
        await _masterDb.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = $"{company.CompanyCode} activated." });
    }

    // POST /api/companies/{id}/deactivate
    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        var company = await _masterDb.Companies.FirstOrDefaultAsync(c => c.CompanyId == id, cancellationToken);
        if (company is null) return NotFound();

        company.IsActive = false;
        await _masterDb.SaveChangesAsync(cancellationToken);

        // Known limitation: any user already signed in to this company keeps
        // working until their JWT expires or they close the app. There is no
        // server-side session store to forcibly revoke an issued token, so
        // deactivation only blocks NEW logins from this point on.
        return Ok(new SimpleMessageDto
        {
            Message = $"{company.CompanyCode} deactivated. New logins are now blocked; " +
                      "users already signed in will remain active until their session ends."
        });
    }

    // GET /api/companies/super-admin-bi
    // Cross-company comparisons for the Super Admin's own Business
    // Intelligence view, distinct from each tenant's own per-company
    // Reports. Loops across every tenant database, same technique as
    // GetSystemStats; a tenant that cannot be reached is skipped for
    // that one company's numbers rather than failing the whole request.
    [HttpGet("super-admin-bi")]
    public async Task<IActionResult> GetSuperAdminBi(CancellationToken cancellationToken)
    {
        var companies = await _masterDb.Companies.ToListAsync(cancellationToken);

        var result = new SuperAdminBiDto();

        foreach (var company in companies)
        {
            try
            {
                await using var db = await _tenantFactory.CreateAsync(company.CompanyId);

                var petCount = await db.Pets.CountAsync(p => p.IsActive, cancellationToken);
                var userCount = await db.Users.CountAsync(cancellationToken);
                var completedAdoptions = await db.AdoptionApplications
                    .CountAsync(a => a.ApplicationStatus == "Completed" && a.DeletedAt == null, cancellationToken);
                var income = await db.Payments
                    .Where(p => p.Status == "Completed" && p.DeletedAt == null)
                    .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
                var branchCount = await db.Branches.CountAsync(b => b.IsActive, cancellationToken);

                result.PetsByCompany.Add(new CompanyMetricDto { CompanyCode = company.CompanyCode, Value = petCount });
                result.UsersByCompany.Add(new CompanyMetricDto { CompanyCode = company.CompanyCode, Value = userCount });
                result.CompletedAdoptionsByCompany.Add(new CompanyMetricDto { CompanyCode = company.CompanyCode, Value = completedAdoptions });
                result.IncomeByCompany.Add(new CompanyMetricDto { CompanyCode = company.CompanyCode, Value = income });
                result.BranchesByCompany.Add(new CompanyMetricDto { CompanyCode = company.CompanyCode, Value = branchCount });
            }
            catch (InvalidOperationException)
            {
                result.PetsByCompany.Add(new CompanyMetricDto { CompanyCode = company.CompanyCode, Value = 0 });
                result.UsersByCompany.Add(new CompanyMetricDto { CompanyCode = company.CompanyCode, Value = 0 });
                result.CompletedAdoptionsByCompany.Add(new CompanyMetricDto { CompanyCode = company.CompanyCode, Value = 0 });
                result.IncomeByCompany.Add(new CompanyMetricDto { CompanyCode = company.CompanyCode, Value = 0 });
                result.BranchesByCompany.Add(new CompanyMetricDto { CompanyCode = company.CompanyCode, Value = 0 });
            }
        }

        return Ok(result);
    }

    // GET /api/companies/system-stats
    // The one place a Super Admin sees a number that spans every tenant at
    // once. Each tenant database is queried in turn; a tenant whose database
    // cannot be reached is simply skipped for the user count rather than
    // failing the whole request, so one bad connection cannot hide every
    // company's data from the Super Admin.
    [HttpGet("system-stats")]
    public async Task<IActionResult> GetSystemStats(CancellationToken cancellationToken)
    {
        var companies = await _masterDb.Companies.ToListAsync(cancellationToken);

        var usersByCompany = new List<CompanyUserCountDto>();
        var totalUsers = 0;

        foreach (var company in companies)
        {
            try
            {
                await using var db = await _tenantFactory.CreateAsync(company.CompanyId);
                var userCount = await db.Users.CountAsync(cancellationToken);
                usersByCompany.Add(new CompanyUserCountDto { CompanyCode = company.CompanyCode, UserCount = userCount });
                totalUsers += userCount;
            }
            catch (InvalidOperationException)
            {
                // This tenant's database could not be reached (e.g. its
                // CompanyDatabase row is missing or inactive). Skip it rather
                // than failing the whole stats request.
                usersByCompany.Add(new CompanyUserCountDto { CompanyCode = company.CompanyCode, UserCount = 0 });
            }
        }

        return Ok(new SystemStatsDto
        {
            TotalCompanies = companies.Count,
            ActiveCompanies = companies.Count(c => c.IsActive),
            TotalUsersAcrossAllCompanies = totalUsers,
            UsersByCompany = usersByCompany
        });
    }

    // A company has accepted the CURRENT terms only if ALL of these hold:
    //  - it has accepted something at all,
    //  - what it accepted was its CURRENT plan,
    //  - the acceptance is newer than the last edit of that plan's terms.
    // Needs company.SubscriptionPlan to be loaded (.Include).
    // Public and static so BranchesController can use the same rule.
    public static bool HasAcceptedCurrentTerms(PAWS.domain.Entities.Company c) =>
        c.TermsAcceptedAt is not null
        && c.SubscriptionPlan is not null
        && c.TermsAcceptedPlanId == c.SubscriptionPlanId
        && c.TermsAcceptedAt >= c.SubscriptionPlan.TermsUpdatedAt;

    // Any query that calls ToDto() must .Include(c => c.SubscriptionPlan).
    private static CompanyDto ToDto(PAWS.domain.Entities.Company c, PAWS.domain.Entities.CompanyDatabase? db)
    {
        var accepted = HasAcceptedCurrentTerms(c);

        return new CompanyDto
        {
            CompanyId = c.CompanyId,
            CompanyCode = c.CompanyCode,
            CompanyName = c.CompanyName,
            IsActive = c.IsActive,
            SubscriptionPlanId = c.SubscriptionPlanId,
            SubscriptionPlan = c.SubscriptionPlan?.Name ?? string.Empty,
            BranchLimit = c.SubscriptionPlan?.BranchLimit ?? 1,
            SubscriptionStartDate = c.SubscriptionStartDate,
            CreatedAt = c.CreatedAt,
            ServerName = db?.ServerName,
            DatabaseName = db?.DatabaseName,
            CredentialKey = db?.CredentialKey,
            DatabaseIsActive = db?.IsActive,
            TermsAccepted = accepted,
            TermsAcceptedAt = accepted ? c.TermsAcceptedAt : null,
            TermsAcceptedBy = accepted ? c.TermsAcceptedByName : null
        };
    }
}