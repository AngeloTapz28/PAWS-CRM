namespace PAWS.api.Dtos;

// The exact JSON shape the dashboard endpoint returns.
// A DTO (data transfer object) is a small class that holds only what we want to send out.
public class DashboardSummaryDto
{
    public int TotalPets { get; set; }
    public int AvailablePets { get; set; }
    public int AdoptedPets { get; set; }

    // Submitted + Under Review + Interview.
    public int PendingApplications { get; set; }
    public int ApprovedApplications { get; set; }

    // Users who have the Adopter role.
    public int TotalCustomers { get; set; }

    public int CustomerServiceRequestsTotal { get; set; }

    // New + In Progress.
    public int CustomerServiceRequestsOpen { get; set; }

    // Sum of Payment.Amount where Status is "Completed" and the payment is not deleted.
    public decimal TotalPaymentsCompleted { get; set; }
}