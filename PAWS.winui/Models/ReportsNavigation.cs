namespace PAWS.winui.Models;

// Carries a report type and an optional single filter across navigation,
// so a click on a KPI in Business Intelligence can land directly on the
// matching, pre-filtered report instead of an unfiltered list.
public class ReportsNavigationParams
{
    // "Pets" | "Applications" | "Payments" | "CustomerService" | "Medical"
    public string ReportType { get; set; } = "Pets";

    // "status" | "type" | "fitForAdoption" | null (no filter)
    public string? FilterField { get; set; }

    public string? FilterValue { get; set; }

    // Human-readable, shown at the top of the report (e.g. "Status: Approved").
    public string? FilterLabel { get; set; }
}