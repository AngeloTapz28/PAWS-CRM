using PAWS.winui.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PAWS.winui.Services;

// Information printed in the PDF header block.
public sealed record PdfReportMeta(
    string ReportName,
    string? Company,
    string? ExportedBy,
    string Filters);

// Builds portrait A4 PDF reports. One public method per report type,
// each mapping the typed list to plain string cells, then handing off
// to the shared layout in BuildDocument.
public static class PdfReportBuilder
{
    private const string Coral = "#E8604C";
    private const string Navy = "#1F2A44";
    private const string ZebraGrey = "#F6F7F9";

    // ----- One method per report -----

    public static void ExportPets(string path, List<PetItem> items, PdfReportMeta meta)
    {
        var rows = items.Select(p => new[]
        {
            p.PetName,
            p.CategoryName,
            p.BreedName,
            p.Age.HasValue ? p.Age.ToString()! : "-",
            p.Status,
            $"{p.CreatedAt:MMM d, yyyy}",
            p.ListedAt.HasValue ? $"{p.ListedAt:MMM d, yyyy}" : "-"
        }).ToList();

        BuildDocument(path, meta,
            new[] { "Pet", "Category", "Breed", "Age", "Status", "Added", "Listed" },
            new float[] { 1.6f, 1f, 1.4f, 0.5f, 1.1f, 1.1f, 1.1f },
            rows, null);
    }

    public static void ExportApplications(string path, List<AdoptionApplicationItem> items, PdfReportMeta meta)
    {
        var rows = items.Select(a => new[]
        {
            a.ApplicationNumber,
            a.PetName,
            a.ApplicantName,
            a.ApplicationStatus,
            $"{a.AppliedAt:MMM d, yyyy}"
        }).ToList();

        BuildDocument(path, meta,
            new[] { "Application No.", "Pet", "Applicant", "Status", "Applied" },
            new float[] { 1.7f, 1.5f, 2f, 1.3f, 1.3f },
            rows, null);
    }

    public static void ExportPayments(string path, List<PaymentItem> items, PdfReportMeta meta)
    {
        // The application number is printed under the reference number so the
        // table stays narrow enough for a portrait page.
        var rows = items.Select(p => new[]
        {
            $"{p.ReferenceNumber}\n{p.ApplicationNumber}",
            p.Type,
            $"\u20b1{p.Amount:N2}",
            p.Method,
            p.Status,
            string.IsNullOrWhiteSpace(p.BranchName) ? "-" : p.BranchName!,
            $"{p.CreatedAt:MMM d, yyyy}"
        }).ToList();

        var total = items.Sum(p => p.Amount);

        BuildDocument(path, meta,
            new[] { "Reference / Application", "Type", "Amount", "Method", "Status", "Branch", "Date" },
            new float[] { 2f, 1.2f, 1.3f, 1f, 1f, 1.3f, 1.2f },
            rows, $"Total amount: \u20b1{total:N2}");
    }

    public static void ExportCustomerService(string path, List<CustomerServiceRequestItem> items, PdfReportMeta meta)
    {
        var rows = items.Select(r => new[]
        {
            r.Type,
            r.Subject,
            r.CustomerName,
            r.Status,
            $"{r.CreatedAt:MMM d, yyyy}"
        }).ToList();

        BuildDocument(path, meta,
            new[] { "Type", "Subject", "From", "Status", "Date" },
            new float[] { 1f, 3f, 1.8f, 1.1f, 1.2f },
            rows, null);
    }

    public static void ExportMedical(string path, List<MedicalRecordItem> items, PdfReportMeta meta)
    {
        var rows = items.Select(m => new[]
        {
            m.PetName,
            $"{m.ExaminationDate:MMM d, yyyy}",
            m.VetName,
            m.HealthStatus,
            m.FitForAdoption ? "Yes" : "No"
        }).ToList();

        BuildDocument(path, meta,
            new[] { "Pet", "Examined", "Veterinarian", "Health status", "Fit for adoption" },
            new float[] { 1.6f, 1.3f, 1.8f, 1.5f, 1f },
            rows, null);
    }

    // ----- Shared layout -----

    private static void BuildDocument(
        string path,
        PdfReportMeta meta,
        string[] headers,
        float[] widths,
        List<string[]> rows,
        string? extraSummary)
    {
        // Optional logo: shown only if the file exists next to the app.
        byte[]? logo = null;
        var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "paws-icon.png");
        if (File.Exists(logoPath))
        {
            logo = File.ReadAllBytes(logoPath);
        }

        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Segoe UI", "Arial"));

                // ----- Header -----
                page.Header().PaddingBottom(10).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        if (logo is not null)
                        {
                            row.ConstantItem(42).Height(42).Image(logo);
                            row.ConstantItem(10);
                        }

                        row.RelativeItem().Column(titles =>
                        {
                            titles.Item().Text("PAWS Pet Adoption System")
                                .FontSize(11).SemiBold().FontColor(Coral);
                            titles.Item().Text(meta.ReportName)
                                .FontSize(18).Bold().FontColor(Navy);
                        });

                        row.ConstantItem(150).AlignRight().Column(right =>
                        {
                            right.Item().AlignRight().Text("Generated")
                                .FontSize(8).FontColor(Colors.Grey.Darken1);
                            right.Item().AlignRight().Text($"{DateTime.Now:MMM d, yyyy h:mm tt}")
                                .FontSize(9);
                        });
                    });

                    col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Coral);
                });

                // ----- Content -----
                page.Content().Column(col =>
                {
                    // Info block
                    col.Item().PaddingBottom(10).Background(ZebraGrey).Padding(8).Column(info =>
                    {
                        if (!string.IsNullOrWhiteSpace(meta.Company))
                            info.Item().Text(t => { t.Span("Company: ").SemiBold(); t.Span(meta.Company); });

                        if (!string.IsNullOrWhiteSpace(meta.ExportedBy))
                            info.Item().Text(t => { t.Span("Exported by: ").SemiBold(); t.Span(meta.ExportedBy); });

                        info.Item().Text(t => { t.Span("Filters: ").SemiBold(); t.Span(meta.Filters); });
                    });

                    // Table
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            foreach (var w in widths)
                                columns.RelativeColumn(w);
                        });

                        // The header row repeats automatically on every page.
                        table.Header(header =>
                        {
                            foreach (var h in headers)
                            {
                                header.Cell().Background(Navy).Padding(5)
                                    .Text(h).FontColor(Colors.White).SemiBold();
                            }
                        });

                        for (int i = 0; i < rows.Count; i++)
                        {
                            string background = i % 2 == 0 ? "#FFFFFF" : ZebraGrey;

                            foreach (var cell in rows[i])
                            {
                                table.Cell()
                                    .Background(background)
                                    .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                                    .Padding(5)
                                    .Text(cell ?? string.Empty);
                            }
                        }
                    });

                    // Summary
                    col.Item().PaddingTop(12).Column(summary =>
                    {
                        summary.Item().Text(rows.Count == 1 ? "Total records: 1" : $"Total records: {rows.Count}")
                            .SemiBold();

                        if (extraSummary is not null)
                            summary.Item().Text(extraSummary).SemiBold().FontColor(Coral);
                    });
                });

                // ----- Footer -----
                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf(path);
    }
}