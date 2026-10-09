using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.domain.Entities;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

// Administrator/Staff only, matching Customer Retention's access rule —
// this feature lives inside that same screen.
[Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
[ApiController]
[Route("api/promos")]
public class PromosController : ControllerBase
{
    private static readonly string[] DiscountTypes = { "Percentage", "FixedAmount", "Other" };

    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;

    public PromosController(ICurrentCompanyService currentCompany, ITenantDbContextFactory tenantFactory)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
    }

    // GET /api/promos?userId=6
    // Lists promos for one customer. UserId is required — this is always
    // used in the context of one customer's retention record, never as a
    // company-wide promo list.
    [HttpGet]
    public async Task<IActionResult> GetPromos(int userId, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var promos = await db.Promos
            .Include(p => p.CreatedByUser)
            .Include(p => p.RedeemedByUser)
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => ToDto(p))
            .ToListAsync(cancellationToken);

        return Ok(promos);
    }

    // POST /api/promos
    [HttpPost]
    public async Task<IActionResult> Create(int userId, CreatePromoRequestDto dto, CancellationToken cancellationToken)
    {
        if (!DiscountTypes.Contains(dto.DiscountType))
        {
            return BadRequest(new { message = $"DiscountType must be one of: {string.Join(", ", DiscountTypes)}." });
        }

        if ((dto.DiscountType == "Percentage" || dto.DiscountType == "FixedAmount") && dto.DiscountValue is null)
        {
            return BadRequest(new { message = "DiscountValue is required for Percentage or FixedAmount promos." });
        }

        if (dto.DiscountType == "Percentage" && (dto.DiscountValue is < 0 or > 100))
        {
            return BadRequest(new { message = "A percentage discount must be between 0 and 100." });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        var currentUserId = GetCurrentUserId();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var isAdopter = await db.RoleUsers
            .AnyAsync(ru => ru.UserId == userId && ru.Role != null && ru.Role.Name == RoleNames.Adopter, cancellationToken);

        if (!isAdopter)
        {
            return BadRequest(new { message = "That user is not an Adopter." });
        }

        var promo = new Promo
        {
            UserId = userId,
            Title = dto.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            DiscountType = dto.DiscountType,
            DiscountValue = dto.DiscountValue,
            ExpiryDate = dto.ExpiryDate,
            CreatedBy = currentUserId
        };

        db.Promos.Add(promo);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Promo created." });
    }

    // POST /api/promos/{id}/redeem
    [HttpPost("{id:int}/redeem")]
    public async Task<IActionResult> Redeem(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var promo = await db.Promos.FirstOrDefaultAsync(p => p.PromoId == id, cancellationToken);
        if (promo is null) return NotFound();

        if (promo.RedeemedAt is not null)
        {
            return BadRequest(new { message = "This promo has already been redeemed." });
        }

        promo.RedeemedAt = DateTime.UtcNow;
        promo.RedeemedBy = GetCurrentUserId();

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Promo marked as redeemed." });
    }

    private static PromoDto ToDto(Promo p) => new()
    {
        PromoId = p.PromoId,
        UserId = p.UserId,
        Title = p.Title,
        Description = p.Description,
        DiscountType = p.DiscountType,
        DiscountValue = p.DiscountValue,
        ExpiryDate = p.ExpiryDate,
        RedeemedAt = p.RedeemedAt,
        RedeemedByName = p.RedeemedByUser?.FullName,
        CreatedByName = p.CreatedByUser?.FullName ?? string.Empty,
        CreatedAt = p.CreatedAt,
        PromoStatus = CalculateStatus(p)
    };

    private static string CalculateStatus(Promo p)
    {
        if (p.RedeemedAt is not null) return "Redeemed";
        if (p.ExpiryDate.HasValue && p.ExpiryDate.Value.Date < DateTime.UtcNow.Date) return "Expired";
        return "Active";
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(PawsClaims.UserId)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }
}