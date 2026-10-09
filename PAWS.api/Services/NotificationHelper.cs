using Microsoft.EntityFrameworkCore;
using PAWS.domain.Constants;
using PAWS.domain.Entities;
using PAWS.infrastructure.Data;

namespace PAWS.api.Services;

// Every notification in the system is created through here, never by hand
// in a controller — keeps all four trigger points consistent. Callers still
// own calling SaveChangesAsync themselves, so a notification can be batched
// into the same unit of work as the action that caused it.
public static class NotificationHelper
{
    public static void Create(TenantErpDbContext db, int userId, string title, string message, string type, string? link = null)
    {
        db.SystemNotifications.Add(new SystemNotification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            Link = link
        });
    }

    // One row per Administrator/Staff user in the company — not one shared row.
    public static async Task NotifyAdminsAndStaffAsync(
        TenantErpDbContext db, string title, string message, string type, string? link, CancellationToken cancellationToken)
    {
        var recipientIds = await db.RoleUsers
            .Where(ru => ru.Role != null && (ru.Role.Name == RoleNames.Administrator || ru.Role.Name == RoleNames.Staff))
            .Select(ru => ru.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var userId in recipientIds)
        {
            Create(db, userId, title, message, type, link);
        }
    }
}