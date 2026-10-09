using Microsoft.UI;
using Windows.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PAWS.winui.Models;
using PAWS.winui.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using ClosedXML.Excel;
using WinRT.Interop;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;

namespace PAWS.winui.Views;

public class ReportRow
{
    public string TitleText { get; set; } = string.Empty;
    public string DetailText { get; set; } = string.Empty;
}

public sealed partial class BusinessIntelligencePage : Page
{
    private const double MaxBarHeight = 160;
    private const double BarWidth = 60;

    // ----- Animation tuning (all times in milliseconds) -----
    private const double BarGrowMs = 650;
    private const double BarStaggerMs = 80;
    private const double CountUpMs = 700;
    private const double TrendDrawMs = 1100;

    private static readonly Dictionary<string, string[]> StatusOptions = new()
    {
        ["Pets"] = new[] { "Pending", "Available", "Under Treatment", "Adopted", "Unavailable" },
        ["Applications"] = new[] { "Submitted", "Under Review", "Approved", "Rejected", "Completed", "Withdrawn" },
        ["Payments"] = new[] { "Pending", "Completed", "Failed", "Refunded" },
        ["CustomerService"] = new[] { "New", "In Progress", "Resolved", "Closed" }
    };

    private static readonly Dictionary<string, string[]> TypeOptions = new()
    {
        ["Payments"] = new[] { "Adoption Fee", "Donation", "Medical Fee", "Other" },
        ["CustomerService"] = new[] { "Inquiry", "Complaint", "Feedback" }
    };

    private readonly ObservableCollection<ReportRow> _rows = new();
    private BiSummary? _lastSummary;
    private bool _isApplyingPendingParams;
    private List<BranchItem> _branches = new();

    // The real, typed data behind whichever report is currently shown —
    // kept alongside _rows (the display-only version) so Export can write
    // proper spreadsheet columns instead of the two text blobs the list uses.
    private List<PetItem>? _lastPets;
    private List<AdoptionApplicationItem>? _lastApplications;
    private List<PaymentItem>? _lastPayments;
    private List<CustomerServiceRequestItem>? _lastServiceRequests;
    private List<MedicalRecordItem>? _lastMedicalRecords;

    // ----- Animation state -----

    private sealed class Counter
    {
        public TextBlock Target = null!;
        public decimal Value;
        public bool IsMoney;
        public DateTime Start;
    }

    private bool _animate = true;

    // Charts wait here until they scroll into view, then play their animation once.
    private readonly List<(FrameworkElement Anchor, Action Start)> _pendingReveals = new();
    private readonly DispatcherTimer _revealTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private DateTime _revealStamp = DateTime.UtcNow;

    // Number count-ups.
    private readonly List<Counter> _counters = new();
    private readonly DispatcherTimer _countTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    // The trend line reveal (left to right).
    private readonly DispatcherTimer _trendTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private Polyline? _trendPolyline;
    private DateTime _trendStart;
    private double _trendLeft;
    private double _trendPlotWidth;
    private double _trendHeight;

    public BusinessIntelligencePage()
    {
        InitializeComponent();
        RowsList.ItemsSource = _rows;

        _countTimer.Tick += CountTimer_Tick;
        _revealTimer.Tick += RevealTimer_Tick;
        _trendTimer.Tick += TrendTimer_Tick;

        // Never leave a timer running after the page is gone.
        Unloaded += (s, e) => StopAllAnimations();
    }

    // ----- PAWS theme lookups (colors come from PawsTheme.xaml) -----

    private static Color ThemeColor(string key) => (Color)Application.Current.Resources[key];
    private static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

    // =====================================================================
    // ANIMATION HELPERS
    // =====================================================================

