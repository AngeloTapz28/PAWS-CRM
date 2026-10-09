namespace PAWS.winui.Models;

// Matches the JSON returned by GET /api/dashboard/summary.
// The names must match the API's fields (capital letters do not matter when reading).
public class DashboardSummary
{
    public int TotalPets { get; set; }
    public int AvailablePets { get; set; }
    public int AdoptedPets { get; set; }

    public int PendingApplications { get; set; }
    public int ApprovedApplications { get; set; }

    public int TotalCustomers { get; set; }

    public int CustomerServiceRequestsTotal { get; set; }
    public int CustomerServiceRequestsOpen { get; set; }

    public decimal TotalPaymentsCompleted { get; set; }
}