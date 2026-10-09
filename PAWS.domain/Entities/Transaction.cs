using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// One financial event under a Payment (a credit, debit or refund).
/// Every payment creates at least one Transaction, which gives an audit
/// trail separate from the payment summary.
/// </summary>
public class Transaction
{
    // Our own primary key.
    public int TransactionId { get; set; }

    public int PaymentId { get; set; }

    // The reference given by an outside payment gateway (GCash, bank, etc).
    // It is separate from TransactionId, which is only our database key.
    public string? TransactionReference { get; set; }

    public string? Gateway { get; set; }

    // Money value. Its exact SQL precision, decimal(18,2), is set in Phase 5.
    public decimal Amount { get; set; }

    // Allowed values: "Credit", "Debit", "Refund".
    public string Type { get; set; } = "Credit";

    // Allowed values: "Success", "Failed", "Pending".
    public string Status { get; set; } = "Pending";

    // The raw response text from the gateway, if there is one.
    public string? GatewayResponse { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation property.
    public Payment? Payment { get; set; }
}
