using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.domain.Entities;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

[Authorize]
[ApiController]
[Route("api/customer-service")]
public class CustomerServiceController : ControllerBase
{
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;
    private readonly ILogger<CustomerServiceController> _logger;

    public CustomerServiceController(
        ICurrentCompanyService currentCompany,
        ITenantDbContextFactory tenantFactory,
        ILogger<CustomerServiceController> logger)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
        _logger = logger;
    }

    // GET /api/customer-service
    [HttpGet]
    public async Task<IActionResult> GetRequests(string? status, string? type, CancellationToken cancellationToken)
    {
        if (User.IsInRole(RoleNames.Veterinarian) && !IsAdminOrStaff())
        {
            return Forbid();
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var query = db.CustomerServiceRequests.Include(r => r.User).AsQueryable();

        if (User.IsInRole(RoleNames.Adopter) && !IsAdminOrStaff())
        {
            query = query.Where(r => r.UserId == GetCurrentUserId());
        }

        if (!string.IsNullOrEmpty(status))
        {
            if (!RequestStatuses.All.Contains(status))
            {
                return BadRequest(new { message = $"Status must be one of: {string.Join(", ", RequestStatuses.All)}." });
            }
            query = query.Where(r => r.Status == status);
        }

        if (!string.IsNullOrEmpty(type))
        {
            if (!RequestTypes.All.Contains(type))
            {
                return BadRequest(new { message = $"Type must be one of: {string.Join(", ", RequestTypes.All)}." });
            }
            query = query.Where(r => r.Type == type);
        }

        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => ToDto(r))
            .ToListAsync(cancellationToken);

        return Ok(requests);
    }

    // GET /api/customer-service/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetRequest(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var request = await db.CustomerServiceRequests
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.CustomerServiceRequestId == id, cancellationToken);

        if (request is null)
        {
            return NotFound();
        }

        var isOwn = request.UserId == GetCurrentUserId();
        if (!isOwn && !IsAdminOrStaff())
        {
            return Forbid();
        }

        return Ok(ToDto(request));
    }

    // POST /api/customer-service
    [HttpPost]
    [Authorize(Roles = RoleNames.Adopter)]
    public async Task<IActionResult> Submit(SubmitCustomerServiceRequestDto dto, CancellationToken cancellationToken)
    {
        if (!RequestTypes.All.Contains(dto.Type))
        {
            return BadRequest(new { message = $"Type must be one of: {string.Join(", ", RequestTypes.All)}." });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        var currentUserId = GetCurrentUserId();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var currentUser = await db.Users.FirstOrDefaultAsync(u => u.UserId == currentUserId, cancellationToken);

        var request = new CustomerServiceRequest
        {
            UserId = currentUserId,
            Type = dto.Type,
            Subject = dto.Subject.Trim(),
            Message = dto.Message.Trim(),
            Status = RequestStatuses.New
        };

        db.CustomerServiceRequests.Add(request);

        await NotificationHelper.NotifyAdminsAndStaffAsync(db,
            $"New {dto.Type}",
            $"{currentUser?.FullName ?? "A customer"} submitted a new {dto.Type}: {request.Subject}",
            "info", "customer-service", cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Customer service request {Id} ({Type}) submitted by UserId {UserId}.",
            request.CustomerServiceRequestId, dto.Type, currentUserId);

        return Ok(new SimpleMessageDto { Message = $"{dto.Type} submitted." });
    }

    // POST /api/customer-service/{id}/respond
    [HttpPost("{id:int}/respond")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> Respond(int id, RespondRequestDto dto, CancellationToken cancellationToken)
    {
        if (!RequestStatuses.All.Contains(dto.Status))
        {
            return BadRequest(new { message = $"Status must be one of: {string.Join(", ", RequestStatuses.All)}." });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var request = await db.CustomerServiceRequests.FirstOrDefaultAsync(
            r => r.CustomerServiceRequestId == id, cancellationToken);

        if (request is null)
        {
            return NotFound();
        }

        request.Response = dto.Response.Trim();
        request.Status = dto.Status;
        request.UpdatedAt = DateTime.UtcNow;

        NotificationHelper.Create(db, request.UserId,
            $"Response to your {request.Type}",
            $"Staff responded to your {request.Type}: {request.Subject}",
            "info", "customer-service");

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Response recorded." });
    }

    // POST /api/customer-service/{id}/status
    [HttpPost("{id:int}/status")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> SetStatus(int id, SetRequestStatusDto dto, CancellationToken cancellationToken)
    {
        if (!RequestStatuses.All.Contains(dto.Status))
        {
            return BadRequest(new { message = $"Status must be one of: {string.Join(", ", RequestStatuses.All)}." });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var request = await db.CustomerServiceRequests.FirstOrDefaultAsync(
            r => r.CustomerServiceRequestId == id, cancellationToken);

        if (request is null)
        {
            return NotFound();
        }

        request.Status = dto.Status;
        request.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = $"Status set to {dto.Status}." });
    }

    // ----- Helpers -----

    private static CustomerServiceRequestDto ToDto(CustomerServiceRequest r) => new()
    {
        CustomerServiceRequestId = r.CustomerServiceRequestId,
        UserId = r.UserId,
        CustomerName = r.User?.FullName ?? string.Empty,
        Type = r.Type,
        Subject = r.Subject,
        Message = r.Message,
        Status = r.Status,
        Response = r.Response,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(PawsClaims.UserId)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }

    private bool IsAdminOrStaff() =>
        User.IsInRole(RoleNames.Administrator) || User.IsInRole(RoleNames.Staff);
}