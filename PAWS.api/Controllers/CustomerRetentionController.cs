using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.domain.Entities;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

// Administrator/Staff only, throughout. This is an internal staff tool —
// Adopters have no access to their own retention record.
[Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
[ApiController]
[Route("api/customer-retention")]
public class CustomerRetentionController : ControllerBase
{
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;

    public CustomerRetentionController(
        ICurrentCompanyService currentCompany,
        ITenantDbContextFactory tenantFactory)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
    }

    // GET /api/customer-retention
    // Lists every Adopter in the company, each with their retention record if
    // one exists yet (most won't, until Staff first save notes for them).
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var adopterUserIds = await db.RoleUsers
            .Where(ru => ru.Role != null && ru.Role.Name == RoleNames.Adopter)
            .Select(ru => ru.UserId)
            .ToListAsync(cancellationToken);

        var adopters = await db.Users
            .Where(u => adopterUserIds.Contains(u.UserId))
            .ToListAsync(cancellationToken);

        var retentionRows = await db.CustomerRetentions
            .Include(r => r.LastContactedByUser)
            .Where(r => adopterUserIds.Contains(r.UserId))
            .ToDictionaryAsync(r => r.UserId, cancellationToken);

        var result = adopters
            .Select(u => ToDto(u, retentionRows.GetValueOrDefault(u.UserId)))
            .OrderBy(d => d.RetentionStatus == "Overdue" ? 0 : d.RetentionStatus == "Due Soon" ? 1 : d.RetentionStatus == "On Track" ? 2 : 3)
            .ThenBy(d => d.CustomerName)
            .ToList();

        return Ok(result);
    }

    // GET /api/customer-retention/{userId}
    [HttpGet("{userId:int}")]
    public async Task<IActionResult> GetOne(int userId, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        if (!await IsAdopterAsync(db, userId, cancellationToken))
        {
            return BadRequest(new { message = "That user is not an Adopter." });
        }

        var retention = await db.CustomerRetentions
            .Include(r => r.LastContactedByUser)
            .FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);

        return Ok(ToDto(user, retention));
    }

    // PUT /api/customer-retention/{userId}
    // Creates the row on first save, or updates it if it already exists.
    [HttpPut("{userId:int}")]
    public async Task<IActionResult> Save(int userId, SaveCustomerRetentionRequestDto dto, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        var currentUserId = GetCurrentUserId();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        if (!await IsAdopterAsync(db, userId, cancellationToken))
        {
            return BadRequest(new { message = "That user is not an Adopter." });
        }

        var retention = await db.CustomerRetentions.FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);
        var now = DateTime.UtcNow;

        if (retention is null)
        {
            retention = new CustomerRetention
            {
                UserId = userId,
                Notes = NullIfBlank(dto.Notes),
                NextFollowUpDate = dto.NextFollowUpDate,
                LastContactedAt = now,
                LastContactedBy = currentUserId
            };
            db.CustomerRetentions.Add(retention);
        }
        else
        {
            retention.Notes = NullIfBlank(dto.Notes);
            retention.NextFollowUpDate = dto.NextFollowUpDate;
            retention.LastContactedAt = now;
            retention.LastContactedBy = currentUserId;
            retention.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Retention notes saved." });
    }

    // ----- Helpers -----

    private static async Task<bool> IsAdopterAsync(PAWS.infrastructure.Data.TenantErpDbContext db, int userId, CancellationToken cancellationToken)
    {
        return await db.RoleUsers
            .AnyAsync(ru => ru.UserId == userId && ru.Role != null && ru.Role.Name == RoleNames.Adopter, cancellationToken);
    }

    private static CustomerRetentionDto ToDto(User user, CustomerRetention? retention) => new()
    {
        CustomerRetentionId = retention?.CustomerRetentionId,
        UserId = user.UserId,
        CustomerName = user.FullName,
        CustomerEmail = user.Email,
        Notes = retention?.Notes,
        NextFollowUpDate = retention?.NextFollowUpDate,
        LastContactedAt = retention?.LastContactedAt,
        LastContactedByName = retention?.LastContactedByUser?.FullName,
        RetentionStatus = CalculateStatus(retention?.NextFollowUpDate)
    };

    private static string CalculateStatus(DateTime? nextFollowUpDate)
    {
        if (nextFollowUpDate is null)
        {
            return "Not Scheduled";
        }

        var daysUntil = (nextFollowUpDate.Value.Date - DateTime.UtcNow.Date).TotalDays;

        if (daysUntil < 0) return "Overdue";
        if (daysUntil <= 7) return "Due Soon";
        return "On Track";
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(PawsClaims.UserId)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }
}