    private static bool ReadAnimationSetting()
    {
        // Respect the Windows "Animation effects" setting.
        try
        {
            return new UISettings().AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }

    private static Visual VisualOf(UIElement element) => ElementCompositionPreview.GetElementVisual(element);

    private static CubicBezierEasingFunction EaseOut(Compositor c) =>
        c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.8f), new Vector2(0.2f, 1.0f));

    private static string FormatNumber(decimal value, bool isMoney) =>
        isMoney
            ? "\u20b1" + value.ToString("N2", CultureInfo.InvariantCulture)
            : ((int)Math.Round(value)).ToString();

    // Stops everything that is running or waiting. Called before each redraw and when the page unloads.
    private void StopAllAnimations()
    {
        _revealTimer.Stop();
        _countTimer.Stop();
        _trendTimer.Stop();
        _counters.Clear();
        _pendingReveals.Clear();

        if (_trendPolyline is not null)
        {
            _trendPolyline.Clip = null;
            _trendPolyline = null;
        }
    }

    // Called once per overview load/refresh, before anything is drawn.
    private void PrepareAnimationRun()
    {
        StopAllAnimations();
        _animate = ReadAnimationSetting();
        _revealStamp = DateTime.UtcNow;

        // The donut canvas and its center are reused between refreshes, so put them back to normal first.
        ResetVisual(PieCanvas);
        ResetVisual(PieCenter);
    }

    private static void ResetVisual(UIElement element)
    {
        var v = VisualOf(element);
        v.Opacity = 1f;
        v.Scale = Vector3.One;
        v.RotationAngleInDegrees = 0f;
    }

    // ----- Count-up numbers -----

    private void ShowNumber(TextBlock target, decimal value, bool isMoney, int delayMs)
    {
        if (!_animate)
        {
            target.Text = FormatNumber(value, isMoney);
            return;
        }

        target.Text = FormatNumber(0, isMoney);

        _counters.RemoveAll(c => c.Target == target);
        _counters.Add(new Counter
        {
            Target = target,
            Value = value,
            IsMoney = isMoney,
            Start = DateTime.UtcNow.AddMilliseconds(delayMs)
        });

        if (!_countTimer.IsEnabled)
        {
            _countTimer.Start();
        }
    }

    private void CountTimer_Tick(object? sender, object e)
    {
        var now = DateTime.UtcNow;

        for (int i = _counters.Count - 1; i >= 0; i--)
        {
            var c = _counters[i];
            double elapsed = (now - c.Start).TotalMilliseconds;
            if (elapsed < 0)
            {
                continue;
            }

            double t = Math.Min(1.0, elapsed / CountUpMs);

            // Ease-out: fast at first, slowing down at the end.
            decimal eased = (decimal)(1.0 - Math.Pow(1.0 - t, 3));

            // The last tick always writes the exact value.
            c.Target.Text = FormatNumber(t >= 1.0 ? c.Value : c.Value * eased, c.IsMoney);

            if (t >= 1.0)
            {
                _counters.RemoveAt(i);
            }
        }

        if (_counters.Count == 0)
        {
            _countTimer.Stop();
        }
    }

    // ----- Reveal when scrolled into view -----

    private void RegisterReveal(FrameworkElement anchor, Action start)
    {
        _pendingReveals.Add((anchor, start));

        if (!_revealTimer.IsEnabled)
        {
            _revealTimer.Start();
        }
    }

    private void RevealTimer_Tick(object? sender, object e)
    {
        CheckReveals();

        if (_pendingReveals.Count == 0)
        {
            _revealTimer.Stop();
        }
    }

    private void CheckReveals()
    {
        // Safety valve: if something has been waiting a long time, show it anyway.
        bool force = (DateTime.UtcNow - _revealStamp).TotalSeconds > 20;

        for (int i = _pendingReveals.Count - 1; i >= 0; i--)
        {
            var (anchor, start) = _pendingReveals[i];

            if (force || IsInView(anchor))
            {
                _pendingReveals.RemoveAt(i);
                try
                {
                    start();
                }
                catch
                {
                    // An animation problem must never break the page.
                }
            }
        }
    }

    private bool IsInView(FrameworkElement anchor)
    {
        try
        {
            if (anchor.ActualHeight <= 0 || ActualHeight <= 0)
            {
                return false;
            }

            var p = anchor.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, 0));
            return p.Y < ActualHeight - 60 && p.Y + anchor.ActualHeight > 0;
        }
        catch
        {
            // Not in the visual tree right now (for example the Details tab is showing).
            return false;
        }
    }

    // ----- Bars: grow from the baseline, labels fade in -----

    private static void PrepareGrow(UIElement bar, double width, double height)
    {
        var v = VisualOf(bar);
        v.CenterPoint = new Vector3((float)(width / 2), (float)height, 0f);
        v.Scale = new Vector3(1f, 0f, 1f);
    }

    private static void PlayGrow(UIElement bar, double delayMs)
    {
        var v = VisualOf(bar);

        try
        {
            var c = v.Compositor;
            var a = c.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(1f, 1f, EaseOut(c));
            a.Duration = TimeSpan.FromMilliseconds(BarGrowMs);
            a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
            v.StartAnimation("Scale.Y", a);
        }
        catch
        {
            v.Scale = Vector3.One;
        }
    }

    private static void PrepareFade(UIElement element)
    {
        VisualOf(element).Opacity = 0f;
    }

    private static void PlayFade(UIElement element, double delayMs, double durationMs = 350)
    {
        var v = VisualOf(element);

        try
        {
            var c = v.Compositor;
            var a = c.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(1f, 1f, EaseOut(c));
            a.Duration = TimeSpan.FromMilliseconds(durationMs);
            a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
            v.StartAnimation("Opacity", a);
        }
        catch
        {
            v.Opacity = 1f;
        }
    }

    // ----- Trend dots: pop in as the line reaches them -----

    private static void PrepareDot(UIElement dot)
    {
        var v = VisualOf(dot);
        v.CenterPoint = new Vector3(4f, 4f, 0f);
        v.Scale = new Vector3(0f, 0f, 1f);
    }

    private static void PlayDotPop(UIElement dot, double delayMs, bool isPeak)
    {
        var v = VisualOf(dot);

        try
        {
            var c = v.Compositor;
            var ease = EaseOut(c);
            var a = c.CreateVector3KeyFrameAnimation();

            if (!isPeak)
            {
                a.InsertKeyFrame(1f, Vector3.One, ease);
                a.Duration = TimeSpan.FromMilliseconds(260);
                a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
            }
            else
            {
                // The highest point pops in, then gives one soft pulse.
                const double popMs = 260;
                const double pulseMs = 700;
                double total = delayMs + popMs + pulseMs;

                a.Duration = TimeSpan.FromMilliseconds(total);

                if (delayMs > 1)
                {
                    a.InsertKeyFrame((float)(delayMs / total), new Vector3(0f, 0f, 1f));
                }
                a.InsertKeyFrame((float)((delayMs + popMs) / total), Vector3.One, ease);
                a.InsertKeyFrame((float)((delayMs + popMs + pulseMs * 0.4) / total), new Vector3(1.9f, 1.9f, 1f), ease);
                a.InsertKeyFrame(1f, Vector3.One, ease);
            }

            v.StartAnimation("Scale", a);
        }
        catch
        {
            v.Scale = Vector3.One;
        }
    }

    private void TrendTimer_Tick(object? sender, object e)
    {
        if (_trendPolyline is null)
        {
            _trendTimer.Stop();
            return;
        }

        double t = Math.Min(1.0, (DateTime.UtcNow - _trendStart).TotalMilliseconds / TrendDrawMs);

        if (t >= 1.0)
        {
            // Done: remove the clip so the whole line shows.
            _trendPolyline.Clip = null;
            _trendPolyline = null;
            _trendTimer.Stop();
            return;
        }

        double eased = 1.0 - Math.Pow(1.0 - t, 3);

        _trendPolyline.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, _trendLeft + _trendPlotWidth * eased + 4, _trendHeight)
        };
    }

    // =====================================================================
    // PAGE LOADING
    // =====================================================================

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadBranchesForFilterAsync();
        await LoadOverviewAsync();
    }

    private async Task LoadBranchesForFilterAsync()
    {
        try
        {
            using var response = await ApiClient.Http.GetAsync("api/branches");
            if (response.IsSuccessStatusCode)
            {
                var items = await response.Content.ReadFromJsonAsync<List<BranchItem>>() ?? new();
                _branches = items.Where(b => b.IsActive).OrderBy(b => b.BranchName).ToList();
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            // Best-effort; the Payments branch filter just won't populate if this fails.
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (ReportsPivot.SelectedIndex == 0)
        {
            await LoadOverviewAsync();
        }
        else
        {
            await LoadDetailsAsync();
        }
    }

    private async void ReportsPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Details tab is index 1. Load it lazily the first time it's shown,
        // and whenever the user manually switches back to it.
        if (ReportsPivot.SelectedIndex != 1 || _isApplyingPendingParams)
        {
            return;
        }

        // If nothing has ever been chosen yet (the person switched to Details
        // without clicking a KPI first), pick a default now, with the guard
        // held so the resulting control changes below cannot trigger a reload
        // of their own — LoadDetailsAsync() at the end is the only reload.
        if (ReportTypeBox.SelectedItem is null)
        {
            _isApplyingPendingParams = true;
            ReportTypeBox.SelectedIndex = 0;
            RebuildFilterControls(CurrentReportType);
            _isApplyingPendingParams = false;
        }

        await LoadDetailsAsync();
    }

    // ----- Overview tab: load and draw all KPIs -----

    private async Task LoadOverviewAsync()
    {
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;

        try
        {
            using var response = await ApiClient.Http.GetAsync("api/reports/bi-summary");

            if (response.IsSuccessStatusCode)
            {
                var summary = await response.Content.ReadFromJsonAsync<BiSummary>();
                if (summary is null)
                {
                    ShowError("The API sent an empty reply.");
                    return;
                }

                _lastSummary = summary;

                // Reset animation state, then draw. Every chart below registers its own animation.
                PrepareAnimationRun();

                ShowNumber(TotalIncomeText, summary.TotalIncomeCompleted, true, 150);
                ShowNumber(MedicalKpiCountText, summary.PetsFitForAdoptionCount, false, 250);

                var coral = ThemeColor("PawsPrimaryColor");
                var slate = ThemeColor("PawsSlateColor");

                DrawPieChart(summary.ApprovedVsRejected, "Applications", "status");
                DrawChart(Chart1Title, Chart1Container, summary.ApplicationsByStatus, coral, "Applications", "status");
                DrawChart(Chart2Title, Chart2Container, summary.PetsByStatus, coral, "Pets", "status");
                DrawChart(Chart4Title, Chart4Container, summary.ServiceRequestsByType, coral, "CustomerService", "type");
                DrawChart(Chart5Title, Chart5Container, summary.ServiceRequestsByStatus, coral, "CustomerService", "status");
                DrawChart(Chart6Title, Chart6Container, summary.PaymentsByType, slate, "Payments", "type");
                DrawChart(Chart7Title, Chart7Container, summary.PaymentsByStatus, slate, "Payments", "status");

                DrawTrendLine(summary.IncomeTrend, coral);
                DrawChart(AdoptionsByBranchTitle, AdoptionsByBranchContainer, summary.AdoptionsByBranch, slate, "Applications", "status");
                DrawBranchIncomeChart(IncomeByBranchTitle, IncomeByBranchContainer, summary.IncomeByBranch, coral);
            }
            else
            {
                ShowError($"Could not load report data (HTTP {(int)response.StatusCode}).");
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            ShowError("Could not reach the API.");
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    // Draws one bar chart and wires every bar to jump to the Details tab,
    // pre-filtered to that bar's exact value.
    private void DrawChart(TextBlock titleBlock, StackPanel container, KpiChart chart, Color barColor,
        string targetReportType, string targetFilterField)
    {
        titleBlock.Text = chart.Title;
        container.Children.Clear();

        if (chart.DataPoints.Count == 0)
        {
            container.Children.Add(new TextBlock
            {
                Text = "No data yet.",
                Foreground = ThemeBrush("PawsTextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        var maxValue = Math.Max(1, chart.DataPoints.Max(d => d.Value));
        var animated = new List<(Rectangle Bar, TextBlock Label)>();

        foreach (var point in chart.DataPoints)
        {
            var barHeight = point.Value == 0 ? 2 : Math.Max(4, (point.Value / (double)maxValue) * MaxBarHeight);

            var column = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = BarWidth,
                VerticalAlignment = VerticalAlignment.Bottom,
                Spacing = 4
            };

            var valueLabel = new TextBlock
            {
                Text = point.Value.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13,
                Foreground = ThemeBrush("PawsTextPrimaryBrush")
            };
            column.Children.Add(valueLabel);

            var bar = new Rectangle
            {
                Width = BarWidth,
                Height = barHeight,
                Fill = new SolidColorBrush(barColor),
                RadiusX = 6,
                RadiusY = 6,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            var label = point.Label;
            bar.PointerPressed += (s, e) => NavigateToDetails(targetReportType, targetFilterField, label, $"{chart.Title}: {label}");

            // Hover feedback and a tooltip with the exact label and value.
            bar.PointerEntered += (s, e) => bar.Opacity = 0.75;
            bar.PointerExited += (s, e) => bar.Opacity = 1.0;
            ToolTipService.SetToolTip(bar, $"{point.Label}: {point.Value}");

            column.Children.Add(bar);

            column.Children.Add(new TextBlock
            {
                Text = point.Label,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                FontSize = 11,
                Foreground = ThemeBrush("PawsTextSecondaryBrush")
            });

            container.Children.Add(column);

            if (_animate)
            {
                PrepareGrow(bar, BarWidth, barHeight);
                PrepareFade(valueLabel);
                animated.Add((bar, valueLabel));
            }
        }

        if (_animate)
        {
            RegisterReveal(container, () =>
            {
                for (int i = 0; i < animated.Count; i++)
                {
                    PlayGrow(animated[i].Bar, i * BarStaggerMs);
                    PlayFade(animated[i].Label, i * BarStaggerMs + BarGrowMs * 0.55);
                }
            });
        }
    }

    // Same visual approach as DrawChart, but for decimal (PHP) values keyed
    // by branch name instead of int counts keyed by a status/type label.
    // Clicking a bar now applies a real branch filter on the Payments
    // report, via the "branchName" filter field handled in NavigateToDetails.
    private void DrawBranchIncomeChart(TextBlock titleBlock, StackPanel container, BranchIncomeChart chart, Color barColor)
    {
        titleBlock.Text = chart.Title;
        container.Children.Clear();

        if (chart.DataPoints.Count == 0)
        {
            container.Children.Add(new TextBlock
            {
                Text = "No data yet.",
                Foreground = ThemeBrush("PawsTextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        var maxValue = Math.Max(1m, chart.DataPoints.Max(d => d.Value));
        var animated = new List<(Rectangle Bar, TextBlock Label)>();

        foreach (var point in chart.DataPoints)
        {
            var barHeight = point.Value == 0 ? 2 : Math.Max(4, (double)(point.Value / maxValue) * 140);

            var column = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = BarWidth,
                VerticalAlignment = VerticalAlignment.Bottom,
                Spacing = 4
            };

            var valueLabel = new TextBlock
            {
                Text = "\u20b1" + point.Value.ToString("N0", CultureInfo.InvariantCulture),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 12,
                Foreground = ThemeBrush("PawsTextPrimaryBrush")
            };
            column.Children.Add(valueLabel);

            var bar = new Rectangle
            {
                Width = BarWidth,
                Height = barHeight,
                Fill = new SolidColorBrush(barColor),
                RadiusX = 6,
                RadiusY = 6,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            var branchName = point.BranchName;
            var branchValue = point.Value;
            bar.PointerPressed += (s, e) => NavigateToDetails(
                "Payments", "branchName", branchName, $"{chart.Title}: {branchName} (\u20b1{branchValue:N2})");

            bar.PointerEntered += (s, e) => bar.Opacity = 0.75;
            bar.PointerExited += (s, e) => bar.Opacity = 1.0;
            ToolTipService.SetToolTip(bar, $"{branchName}: \u20b1{branchValue:N2}");

            column.Children.Add(bar);

            column.Children.Add(new TextBlock
            {
                Text = point.BranchName,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                FontSize = 11,
                Foreground = ThemeBrush("PawsTextSecondaryBrush")
            });

            container.Children.Add(column);

            if (_animate)
            {
                PrepareGrow(bar, BarWidth, barHeight);
                PrepareFade(valueLabel);
                animated.Add((bar, valueLabel));
            }
        }

        if (_animate)
        {
            RegisterReveal(container, () =>
            {
                for (int i = 0; i < animated.Count; i++)
                {
                    PlayGrow(animated[i].Bar, i * BarStaggerMs);
                    PlayFade(animated[i].Label, i * BarStaggerMs + BarGrowMs * 0.55);
                }
            });
        }
    }

    // Draws the 14-day income trend as a connected line with dots at each
    // point, axis labels, and a light baseline grid. No click-to-navigate
    // here, since a single day's income has no matching Details filter.
    private void DrawTrendLine(List<IncomeTrendPoint> points, Color lineColor)
    {
        TrendCanvas.Children.Clear();

        if (points.Count == 0)
        {
            TrendCanvas.Children.Add(new TextBlock
            {
                Text = "No data yet.",
                Foreground = ThemeBrush("PawsTextSecondaryBrush")
            });
            return;
        }

        const double canvasWidth = 900;
        const double canvasHeight = 180;
        const double leftPadding = 50;
        const double bottomPadding = 30;
        const double topPadding = 10;

        TrendCanvas.Width = canvasWidth;
        TrendCanvas.Height = canvasHeight + bottomPadding;

        var maxValue = Math.Max(1m, points.Max(p => p.Value));
        var plotWidth = canvasWidth - leftPadding;
        var plotHeight = canvasHeight - topPadding;
        var stepX = points.Count > 1 ? plotWidth / (points.Count - 1) : 0;

        // Baseline grid: a light horizontal line at 0, 50% and 100% of max.
        foreach (var fraction in new[] { 0.0, 0.5, 1.0 })
        {
            var y = topPadding + plotHeight - (plotHeight * fraction);
            var gridLine = new Line
            {
                X1 = leftPadding,
                X2 = canvasWidth,
                Y1 = y,
                Y2 = y,
                Stroke = ThemeBrush("PawsBorderBrush"),
                StrokeThickness = 1
            };
            TrendCanvas.Children.Add(gridLine);

            var gridLabel = new TextBlock
            {
                Text = "\u20b1" + Math.Round(maxValue * (decimal)fraction, 0).ToString("N0", CultureInfo.InvariantCulture),
                FontSize = 10,
                Foreground = ThemeBrush("PawsTextSecondaryBrush")
            };
            Canvas.SetLeft(gridLabel, 0);
            Canvas.SetTop(gridLabel, y - 7);
            TrendCanvas.Children.Add(gridLabel);
        }

        // Build the connected line through every point.
        var polyline = new Polyline
        {
            Stroke = new SolidColorBrush(lineColor),
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round
        };

        var points2d = new List<Windows.Foundation.Point>();

        for (int i = 0; i < points.Count; i++)
        {
            var x = leftPadding + (stepX * i);
            var valueFraction = (double)(points[i].Value / maxValue);
            var y = topPadding + plotHeight - (plotHeight * valueFraction);
            points2d.Add(new Windows.Foundation.Point(x, y));
            polyline.Points.Add(new Windows.Foundation.Point(x, y));
        }

        TrendCanvas.Children.Add(polyline);

        // Which dot is the highest? It gets a soft pulse (only if there is any income at all).
        int peakIndex = -1;
        if (points.Max(p => p.Value) > 0)
        {
            decimal best = -1;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].Value > best)
                {
                    best = points[i].Value;
                    peakIndex = i;
                }
            }
        }

        var dots = new List<Ellipse>();

        // Dots on each point, with a tooltip showing the exact date and amount.
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(lineColor),
                Stroke = ThemeBrush("PawsSurfaceBrush"),
                StrokeThickness = 1.5
            };
            Canvas.SetLeft(dot, points2d[i].X - 4);
            Canvas.SetTop(dot, points2d[i].Y - 4);

            dot.PointerEntered += (s, e) => dot.Opacity = 0.7;
            dot.PointerExited += (s, e) => dot.Opacity = 1.0;
            ToolTipService.SetToolTip(dot, $"{point.Date:MMM d}: \u20b1{point.Value:N2}");

            TrendCanvas.Children.Add(dot);
            dots.Add(dot);

            // Date label every other day, to avoid crowding 14 labels together.
            if (i % 2 == 0)
            {
                var dateLabel = new TextBlock
                {
                    Text = point.Date.ToString("MMM d"),
                    FontSize = 10,
                    Foreground = ThemeBrush("PawsTextSecondaryBrush")
                };
                Canvas.SetLeft(dateLabel, points2d[i].X - 16);
                Canvas.SetTop(dateLabel, canvasHeight + 6);
                TrendCanvas.Children.Add(dateLabel);
            }
        }

        if (!_animate)
        {
            return;
        }

        // Start hidden: the line is clipped to nothing and every dot is scaled to zero.
        double totalHeight = canvasHeight + bottomPadding;
        polyline.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 0, totalHeight) };
        foreach (var dot in dots)
        {
            PrepareDot(dot);
        }

        RegisterReveal(TrendCanvas, () =>
        {
            _trendPolyline = polyline;
            _trendLeft = leftPadding;
            _trendPlotWidth = plotWidth;
            _trendHeight = totalHeight;
            _trendStart = DateTime.UtcNow;
            _trendTimer.Start();

            for (int i = 0; i < dots.Count; i++)
            {
                // Pop each dot just as the (eased) line reaches it.
                double x = dots.Count > 1 ? (double)i / (dots.Count - 1) : 0;
                double delay = TrendDrawMs * (1.0 - Math.Pow(1.0 - x, 1.0 / 3.0));
                PlayDotPop(dots[i], delay, i == peakIndex);
            }
        });
    }

    // Approved is green and Rejected is red (matched by label); anything else
    // cycles through the remaining PAWS colors.
    private static Color PieColorFor(string label, int index)
    {
        if (label.Contains("Approved", StringComparison.OrdinalIgnoreCase)) return ThemeColor("PawsSuccessColor");
        if (label.Contains("Reject", StringComparison.OrdinalIgnoreCase)) return ThemeColor("PawsErrorColor");

        var fallback = new[] { "PawsInfoColor", "PawsWarningColor", "PawsSlateColor", "PawsPrimaryColor" };
        return ThemeColor(fallback[index % fallback.Length]);
    }

    private void DrawPieChart(KpiChart chart, string targetReportType, string targetFilterField)
    {
        PieTitle.Text = chart.Title;
        PieCanvas.Children.Clear();
        PieLegend.Children.Clear();

        var total = chart.DataPoints.Sum(d => d.Value);
        PieCenterValue.Text = total.ToString();

        if (total == 0)
        {
            PieCenter.Visibility = Visibility.Collapsed;
            PieLegend.Children.Add(new TextBlock
            {
                Text = "No data yet.",
                Foreground = ThemeBrush("PawsTextSecondaryBrush")
            });
            return;
        }

        PieCenter.Visibility = Visibility.Visible;

        const double radius = 100;
        const double centerX = 110;
        const double centerY = 110;

        double startAngle = 0;
        var legendRows = new List<StackPanel>();

        for (int i = 0; i < chart.DataPoints.Count; i++)
        {
            var point = chart.DataPoints[i];
            if (point.Value == 0) continue;

            var sweepAngle = point.Value / (double)total * 360.0;
            var endAngle = startAngle + sweepAngle;
            var color = PieColorFor(point.Label, i);
            var percent = Math.Round(point.Value / (double)total * 100, 1);

            // One category holding 100% of the data is a full circle. An arc whose
            // start and end points are identical draws nothing, so use an Ellipse.
            Shape slice = sweepAngle >= 359.9
                ? new Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Fill = new SolidColorBrush(color)
                }
                : BuildPieSlice(centerX, centerY, radius, startAngle, endAngle, color);

            if (slice is Ellipse)
            {
                Canvas.SetLeft(slice, centerX - radius);
                Canvas.SetTop(slice, centerY - radius);
            }

            var label = point.Label;
            slice.PointerPressed += (s, e) => NavigateToDetails(targetReportType, targetFilterField, label, $"{chart.Title}: {label} ({percent}%)");
            slice.PointerEntered += (s, e) => slice.Opacity = 0.85;
            slice.PointerExited += (s, e) => slice.Opacity = 1.0;
            ToolTipService.SetToolTip(slice, $"{point.Label}: {point.Value} ({percent}%)");
            PieCanvas.Children.Add(slice);

            var legendRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            legendRow.Children.Add(new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center
            });
            legendRow.Children.Add(new TextBlock
            {
                Text = $"{point.Label}: {point.Value} ({percent}%)",
                FontSize = 13,
                Foreground = ThemeBrush("PawsTextPrimaryBrush")
            });
            PieLegend.Children.Add(legendRow);
            legendRows.Add(legendRow);

            startAngle = endAngle;
        }

        if (!_animate)
        {
            return;
        }

        // Start hidden: the donut is small, tilted and transparent; the center and legend are transparent.
        var pieVisual = VisualOf(PieCanvas);
        pieVisual.CenterPoint = new Vector3((float)centerX, (float)centerY, 0f);
        pieVisual.Opacity = 0f;
        pieVisual.Scale = new Vector3(0.7f, 0.7f, 1f);
        pieVisual.RotationAngleInDegrees = -90f;

        PrepareFade(PieCenter);
        foreach (var row in legendRows)
        {
            PrepareFade(row);
        }
        PieCenterValue.Text = "0";

        RegisterReveal(PieCanvas, () =>
        {
            var v = VisualOf(PieCanvas);

            try
            {
                var c = v.Compositor;
                var ease = EaseOut(c);

                var fade = c.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(1f, 1f, ease);
                fade.Duration = TimeSpan.FromMilliseconds(900);

                var spin = c.CreateScalarKeyFrameAnimation();
                spin.InsertKeyFrame(1f, 0f, ease);
                spin.Duration = TimeSpan.FromMilliseconds(900);

                var grow = c.CreateVector3KeyFrameAnimation();
                grow.InsertKeyFrame(1f, Vector3.One, ease);
                grow.Duration = TimeSpan.FromMilliseconds(900);

                v.StartAnimation("Opacity", fade);
                v.StartAnimation("RotationAngleInDegrees", spin);
                v.StartAnimation("Scale", grow);
            }
            catch
            {
                ResetVisual(PieCanvas);
            }

            PlayFade(PieCenter, 500, 400);
            ShowNumber(PieCenterValue, total, false, 450);

            for (int i = 0; i < legendRows.Count; i++)
            {
                PlayFade(legendRows[i], 350 + i * 120, 350);
            }
        });
    }

    private static Microsoft.UI.Xaml.Shapes.Path BuildPieSlice(
        double centerX, double centerY, double radius, double startAngleDeg, double endAngleDeg, Color color)
    {
        var startRad = Math.PI * startAngleDeg / 180.0;
        var endRad = Math.PI * endAngleDeg / 180.0;

        var startPoint = new Windows.Foundation.Point(
            centerX + radius * Math.Sin(startRad), centerY - radius * Math.Cos(startRad));
        var endPoint = new Windows.Foundation.Point(
            centerX + radius * Math.Sin(endRad), centerY - radius * Math.Cos(endRad));

        var isLargeArc = (endAngleDeg - startAngleDeg) > 180.0;

        var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(centerX, centerY), IsClosed = true };
        figure.Segments.Add(new LineSegment { Point = startPoint });
        figure.Segments.Add(new ArcSegment
        {
            Point = endPoint,
            Size = new Windows.Foundation.Size(radius, radius),
            IsLargeArc = isLargeArc,
            SweepDirection = SweepDirection.Clockwise
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(color),
            Stroke = ThemeBrush("PawsSurfaceBrush"),
            StrokeThickness = 2
        };
    }

    private void MedicalKpi_Click(object sender, RoutedEventArgs e)
    {
        NavigateToDetails("Medical", "fitForAdoption", "true", "Pets Cleared for Adoption");
    }

    private void TotalIncome_Tapped(object sender, TappedRoutedEventArgs e)
    {
        NavigateToDetails("Payments", "status", "Completed", "Total Income: Completed Payments");
    }

    // The single place every clickable KPI funnels through: switch to the
    // Details tab and pre-apply the matching report type + filter.
    private void NavigateToDetails(string reportType, string filterField, string filterValue, string filterLabel)
    {
        _isApplyingPendingParams = true;

        ReportsPivot.SelectedIndex = 1;
        SelectReportType(reportType);
        RebuildFilterControls(reportType);

        if (filterField == "status")
        {
            SelectComboBoxValue(StatusFilterBox, filterValue);
        }
        else if (filterField == "type")
        {
            SelectComboBoxValue(TypeFilterBox, filterValue);
        }
        else if (filterField == "fitForAdoption")
        {
            FitForAdoptionOnlyBox.IsChecked = true;
        }
        else if (filterField == "branchName")
        {
            foreach (BranchItem b in PaymentsBranchFilterBox.Items)
            {
                if (b.BranchName == filterValue)
                {
                    PaymentsBranchFilterBox.SelectedItem = b;
                    break;
                }
            }
        }

        FilterInfoBar.Title = "Filtered from Overview";
        FilterInfoBar.Message = filterLabel;
        FilterInfoBar.IsOpen = true;

        _isApplyingPendingParams = false;

        _ = LoadDetailsAsync();
    }

    // ----- Details tab: report picker + filtered list -----

    private void SelectReportType(string reportType)
    {
        foreach (ComboBoxItem item in ReportTypeBox.Items)
        {
            if ((string)item.Tag == reportType)
            {
                ReportTypeBox.SelectedItem = item;
                return;
            }
        }
        ReportTypeBox.SelectedIndex = 0;
    }

    private static void SelectComboBoxValue(ComboBox box, string value)
    {
        foreach (var item in box.Items)
        {
            if (item is string s && s == value)
            {
                box.SelectedItem = s;
                return;
            }
        }
    }

    private string CurrentReportType => (ReportTypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Pets";

    private void RebuildFilterControls(string reportType)
    {
        StatusFilterBox.ItemsSource = StatusOptions.TryGetValue(reportType, out var statuses)
            ? new List<string> { "(any)" }.Concat(statuses).ToList()
            : null;
        StatusFilterBox.Visibility = StatusOptions.ContainsKey(reportType) ? Visibility.Visible : Visibility.Collapsed;
        if (StatusFilterBox.Visibility == Visibility.Visible) StatusFilterBox.SelectedIndex = 0;

        TypeFilterBox.ItemsSource = TypeOptions.TryGetValue(reportType, out var types)
            ? new List<string> { "(any)" }.Concat(types).ToList()
            : null;
        TypeFilterBox.Visibility = TypeOptions.ContainsKey(reportType) ? Visibility.Visible : Visibility.Collapsed;
        if (TypeFilterBox.Visibility == Visibility.Visible) TypeFilterBox.SelectedIndex = 0;

        FitForAdoptionOnlyBox.Visibility = reportType == "Medical" ? Visibility.Visible : Visibility.Collapsed;
        if (FitForAdoptionOnlyBox.Visibility == Visibility.Collapsed) FitForAdoptionOnlyBox.IsChecked = false;

        if (reportType == "Payments")
        {
            var withAny = new List<BranchItem> { new BranchItem { BranchId = 0, BranchName = "(any branch)" } };
            withAny.AddRange(_branches);
            PaymentsBranchFilterBox.ItemsSource = withAny;
            PaymentsBranchFilterBox.Visibility = Visibility.Visible;
            PaymentsBranchFilterBox.SelectedIndex = 0;
        }
        else
        {
            PaymentsBranchFilterBox.Visibility = Visibility.Collapsed;
        }
    }

    private async void ReportType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingPendingParams) return;
        RebuildFilterControls(CurrentReportType);
        FilterInfoBar.IsOpen = false;
        await LoadDetailsAsync();
    }

    private async void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingPendingParams) return;
        await LoadDetailsAsync();
    }

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingPendingParams) return;
        await LoadDetailsAsync();
    }

    private async Task LoadDetailsAsync()
    {
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        DetailsEmpty.Visibility = Visibility.Collapsed;

        try
        {
            var reportType = CurrentReportType;
            _rows.Clear();

            switch (reportType)
            {
                case "Pets":
                    await LoadPetsAsync();
                    break;
                case "Applications":
                    await LoadApplicationsAsync();
                    break;
                case "Payments":
                    await LoadPaymentsAsync();
                    break;
                case "CustomerService":
                    await LoadCustomerServiceAsync();
                    break;
                case "Medical":
                    await LoadMedicalAsync();
                    break;
            }

            var countText = _rows.Count == 1 ? "1 record" : $"{_rows.Count} records";
            DetailsCountText.Text = reportType == "Payments" && _lastPayments is { Count: > 0 }
                ? $"{countText} \u2022 Total: \u20b1{_lastPayments.Sum(p => p.Amount):N2}"
                : countText;

            DetailsEmpty.Visibility = _rows.Count == 0 && !ErrorBar.IsOpen
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            ShowError("Could not reach the API.");
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    private string? SelectedStatus =>
        StatusFilterBox.Visibility == Visibility.Visible && StatusFilterBox.SelectedItem is string s && s != "(any)" ? s : null;

    private string? SelectedType =>
        TypeFilterBox.Visibility == Visibility.Visible && TypeFilterBox.SelectedItem is string s && s != "(any)" ? s : null;

    private int? SelectedBranchId =>
        PaymentsBranchFilterBox.Visibility == Visibility.Visible && PaymentsBranchFilterBox.SelectedItem is BranchItem b && b.BranchId > 0
            ? b.BranchId : null;

    private async Task LoadPetsAsync()
    {
        var url = "api/pets" + (SelectedStatus is not null ? $"?status={Uri.EscapeDataString(SelectedStatus)}" : "");
        using var response = await ApiClient.Http.GetAsync(url);
        if (!await HandleResponseAsync(response)) return;

        var items = await response.Content.ReadFromJsonAsync<List<PetItem>>() ?? new();
        _lastPets = items;
        foreach (var p in items)
        {
            _rows.Add(new ReportRow
            {
                TitleText = $"{p.PetName} \u2014 {p.BreedName} ({p.CategoryName})",
                DetailText = $"Status: {p.Status} \u2022 Age: {(p.Age.HasValue ? p.Age.ToString() : "Unknown")} \u2022 " +
                             $"Added: {p.CreatedAt:MMM d, yyyy}" + (p.ListedAt.HasValue ? $" \u2022 Listed: {p.ListedAt:MMM d, yyyy}" : "")
            });
        }
    }

    private async Task LoadApplicationsAsync()
    {
        var url = "api/adoption-applications" + (SelectedStatus is not null ? $"?status={Uri.EscapeDataString(SelectedStatus)}" : "");
        using var response = await ApiClient.Http.GetAsync(url);
        if (!await HandleResponseAsync(response)) return;

        var items = await response.Content.ReadFromJsonAsync<List<AdoptionApplicationItem>>() ?? new();
        _lastApplications = items;
        foreach (var a in items)
        {
            _rows.Add(new ReportRow
            {
                TitleText = $"{a.ApplicationNumber} \u2014 {a.PetName}",
                DetailText = $"Applicant: {a.ApplicantName} \u2022 Status: {a.ApplicationStatus} \u2022 Applied: {a.AppliedAt:MMM d, yyyy}"
            });
        }
    }

    private async Task LoadPaymentsAsync()
    {
        var query = new List<string>();
        if (SelectedStatus is not null) query.Add($"status={Uri.EscapeDataString(SelectedStatus)}");
        if (SelectedType is not null) query.Add($"type={Uri.EscapeDataString(SelectedType)}");
        if (SelectedBranchId is not null) query.Add($"branchId={SelectedBranchId}");
        var url = "api/payments" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

        using var response = await ApiClient.Http.GetAsync(url);
        if (!await HandleResponseAsync(response)) return;

        var items = await response.Content.ReadFromJsonAsync<List<PaymentItem>>() ?? new();
        _lastPayments = items;
        foreach (var p in items)
        {
            var branchText = string.IsNullOrWhiteSpace(p.BranchName) ? "" : $" \u2022 {p.BranchName}";
            _rows.Add(new ReportRow
            {
                TitleText = $"{p.ReferenceNumber} \u2014 {p.Type}",
                DetailText = $"{p.ApplicationNumber} \u2022 \u20b1{p.Amount:N2} \u2022 {p.Method} \u2022 " +
                             $"Status: {p.Status}{branchText} \u2022 {p.CreatedAt:MMM d, yyyy}"
            });
        }
    }

    private async Task LoadCustomerServiceAsync()
    {
        var query = new List<string>();
        if (SelectedStatus is not null) query.Add($"status={Uri.EscapeDataString(SelectedStatus)}");
        if (SelectedType is not null) query.Add($"type={Uri.EscapeDataString(SelectedType)}");
        var url = "api/customer-service" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

        using var response = await ApiClient.Http.GetAsync(url);
        if (!await HandleResponseAsync(response)) return;

        var items = await response.Content.ReadFromJsonAsync<List<CustomerServiceRequestItem>>() ?? new();
        _lastServiceRequests = items;
        foreach (var r in items)
        {
            _rows.Add(new ReportRow
            {
                TitleText = $"{r.Type}: {r.Subject}",
                DetailText = $"From: {r.CustomerName} \u2022 Status: {r.Status} \u2022 {r.CreatedAt:MMM d, yyyy}"
            });
        }
    }

    private async Task LoadMedicalAsync()
    {
        var onlyFit = FitForAdoptionOnlyBox.IsChecked == true;
        var url = "api/medical-records" + (onlyFit ? "?fitForAdoption=true" : "");

        using var response = await ApiClient.Http.GetAsync(url);
        if (!await HandleResponseAsync(response)) return;

        var items = await response.Content.ReadFromJsonAsync<List<MedicalRecordItem>>() ?? new();
        _lastMedicalRecords = items;
        foreach (var m in items)
        {
            _rows.Add(new ReportRow
            {
                TitleText = $"{m.PetName} \u2014 {m.ExaminationDate:MMM d, yyyy}",
                DetailText = $"Vet: {m.VetName} \u2022 Health: {m.HealthStatus}" +
                             (m.FitForAdoption ? " \u2022 Fit for adoption" : "")
            });
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var reportType = CurrentReportType;
        var hasData = reportType switch
        {
            "Pets" => _lastPets is { Count: > 0 },
            "Applications" => _lastApplications is { Count: > 0 },
            "Payments" => _lastPayments is { Count: > 0 },
            "CustomerService" => _lastServiceRequests is { Count: > 0 },
            "Medical" => _lastMedicalRecords is { Count: > 0 },
            _ => false
        };

        if (!hasData)
        {
            ShowError("There is nothing to export for the current filter.");
            return;
        }

        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        picker.SuggestedFileName = $"PAWS-{reportType}-{DateTime.Now:yyyyMMdd-HHmm}";
        picker.FileTypeChoices.Add("Excel Workbook", new List<string> { ".xlsx" });

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            // The person cancelled the dialog. Nothing to do.
            return;
        }

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;

        try
        {
            switch (reportType)
            {
                case "Pets":
                    ReportExporter.ExportPets(file.Path, _lastPets!);
                    break;
                case "Applications":
                    ReportExporter.ExportApplications(file.Path, _lastApplications!);
                    break;
                case "Payments":
                    ReportExporter.ExportPayments(file.Path, _lastPayments!);
                    break;
                case "CustomerService":
                    ReportExporter.ExportCustomerService(file.Path, _lastServiceRequests!);
                    break;
                case "Medical":
                    ReportExporter.ExportMedical(file.Path, _lastMedicalRecords!);
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowError($"Could not save the file: {ex.Message}");
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    // ----- PDF export: same data and filters as the Excel export -----

    private bool HasExportData(string reportType) => reportType switch
    {
        "Pets" => _lastPets is { Count: > 0 },
        "Applications" => _lastApplications is { Count: > 0 },
        "Payments" => _lastPayments is { Count: > 0 },
        "CustomerService" => _lastServiceRequests is { Count: > 0 },
        "Medical" => _lastMedicalRecords is { Count: > 0 },
        _ => false
    };

    // A readable summary of whichever filters are active, printed in the PDF header.
    private string DescribeActiveFilters()
    {
        var parts = new List<string>();

        if (SelectedStatus is not null) parts.Add($"Status: {SelectedStatus}");
        if (SelectedType is not null) parts.Add($"Type: {SelectedType}");

        if (SelectedBranchId is not null && PaymentsBranchFilterBox.SelectedItem is BranchItem branch)
            parts.Add($"Branch: {branch.BranchName}");

        if (FitForAdoptionOnlyBox.Visibility == Visibility.Visible && FitForAdoptionOnlyBox.IsChecked == true)
            parts.Add("Fit for adoption only");

        return parts.Count == 0 ? "None (all records)" : string.Join("  |  ", parts);
    }

    private async void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        var reportType = CurrentReportType;

        if (!HasExportData(reportType))
        {
            ShowError("There is nothing to export for the current filter.");
            return;
        }

        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        picker.SuggestedFileName = $"PAWS-{reportType}-{DateTime.Now:yyyyMMdd-HHmm}";
        picker.FileTypeChoices.Add("PDF Document", new List<string> { ".pdf" });

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return; // The person cancelled the dialog.
        }

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;

        try
        {
            var reportName = (ReportTypeBox.SelectedItem as ComboBoxItem)?.Content as string ?? reportType;

            // Who exported the report, and from which company, come from the signed-in session.
            var exportedBy = string.IsNullOrWhiteSpace(AuthSession.FullName)
                ? null
                : AuthSession.Roles.Count > 0
                    ? $"{AuthSession.FullName} ({string.Join(", ", AuthSession.Roles)})"
                    : AuthSession.FullName;

            var meta = new PdfReportMeta(reportName, AuthSession.CompanyCode, exportedBy, DescribeActiveFilters());
            var path = file.Path;

            // PDF layout can take a moment on big lists, so keep it off the UI thread.
            await Task.Run(() =>
            {
                switch (reportType)
                {
                    case "Pets":
                        PdfReportBuilder.ExportPets(path, _lastPets!, meta);
                        break;
                    case "Applications":
                        PdfReportBuilder.ExportApplications(path, _lastApplications!, meta);
                        break;
                    case "Payments":
                        PdfReportBuilder.ExportPayments(path, _lastPayments!, meta);
                        break;
                    case "CustomerService":
                        PdfReportBuilder.ExportCustomerService(path, _lastServiceRequests!, meta);
                        break;
                    case "Medical":
                        PdfReportBuilder.ExportMedical(path, _lastMedicalRecords!, meta);
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            ShowError($"Could not save the PDF: {ex.Message}");
        }
        finally
        {
            Busy.IsActive = false;
        }
    }
    private async Task<bool> HandleResponseAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return true;

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            AuthSession.Clear();
            RootNavigation.GoToLogin();
        }
        else
        {
            ShowError($"Could not load this report (HTTP {(int)response.StatusCode}).");
        }
        return false;
    }

    private void ShowError(string message)
    {
        ErrorBar.Title = "Error";
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}