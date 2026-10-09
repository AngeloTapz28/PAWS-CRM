using System.ComponentModel.DataAnnotations;

namespace PAWS.api.Dtos;

public class CustomerServiceRequestDto
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

public class SubmitCustomerServiceRequestDto
{
    // Must be one of RequestTypes (Inquiry, Complaint, Feedback) — checked in the controller.
    [Required]
    public string Type { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    [StringLength(4000, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;
}

public class RespondRequestDto
{
    [Required]
    [StringLength(4000, MinimumLength = 1)]
    public string Response { get; set; } = string.Empty;

    // Must be one of RequestStatuses — checked in the controller.
    [Required]
    public string Status { get; set; } = string.Empty;
}

public class SetRequestStatusDto
{
    [Required]
    public string Status { get; set; } = string.Empty;
}
