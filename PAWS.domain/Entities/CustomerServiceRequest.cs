using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// An Inquiry, Complaint or Feedback submitted by a customer (adopter)
/// through the application. Staff and Administrators review it, respond,
/// and update its status. The customer can then see the status and response.
/// </summary>
public class CustomerServiceRequest
{
    public int CustomerServiceRequestId { get; set; }

    // The customer who submitted the request.
    // It must reference an existing User.
    public int UserId { get; set; }

    // Allowed values: "Inquiry", "Complaint", "Feedback".
    public string Type { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    // Allowed values: "New", "In Progress", "Resolved", "Closed".
    public string Status { get; set; } = "New";

    // Written by Staff/Admin. Empty until someone responds.
    public string? Response { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation property.
    public User? User { get; set; }
}
