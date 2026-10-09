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
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private static readonly string[] PaymentTypes = { "Adoption Fee", "Donation", "Medical Fee", "Other" };

    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;

    public PaymentsController(ICurrentCompanyService currentCompany, ITenantDbContextFactory tenantFactory)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
    }

    // GET /api/payments
    [HttpGet]
    public async Task<IActionResult> GetPayments(string? status, string? type, int? branchId, CancellationToken cancellationToken)
    {
        if (User.IsInRole(RoleNames.Veterinarian) && !IsAdminOrStaff())
        {
            return Forbid();
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var query = db.Payments
            .Include(p => p.AdoptionApplication!)
                .ThenInclude(a => a.Pet!)
                    .ThenInclude(pet => pet.Branch)
            .Where(p => p.DeletedAt == null)
            .AsQueryable();

        if (IsAdopterOnly())
        {
            var ownApplicationIds = await db.AdoptionApplications
                .Where(a => a.UserId == GetCurrentUserId())
                .Select(a => a.AdoptionApplicationId)
                .ToListAsync(cancellationToken);

            query = query.Where(p => ownApplicationIds.Contains(p.AdoptionApplicationId));
        }
        else if (!IsAdminOrStaff())
        {
            return Forbid();
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(p => p.Status == status);
        }

        if (!string.IsNullOrEmpty(type))
        {
            query = query.Where(p => p.Type == type);
        }

        if (branchId.HasValue)
        {
            query = query.Where(p => p.AdoptionApplication != null
                                   && p.AdoptionApplication.Pet != null
                                   && p.AdoptionApplication.Pet.BranchId == branchId.Value);
        }

        var payments = await query.OrderByDescending(p => p.CreatedAt).Select(p => ToDto(p)).ToListAsync(cancellationToken);
        return Ok(payments);
    }

    // GET /api/payments/payable-applications
    // Each application carries the pet's adoption fee, so the form can show it
    // and nobody has to type the price.
    [HttpGet("payable-applications")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> GetPayableApplications(CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var applicationIdsWithCompletedFee = await db.Payments
            .Where(p => p.Type == "Adoption Fee" && p.Status == PaymentStatuses.Completed && p.DeletedAt == null)
            .Select(p => p.AdoptionApplicationId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var applications = await db.AdoptionApplications
            .Include(a => a.Pet)
            .Include(a => a.User)
            .Where(a => (a.ApplicationStatus == ApplicationStatuses.Approved || a.ApplicationStatus == ApplicationStatuses.Completed)
                     && !applicationIdsWithCompletedFee.Contains(a.AdoptionApplicationId))
            .OrderByDescending(a => a.AppliedAt)
            .Select(a => new PayableApplicationDto
            {
                AdoptionApplicationId = a.AdoptionApplicationId,
                ApplicationNumber = a.ApplicationNumber,
                PetName = a.Pet != null ? a.Pet.PetName : string.Empty,
                ApplicantName = a.User != null ? a.User.FullName : string.Empty,
                AdoptionFee = a.Pet != null ? a.Pet.AdoptionFee : null
            })
            .ToListAsync(cancellationToken);

        return Ok(applications);
    }

    // POST /api/payments
    // This endpoint records ADOPTION FEE payments only. The SERVER decides the amount:
    // it is the pet's fee. A request with a different amount or type is refused,
    // so the price can't be tampered with.
    [HttpPost]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> Create(CreatePaymentRequestDto dto, CancellationToken cancellationToken)
    {
        if (!PaymentTypes.Contains(dto.Type))
        {
            return BadRequest(new { message = $"Type must be one of: {string.Join(", ", PaymentTypes)}." });
        }

        if (dto.Type != "Adoption Fee")
        {
            return BadRequest(new
            {
                message = "Only Adoption Fee payments can be recorded here. Donations are recorded with the adoption application."
            });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var application = await db.AdoptionApplications
            .Include(a => a.Pet)
            .FirstOrDefaultAsync(a => a.AdoptionApplicationId == dto.AdoptionApplicationId, cancellationToken);
        if (application is null)
        {
            return BadRequest(new { message = "That adoption application does not exist." });
        }

        var alreadyPaid = await db.Payments.AnyAsync(
            p => p.AdoptionApplicationId == dto.AdoptionApplicationId
              && p.Type == "Adoption Fee"
              && p.Status == PaymentStatuses.Completed
              && p.DeletedAt == null,
            cancellationToken);

        if (alreadyPaid)
        {
            return BadRequest(new { message = "This application already has a completed Adoption Fee payment." });
        }

        var fee = application.Pet?.AdoptionFee;
        if (fee is null || fee <= 0)
        {
            return BadRequest(new
            {
                message = "This pet has no adoption fee set. Set the fee on the Pets page first."
            });
        }

        if (dto.Amount != fee.Value)
        {
            return BadRequest(new
            {
                message = $"The adoption fee for this pet is \u20b1{fee.Value:N2}. The amount cannot be changed."
            });
        }

        var count = await db.Payments.CountAsync(cancellationToken);
        var payment = new Payment
        {
            ReferenceNumber = $"PAY-{DateTime.UtcNow.Year}-{count + 1:D6}",
            AdoptionApplicationId = dto.AdoptionApplicationId,
            Type = "Adoption Fee",
            Amount = fee.Value,
            Currency = "PHP",
            Method = dto.Method,
            Status = PaymentStatuses.Pending,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            RecordedBy = GetCurrentUserId()
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = $"Payment {payment.ReferenceNumber} recorded." });
    }

    // POST /api/payments/{id}/status
    [HttpPost("{id:int}/status")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> SetStatus(int id, SetPaymentStatusRequestDto dto, CancellationToken cancellationToken)
    {
        if (!PaymentStatuses.All.Contains(dto.Status))
        {
            return BadRequest(new { message = $"Status must be one of: {string.Join(", ", PaymentStatuses.All)}." });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.PaymentId == id, cancellationToken);
        if (payment is null) return NotFound();

        payment.Status = dto.Status;
        if (dto.Status == PaymentStatuses.Completed && payment.PaidAt is null)
        {
            payment.PaidAt = DateTime.UtcNow;
        }
        payment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new SimpleMessageDto { Message = $"Payment status set to {dto.Status}." });
    }

    private static PaymentDto ToDto(Payment p) => new()
    {
        PaymentId = p.PaymentId,
        ReferenceNumber = p.ReferenceNumber,
        AdoptionApplicationId = p.AdoptionApplicationId,
        ApplicationNumber = p.AdoptionApplication?.ApplicationNumber ?? string.Empty,
        Type = p.Type,
        Amount = p.Amount,
        Currency = p.Currency,
        Method = p.Method,
        Status = p.Status,
        Notes = p.Notes,
        PaidAt = p.PaidAt,
        CreatedAt = p.CreatedAt,
        BranchName = p.AdoptionApplication?.Pet?.Branch?.BranchName
    };

    private bool IsAdminOrStaff() =>
        User.IsInRole(RoleNames.Administrator) || User.IsInRole(RoleNames.Staff);

    private bool IsAdopterOnly() =>
        User.IsInRole(RoleNames.Adopter) && !IsAdminOrStaff() && !User.IsInRole(RoleNames.Veterinarian);

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(PawsClaims.UserId)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }
}