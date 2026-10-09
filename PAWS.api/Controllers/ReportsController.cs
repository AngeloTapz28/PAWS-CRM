using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

// Administrator ONLY, matching the approved use case diagram (Staff does not
// have "View Reports").
[Authorize(Roles = RoleNames.Administrator)]
[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;

    public ReportsController(
        ICurrentCompanyService currentCompany,
        ITenantDbContextFactory tenantFactory)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
    }

    // GET /api/reports/bi-summary
    [HttpGet("bi-summary")]
    public async Task<IActionResult> GetBiSummary(CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        // 1. Applications by status.
        var appStatusCounts = await db.AdoptionApplications
            .GroupBy(a => a.ApplicationStatus)
            .Select(g => new KpiDataPointDto { Label = g.Key, Value = g.Count() })
            .ToListAsync(cancellationToken);

        var applicationsByStatus = new KpiChartDto
        {
            Title = "Applications by Status",
            DataPoints = FillMissing(appStatusCounts, ApplicationStatuses.All)
        };

        // 2. Pets by status (active pets only).
        var petStatusCounts = await db.Pets
            .Where(p => p.IsActive)
            .GroupBy(p => p.Status)
            .Select(g => new KpiDataPointDto { Label = g.Key, Value = g.Count() })
            .ToListAsync(cancellationToken);

        var petsByStatus = new KpiChartDto
        {
            Title = "Pets by Status",
            DataPoints = FillMissing(petStatusCounts, PetStatuses.All)
        };

        // 3. Approved vs Rejected (simple two-bar comparison).
        var approvedCount = await db.AdoptionApplications
            .CountAsync(a => a.ApplicationStatus == ApplicationStatuses.Approved || a.ApplicationStatus == ApplicationStatuses.Completed, cancellationToken);
        var rejectedCount = await db.AdoptionApplications
            .CountAsync(a => a.ApplicationStatus == ApplicationStatuses.Rejected, cancellationToken);

        var approvedVsRejected = new KpiChartDto
        {
            Title = "Approved vs Rejected Applications",
            DataPoints = new List<KpiDataPointDto>
            {
                new() { Label = "Approved", Value = approvedCount },
                new() { Label = "Rejected", Value = rejectedCount }
            }
        };

        // 4. Customer service requests by type.
        var typeCounts = await db.CustomerServiceRequests
            .GroupBy(r => r.Type)
            .Select(g => new KpiDataPointDto { Label = g.Key, Value = g.Count() })
            .ToListAsync(cancellationToken);

        var serviceRequestsByType = new KpiChartDto
        {
            Title = "Customer Service Requests by Type",
            DataPoints = FillMissing(typeCounts, RequestTypes.All)
        };

        // 5. Customer service requests by status.
        var statusCounts = await db.CustomerServiceRequests
            .GroupBy(r => r.Status)
            .Select(g => new KpiDataPointDto { Label = g.Key, Value = g.Count() })
            .ToListAsync(cancellationToken);

        var serviceRequestsByStatus = new KpiChartDto
        {
            Title = "Customer Service Requests by Status",
            DataPoints = FillMissing(statusCounts, RequestStatuses.All)
        };

        // 6. Total income (completed payments only).
        var totalIncome = await db.Payments
            .Where(p => p.Status == PaymentStatuses.Completed && p.DeletedAt == null)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        // 7. Payments by type / by status.
        var paymentTypeGroups = await db.Payments
            .Where(p => p.DeletedAt == null)
            .GroupBy(p => p.Type)
            .Select(g => new KpiDataPointDto { Label = g.Key, Value = g.Count() })
            .ToListAsync(cancellationToken);

        var paymentStatusGroups = await db.Payments
            .Where(p => p.DeletedAt == null)
            .GroupBy(p => p.Status)
            .Select(g => new KpiDataPointDto { Label = g.Key, Value = g.Count() })
            .ToListAsync(cancellationToken);

        var paymentsByType = new KpiChartDto
        {
            Title = "Payments by Type",
            DataPoints = FillMissing(paymentTypeGroups, new[] { "Adoption Fee", "Donation", "Medical Fee", "Other" })
        };

        var paymentsByStatus = new KpiChartDto
        {
            Title = "Payments by Status",
            DataPoints = FillMissing(paymentStatusGroups, PaymentStatuses.All)
        };

        // 8. Distinct pets with at least one medical record marked fit for adoption.
        var petsFitForAdoptionCount = await db.MedicalRecords
            .Where(r => r.DeletedAt == null && r.FitForAdoption)
            .Select(r => r.PetId)
            .Distinct()
            .CountAsync(cancellationToken);

        // 9. Adoptions (Completed applications) by branch, via Application -> Pet -> Branch.
        var adoptionsByBranchRaw = await db.AdoptionApplications
            .Where(a => a.ApplicationStatus == ApplicationStatuses.Completed && a.DeletedAt == null)
            .Join(db.Pets, a => a.PetId, p => p.PetId, (a, p) => p.BranchId)
            .Join(db.Branches, branchId => branchId, b => b.BranchId, (branchId, b) => b.BranchName)
            .GroupBy(branchName => branchName)
            .Select(g => new KpiDataPointDto { Label = g.Key, Value = g.Count() })
            .ToListAsync(cancellationToken);

        var allBranchNames = await db.Branches
            .Where(b => b.IsActive)
            .Select(b => b.BranchName)
            .ToListAsync(cancellationToken);

        var adoptionsByBranch = new KpiChartDto
        {
            Title = "Adoptions by Branch",
            DataPoints = FillMissing(adoptionsByBranchRaw, allBranchNames.ToArray())
        };

        // 10. Income (Completed payments) by branch, via Payment -> Application -> Pet -> Branch.
        var incomeByBranchRaw = await db.Payments
            .Where(p => p.Status == PaymentStatuses.Completed && p.DeletedAt == null)
            .Join(db.AdoptionApplications, p => p.AdoptionApplicationId, a => a.AdoptionApplicationId, (p, a) => new { p.Amount, a.PetId })
            .Join(db.Pets, x => x.PetId, pet => pet.PetId, (x, pet) => new { x.Amount, pet.BranchId })
            .Join(db.Branches, x => x.BranchId, b => b.BranchId, (x, b) => new { x.Amount, b.BranchName })
            .GroupBy(x => x.BranchName)
            .Select(g => new BranchIncomePointDto { BranchName = g.Key, Value = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        // Ensure every active branch appears, even with zero income, same
        // principle as FillMissing but for the decimal-based branch chart.
        var incomeByBranch = new BranchIncomeChartDto
        {
            Title = "Income by Branch (PHP)",
            DataPoints = allBranchNames.Select(name =>
                incomeByBranchRaw.FirstOrDefault(d => d.BranchName == name)
                ?? new BranchIncomePointDto { BranchName = name, Value = 0m }
            ).ToList()
        };

        // 11. Income trend: last 14 days, one point per calendar day, in order.
        var fourteenDaysAgo = DateTime.UtcNow.Date.AddDays(-13);
        var recentCompletedPayments = await db.Payments
            .Where(p => p.Status == PaymentStatuses.Completed && p.DeletedAt == null && p.CreatedAt.Date >= fourteenDaysAgo)
            .Select(p => new { p.Amount, p.CreatedAt })
            .ToListAsync(cancellationToken);

        var incomeTrend = new List<IncomeTrendPointDto>();
        for (var day = fourteenDaysAgo; day <= DateTime.UtcNow.Date; day = day.AddDays(1))
        {
            var dayTotal = recentCompletedPayments
                .Where(p => p.CreatedAt.Date == day)
                .Sum(p => p.Amount);
            incomeTrend.Add(new IncomeTrendPointDto { Date = day, Value = dayTotal });
        }

        return Ok(new BiSummaryDto
        {
            ApplicationsByStatus = applicationsByStatus,
            PetsByStatus = petsByStatus,
            ApprovedVsRejected = approvedVsRejected,
            ServiceRequestsByType = serviceRequestsByType,
            ServiceRequestsByStatus = serviceRequestsByStatus,
            TotalIncomeCompleted = totalIncome,
            PaymentsByType = paymentsByType,
            PaymentsByStatus = paymentsByStatus,
            PetsFitForAdoptionCount = petsFitForAdoptionCount,
            AdoptionsByBranch = adoptionsByBranch,
            IncomeByBranch = incomeByBranch,
            IncomeTrend = incomeTrend
        });
    }

    // Ensures every known label appears in the chart, even with a count of zero,
    // so a bar chart's categories stay consistent instead of only showing
    // whichever statuses happen to have data right now.
    private static List<KpiDataPointDto> FillMissing(List<KpiDataPointDto> actual, string[] allLabels)
    {
        var result = new List<KpiDataPointDto>();
        foreach (var label in allLabels)
        {
            var match = actual.FirstOrDefault(a => a.Label == label);
            result.Add(new KpiDataPointDto { Label = label, Value = match?.Value ?? 0 });
        }
        return result;
    }
}