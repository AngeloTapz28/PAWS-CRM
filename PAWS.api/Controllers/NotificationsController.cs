using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Entities;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

[Authorize]
[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;

    public NotificationsController(ICurrentCompanyService currentCompany, ITenantDbContextFactory tenantFactory)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
    }

    // GET /api/notifications
    // GET /api/notifications?unreadOnly=true
    [HttpGet]
    public async Task<IActionResult> GetNotifications(bool? unreadOnly, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        var currentUserId = GetCurrentUserId();
        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var query = db.SystemNotifications.Where(n => n.UserId == currentUserId).AsQueryable();

        if (unreadOnly == true)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => ToDto(n))
            .ToListAsync(cancellationToken);

        return Ok(notifications);
    }

    // GET /api/notifications/unread-count
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        var currentUserId = GetCurrentUserId();
        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var count = await db.SystemNotifications.CountAsync(
            n => n.UserId == currentUserId && n.ReadAt == null, cancellationToken);

        return Ok(new UnreadCountDto { UnreadCount = count });
    }

    // POST /api/notifications/{id}/read
    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        var currentUserId = GetCurrentUserId();
        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var notification = await db.SystemNotifications.FirstOrDefaultAsync(
            n => n.SystemNotificationId == id, cancellationToken);

        if (notification is null) return NotFound();

        if (notification.UserId != currentUserId)
        {
            return Forbid();
        }

        if (notification.ReadAt is null)
        {
            notification.ReadAt = DateTime.UtcNow;
            notification.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new SimpleMessageDto { Message = "Notification marked as read." });
    }

    // POST /api/notifications/read-all
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        var currentUserId = GetCurrentUserId();
        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var unread = await db.SystemNotifications
            .Where(n => n.UserId == currentUserId && n.ReadAt == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var n in unread)
        {
            n.ReadAt = now;
            n.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = $"{unread.Count} notification(s) marked as read." });
    }

    private static NotificationDto ToDto(SystemNotification n) => new()
    {
        SystemNotificationId = n.SystemNotificationId,
        Title = n.Title,
        Message = n.Message,
        Type = n.Type,
        Link = n.Link,
        Icon = n.Icon,
        ReadAt = n.ReadAt,
        CreatedAt = n.CreatedAt
    };

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(PawsClaims.UserId)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }
}