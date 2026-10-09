using System;

namespace PAWS.winui.Models;

public class CustomerServiceRequestItem
{
    public int CustomerServiceRequestId { get; set; }
    public int UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Response { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class SubmitCustomerServiceRequest
{
    public string Type { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class RespondRequest
{
    public string Response { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class SetRequestStatus
{
    public string Status { get; set; } = string.Empty;
}