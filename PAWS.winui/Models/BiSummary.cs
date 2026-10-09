using System;
using System.Collections.Generic;

namespace PAWS.winui.Models;

public class BiSummary
{
    public KpiChart ApplicationsByStatus { get; set; } = new();
    public KpiChart PetsByStatus { get; set; } = new();
    public KpiChart ApprovedVsRejected { get; set; } = new();
    public KpiChart ServiceRequestsByType { get; set; } = new();
    public KpiChart ServiceRequestsByStatus { get; set; } = new();
    public decimal TotalIncomeCompleted { get; set; }
    public KpiChart PaymentsByType { get; set; } = new();
    public KpiChart PaymentsByStatus { get; set; } = new();
    public int PetsFitForAdoptionCount { get; set; }
    public KpiChart AdoptionsByBranch { get; set; } = new();
    public BranchIncomeChart IncomeByBranch { get; set; } = new();
    public List<IncomeTrendPoint> IncomeTrend { get; set; } = new();
}

public class KpiChart
{
    public string Title { get; set; } = string.Empty;
    public List<KpiDataPoint> DataPoints { get; set; } = new();
}

public class KpiDataPoint
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}

public class BranchIncomeChart
{
    public string Title { get; set; } = string.Empty;
    public List<BranchIncomePoint> DataPoints { get; set; } = new();
}

public class BranchIncomePoint
{
    public string BranchName { get; set; } = string.Empty;
    public decimal Value { get; set; }
}

public class IncomeTrendPoint
{
    public DateTime Date { get; set; }
    public decimal Value { get; set; }
}