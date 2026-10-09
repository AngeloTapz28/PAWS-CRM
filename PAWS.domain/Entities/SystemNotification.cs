using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// An alert that belongs to one user. The system creates these for events
/// such as an application being submitted, a status change, a pet being
/// medically cleared, or a payment being recorded.
/// </summary>
public class SystemNotification
{
    public int SystemNotificationId { get; set; }

    // The user who receives this notification.
    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    // A category such as "info", "success", "warning".
    public string Type { get; set; } = "info";

    // Optional link to the related page or record.
    public string? Link { get; set; }

    // Optional icon name shown next to the notification.
    public string? Icon { get; set; }

    // Empty (null) means unread. It is filled in when the user reads it.
    // The unread count is the number of rows where this is null.
    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation property.
    public User? User { get; set; }
}
