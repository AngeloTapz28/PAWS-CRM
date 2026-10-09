using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

// Only roles the WinUI menu actually shows Dashboard to may call this.
// An Adopter's token is valid (passes authentication) but not authorized
// (fails this role check), so it gets 403, not 401.
[Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff},{RoleNames.Veterinarian}")]
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;

    public DashboardController(
        ICurrentCompanyService currentCompany,
        ITenantDbContextFactory tenantFactory)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
    }

    // GET /api/dashboard/summary
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var summary = new DashboardSummaryDto
        {
            TotalPets = await db.Pets.CountAsync(cancellationToken),

            AvailablePets = await db.Pets.CountAsync(
                p => p.Status == PetStatuses.Available, cancellationToken),

            AdoptedPets = await db.Pets.CountAsync(
                p => p.Status == PetStatuses.Adopted, cancellationToken),

            PendingApplications = await db.AdoptionApplications.CountAsync(
                a => ApplicationStatuses.AwaitingDecision.Contains(a.ApplicationStatus),
                cancellationToken),

            ApprovedApplications = await db.AdoptionApplications.CountAsync(
                a => a.ApplicationStatus == ApplicationStatuses.Approved,
                cancellationToken),

            TotalCustomers = await db.RoleUsers.CountAsync(
                ru => ru.Role != null && ru.Role.Name == RoleNames.Adopter,
                cancellationToken),

            CustomerServiceRequestsTotal = await db.CustomerServiceRequests
                .CountAsync(cancellationToken),

            CustomerServiceRequestsOpen = await db.CustomerServiceRequests.CountAsync(
                r => RequestStatuses.Open.Contains(r.Status), cancellationToken)
        };

        summary.TotalPaymentsCompleted = await db.Payments
            .Where(p => p.Status == PaymentStatuses.Completed && p.DeletedAt == null)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        return Ok(summary);
    }
}