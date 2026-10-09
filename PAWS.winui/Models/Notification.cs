using System;
using System.Text.Json.Serialization;

namespace PAWS.winui.Models;

public class NotificationItem
{
    public int SystemNotificationId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Link { get; set; }
    public string? Icon { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UnreadCount
{
    [JsonPropertyName("unreadCount")]
    public int Count { get; set; }
}