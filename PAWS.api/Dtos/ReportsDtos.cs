namespace PAWS.api.Dtos;

public class KpiChartDto
{
    public string Title { get; set; } = string.Empty;
    public List<KpiDataPointDto> DataPoints { get; set; } = new();
}

public class KpiDataPointDto
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}

// Separate from KpiDataPointDto because income is money (decimal), not a count.
public class BranchIncomeChartDto
{
    public string Title { get; set; } = string.Empty;
    public List<BranchIncomePointDto> DataPoints { get; set; } = new();
}

public class BranchIncomePointDto
{
    public string BranchName { get; set; } = string.Empty;
    public decimal Value { get; set; }
}

// One point on the income trend line: a calendar date and that day's total.
public class IncomeTrendPointDto
{
    public DateTime Date { get; set; }
    public decimal Value { get; set; }
}

public class BiSummaryDto
{
    public KpiChartDto ApplicationsByStatus { get; set; } = new();
    public KpiChartDto PetsByStatus { get; set; } = new();
    public KpiChartDto ApprovedVsRejected { get; set; } = new();
    public KpiChartDto ServiceRequestsByType { get; set; } = new();
    public KpiChartDto ServiceRequestsByStatus { get; set; } = new();
    public decimal TotalIncomeCompleted { get; set; }
    public KpiChartDto PaymentsByType { get; set; } = new();
    public KpiChartDto PaymentsByStatus { get; set; } = new();
    public int PetsFitForAdoptionCount { get; set; }

    // New: branch-level breakdowns and the 14-day income trend.
    public KpiChartDto AdoptionsByBranch { get; set; } = new();
    public BranchIncomeChartDto IncomeByBranch { get; set; } = new();
    public List<IncomeTrendPointDto> IncomeTrend { get; set; } = new();
}