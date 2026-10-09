using ClosedXML.Excel;
using PAWS.winui.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PAWS.winui.Services;

// Builds an .xlsx workbook for whichever report is currently on screen.
// Each method matches the columns already shown in that report's list, so
// the exported file reflects exactly what the person was looking at.
public static class ReportExporter
{
    public static void ExportPets(string filePath, List<PetItem> items)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Pet Inventory");

        var headers = new[] { "Pet Name", "Category", "Breed", "Gender", "Age", "Status", "Adoption Fee", "Listed", "Added" };
        WriteHeader(sheet, headers);

        for (int i = 0; i < items.Count; i++)
        {
            var p = items[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = p.PetName;
            sheet.Cell(row, 2).Value = p.CategoryName;
            sheet.Cell(row, 3).Value = p.BreedName;
            sheet.Cell(row, 4).Value = p.Gender ?? "";
            sheet.Cell(row, 5).Value = p.Age?.ToString() ?? "";
            sheet.Cell(row, 6).Value = p.Status;
            sheet.Cell(row, 7).Value = p.AdoptionFee?.ToString("N2") ?? "";
            sheet.Cell(row, 8).Value = p.ListedAt?.ToString("yyyy-MM-dd") ?? "";
            sheet.Cell(row, 9).Value = p.CreatedAt.ToString("yyyy-MM-dd");
        }

        AutoFitAndSave(sheet, workbook, filePath);
    }

    public static void ExportApplications(string filePath, List<AdoptionApplicationItem> items)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Adoption Applications");

        var headers = new[] { "Application #", "Pet", "Applicant", "Status", "Applied", "Reviewed By", "Rejection Reason" };
        WriteHeader(sheet, headers);

        for (int i = 0; i < items.Count; i++)
        {
            var a = items[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = a.ApplicationNumber;
            sheet.Cell(row, 2).Value = a.PetName;
            sheet.Cell(row, 3).Value = a.ApplicantName;
            sheet.Cell(row, 4).Value = a.ApplicationStatus;
            sheet.Cell(row, 5).Value = a.AppliedAt.ToString("yyyy-MM-dd");
            sheet.Cell(row, 6).Value = a.ReviewerName ?? "";
            sheet.Cell(row, 7).Value = a.RejectionReason ?? "";
        }

        AutoFitAndSave(sheet, workbook, filePath);
    }

    public static void ExportPayments(string filePath, List<PaymentItem> items)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Payments");

        var headers = new[] { "Reference #", "Application #", "Type", "Amount (PHP)", "Method", "Status", "Paid", "Created" };
        WriteHeader(sheet, headers);

        for (int i = 0; i < items.Count; i++)
        {
            var p = items[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = p.ReferenceNumber;
            sheet.Cell(row, 2).Value = p.ApplicationNumber;
            sheet.Cell(row, 3).Value = p.Type;
            sheet.Cell(row, 4).Value = p.Amount;
            sheet.Cell(row, 5).Value = p.Method;
            sheet.Cell(row, 6).Value = p.Status;
            sheet.Cell(row, 7).Value = p.PaidAt?.ToString("yyyy-MM-dd") ?? "";
            sheet.Cell(row, 8).Value = p.CreatedAt.ToString("yyyy-MM-dd");
        }

        AutoFitAndSave(sheet, workbook, filePath);
    }

    public static void ExportCustomerService(string filePath, List<CustomerServiceRequestItem> items)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Customer Service");

        var headers = new[] { "Type", "Subject", "From", "Status", "Response", "Created" };
        WriteHeader(sheet, headers);

        for (int i = 0; i < items.Count; i++)
        {
            var r = items[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = r.Type;
            sheet.Cell(row, 2).Value = r.Subject;
            sheet.Cell(row, 3).Value = r.CustomerName;
            sheet.Cell(row, 4).Value = r.Status;
            sheet.Cell(row, 5).Value = r.Response ?? "";
            sheet.Cell(row, 6).Value = r.CreatedAt.ToString("yyyy-MM-dd");
        }

        AutoFitAndSave(sheet, workbook, filePath);
    }

    public static void ExportMedical(string filePath, List<MedicalRecordItem> items)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Medical Records");

        var headers = new[] { "Pet", "Exam Date", "Vet", "Health Status", "Fit for Adoption", "Diagnosis", "Next Checkup" };
        WriteHeader(sheet, headers);

        for (int i = 0; i < items.Count; i++)
        {
            var m = items[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = m.PetName;
            sheet.Cell(row, 2).Value = m.ExaminationDate.ToString("yyyy-MM-dd");
            sheet.Cell(row, 3).Value = m.VetName;
            sheet.Cell(row, 4).Value = m.HealthStatus;
            sheet.Cell(row, 5).Value = m.FitForAdoption ? "Yes" : "No";
            sheet.Cell(row, 6).Value = m.Diagnosis ?? "";
            sheet.Cell(row, 7).Value = m.NextCheckupDate?.ToString("yyyy-MM-dd") ?? "";
        }

        AutoFitAndSave(sheet, workbook, filePath);
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
        }
    }

    private static void AutoFitAndSave(IXLWorksheet sheet, XLWorkbook workbook, string filePath)
    {
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }
}