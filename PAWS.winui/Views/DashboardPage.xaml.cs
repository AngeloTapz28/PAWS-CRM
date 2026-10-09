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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace PAWS.winui.Views;

public sealed partial class DashboardPage : Page
{
    private const string Placeholder = "\u2013";

    // ----- Animation tuning (all times in milliseconds) -----
    private const double CountUpMs = 700;
    private const double BarGrowMs = 650;
    private const double BarStaggerMs = 90;
    private const double SparkDrawMs = 900;

    private static bool IsAdminOrStaff =>
        AuthSession.Roles.Contains("Administrator") || AuthSession.Roles.Contains("Staff");

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
    private static Color ThemeColor(string key) => (Color)Application.Current.Resources[key];

    private sealed class Counter
    {
        public TextBlock Target = null!;
        public decimal Value;
        public bool IsMoney;
        public DateTime Start;
    }

    private readonly List<Counter> _counters = new();
    private readonly DispatcherTimer _countTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    private readonly List<Border> _kpiCards = new();
    private bool _animate = true;
    private bool _summaryShown;
    private int _kpiCols;
    private int _row2Cols;

    // Pet counts from the summary API (used as a fallback if the pet list cannot be loaded).
    private int _summaryTotal, _summaryAvailable, _summaryAdopted;

    public DashboardPage()
    {
        InitializeComponent();

        _countTimer.Tick += CountTimer_Tick;

        _kpiCards.AddRange(new[] { KpiPets, KpiApps, KpiCustomers, KpiPayments });

        // Hover: each KPI card lifts a little and its border turns coral.
        foreach (var card in _kpiCards)
        {
            card.PointerEntered += Card_PointerEntered;
            card.PointerExited += Card_PointerExited;
        }

        SetGreeting();
        ConfigureQuickActions();
    }

    // =====================================================================
    // PAGE EVENTS
    // =====================================================================

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _animate = ReadAnimationSetting();
        ArrangeLayout(ActualWidth);

        if (_animate)
        {
            // These do not depend on data, so they play right away.
            PlaySlideFadeIn(HeroCard, 0, 12, 0, 450);
            PlaySlideFadeIn(HeroText, -24, 0, 150, 500);
            PlaySlideFadeIn(HeroPicture, 60, 0, 250, 600);

            for (int i = 0; i < _kpiCards.Count; i++)
            {
                PlaySlideFadeIn(_kpiCards[i], 0, 18, 200 + i * 70, 450);
            }

            PlaySlideFadeIn(DonutCard, 0, 18, 520, 450);
            PlaySlideFadeIn(PipelineCard, 0, 18, 590, 450);
            PlaySlideFadeIn(QuickCard, 0, 18, 660, 450);
        }

        await LoadAllAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Never leave a timer running after the page is gone.
        _countTimer.Stop();
        _counters.Clear();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAllAsync();

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ArrangeLayout(e.NewSize.Width);
    }

    // =====================================================================
    // RESPONSIVE LAYOUT: 4 / 2 / 1 KPI columns, 3 / 2 / 1 panels, banner picture by width
    // =====================================================================

    private void ArrangeLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        // The banner picture shrinks on medium windows and hides on narrow ones, so the greeting always has room.
        if (width >= 1400)
        {
            HeroPicture.Visibility = Visibility.Visible;
            HeroImage.Height = 200;
        }
        else if (width >= 1050)
        {
            HeroPicture.Visibility = Visibility.Visible;
            HeroImage.Height = 160;
        }
        else
        {
            HeroPicture.Visibility = Visibility.Collapsed;
        }

        int kpiCols = width >= 1150 ? 4 : width >= 700 ? 2 : 1;
        if (kpiCols != _kpiCols)
        {
            _kpiCols = kpiCols;

            KpiGrid.ColumnDefinitions.Clear();
            KpiGrid.RowDefinitions.Clear();
            for (int c = 0; c < kpiCols; c++)
            {
                KpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            int rows = (int)Math.Ceiling(_kpiCards.Count / (double)kpiCols);
            for (int r = 0; r < rows; r++)
            {
                KpiGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            for (int i = 0; i < _kpiCards.Count; i++)
            {
                Grid.SetRow(_kpiCards[i], i / kpiCols);
                Grid.SetColumn(_kpiCards[i], i % kpiCols);
            }
        }

        int row2Cols = width >= 1450 ? 3 : width >= 850 ? 2 : 1;
        if (row2Cols != _row2Cols)
        {
            _row2Cols = row2Cols;

            Row2Grid.ColumnDefinitions.Clear();
            Row2Grid.RowDefinitions.Clear();

            if (row2Cols == 3)
            {
                Row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
                Row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                Row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Row2Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Place(DonutCard, 0, 0, 1);
                Place(PipelineCard, 0, 1, 1);
                Place(QuickCard, 0, 2, 1);
            }
            else if (row2Cols == 2)
            {
                Row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Row2Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Row2Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Place(DonutCard, 0, 0, 1);
                Place(PipelineCard, 0, 1, 1);
                Place(QuickCard, 1, 0, 2);
            }
            else
            {
                Row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Row2Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Row2Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Row2Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Place(DonutCard, 0, 0, 1);
                Place(PipelineCard, 1, 0, 1);
                Place(QuickCard, 2, 0, 1);
            }
        }
    }

    private static void Place(FrameworkElement element, int row, int column, int columnSpan)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
    }

    // =====================================================================
    // GREETING AND QUICK ACTIONS
    // =====================================================================

    private void SetGreeting()
    {
        int hour = DateTime.Now.Hour;
        string greeting = hour < 12 ? "Good morning"
                        : hour < 18 ? "Good afternoon"
                        : "Good evening";

        // Sun during the day, moon in the evening and at night.
        bool night = hour >= 18 || hour < 5;
        GreetingIcon.Glyph = night ? "\uE708" : "\uE706";
        GreetingIcon.Foreground = night ? Res("PawsInfoBrush") : Res("PawsWarningBrush");

        string? fullName = AuthSession.FullName;
        string firstName = string.IsNullOrWhiteSpace(fullName)
            ? string.Empty
            : fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        GreetingText.Text = string.IsNullOrEmpty(firstName)
            ? greeting
            : $"{greeting}, {firstName}";

        SubtitleText.Text =
            $"Here's what's happening with PAWS today \u2014 {DateTime.Now.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture)}";
    }

    private void ConfigureQuickActions()
    {
        bool adminOrStaff = IsAdminOrStaff;
        bool vet = AuthSession.Roles.Contains("Veterinarian");

        QaPets.Visibility = adminOrStaff || vet ? Visibility.Visible : Visibility.Collapsed;
        QaApplications.Visibility = adminOrStaff ? Visibility.Visible : Visibility.Collapsed;
        QaPayments.Visibility = adminOrStaff ? Visibility.Visible : Visibility.Collapsed;
        QaRequests.Visibility = adminOrStaff ? Visibility.Visible : Visibility.Collapsed;
        QaMedical.Visibility = vet && !adminOrStaff ? Visibility.Visible : Visibility.Collapsed;
    }

    private void QuickAction_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag)
        {
            ShellPage.GoTo(tag);
        }
    }

    private void Qa_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border row) return;

        if (row.Name != "QaPets")
        {
            row.Background = Res("PawsHoverSurfaceBrush");
        }

        if ((row.Child as Grid)?.Children.LastOrDefault() is FrameworkElement chevron)
        {
            AnimateShift(chevron, 4f);
        }
    }

    private void Qa_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border row) return;

        if (row.Name != "QaPets")
        {
            row.Background = Res("PawsBackgroundBrush");
        }

        if ((row.Child as Grid)?.Children.LastOrDefault() is FrameworkElement chevron)
        {
            AnimateShift(chevron, 0f);
        }
    }

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

    private static void PrepareFade(UIElement element)
    {
        VisualOf(element).Opacity = 0f;
    }

    private static void PlayFade(UIElement element, double delayMs, double durationMs = 350)
    {
        var v = VisualOf(element);

        try
        {
            v.Opacity = 0f;

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

    // Fade in while sliding in from a small offset (x and/or y).
    private static void PlaySlideFadeIn(FrameworkElement element, double fromX, double fromY, double delayMs, double durationMs)
    {
        var v = VisualOf(element);

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);

            v.Opacity = 0f;
            element.Translation = new Vector3((float)fromX, (float)fromY, 0f);

            var c = v.Compositor;
            var ease = EaseOut(c);

            var fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 1f, ease);
            fade.Duration = TimeSpan.FromMilliseconds(durationMs);
            fade.DelayTime = TimeSpan.FromMilliseconds(delayMs);

            var slide = c.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(1f, Vector3.Zero, ease);
            slide.Duration = TimeSpan.FromMilliseconds(durationMs);
            slide.DelayTime = TimeSpan.FromMilliseconds(delayMs);

            v.StartAnimation("Opacity", fade);
            v.StartAnimation("Translation", slide);
        }
        catch
        {
            // An animation problem must never leave something invisible.
            v.Opacity = 1f;
            element.Translation = Vector3.Zero;
        }
    }

    // Smoothly moves an element to a vertical offset (card hover lift).
    private static void AnimateLift(FrameworkElement element, float toY, double durationMs = 150)
    {
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);

            var v = VisualOf(element);
            var c = v.Compositor;
            var a = c.CreateVector3KeyFrameAnimation();
            a.InsertKeyFrame(1f, new Vector3(0f, toY, 0f), EaseOut(c));
            a.Duration = TimeSpan.FromMilliseconds(durationMs);
            v.StartAnimation("Translation", a);
        }
        catch
        {
            element.Translation = new Vector3(0f, toY, 0f);
        }
    }

    // Smoothly moves an element to a horizontal offset (the quick-action chevron).
    private static void AnimateShift(FrameworkElement element, float toX, double durationMs = 150)
    {
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);

            var v = VisualOf(element);
            var c = v.Compositor;
            var a = c.CreateVector3KeyFrameAnimation();
            a.InsertKeyFrame(1f, new Vector3(toX, 0f, 0f), EaseOut(c));
            a.Duration = TimeSpan.FromMilliseconds(durationMs);
            v.StartAnimation("Translation", a);
        }
        catch
        {
            element.Translation = new Vector3(toX, 0f, 0f);
        }
    }

    private static void ResetVisual(UIElement element)
    {
        var v = VisualOf(element);
        v.Opacity = 1f;
        v.Scale = Vector3.One;
        v.RotationAngleInDegrees = 0f;
    }

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

    // Sweeps a bar in from the left edge.
    private static void SweepIn(FrameworkElement bar, double delayMs)
    {
        var v = VisualOf(bar);

        try
        {
            v.CenterPoint = Vector3.Zero;
            v.Scale = new Vector3(0f, 1f, 1f);

            var c = v.Compositor;
            var a = c.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(1f, 1f, EaseOut(c));
            a.Duration = TimeSpan.FromMilliseconds(900);
            a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
            v.StartAnimation("Scale.X", a);
        }
        catch
        {
            v.Scale = Vector3.One;
        }
    }

    // ----- Card hover -----

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            AnimateLift(card, -4f);
            card.BorderBrush = Res("PawsPrimaryBrush");
        }
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            AnimateLift(card, 0f);
            card.BorderBrush = Res("PawsBorderBrush");
        }
    }

    // ----- Count-up numbers -----

    private static string FormatNumber(decimal value, bool isMoney) =>
        isMoney
            ? "\u20b1" + value.ToString("N2", CultureInfo.InvariantCulture)
            : Math.Round(value).ToString("N0", CultureInfo.CurrentCulture);

    private void ShowNumber(TextBlock target, decimal value, bool isMoney, double delayMs)
    {
        _counters.RemoveAll(c => c.Target == target);

        if (!_animate)
        {
            target.Text = FormatNumber(value, isMoney);
            return;
        }

        target.Text = FormatNumber(0, isMoney);

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

    // =====================================================================
    // LOAD DATA
    // =====================================================================

    private async Task LoadAllAsync()
    {
        RefreshButton.IsEnabled = false;
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;

        _animate = ReadAnimationSetting();
        _summaryShown = false;
        _counters.Clear();

        ResetTexts();

        try
        {
            await LoadSummaryAsync();

            if (!_summaryShown)
            {
                return;
            }

            // The panels load side by side; each one handles its own failure.
            await Task.WhenAll(
                LoadPetsAsync(),
                LoadApplicationsAsync(),
                LoadPaymentsAsync(),
                LoadRetentionAsync());
        }
        finally
        {
            Busy.IsActive = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void ResetTexts()
    {
        PetsDelta.Text = " ";
        AppsDelta.Text = " ";
        CustomersDelta.Text = " ";
        PaymentsDelta.Text = " ";
        PetsFooter.Text = "All pets in the system";
        AppsFooter.Text = "Awaiting review";
        CustomersFooter.Text = "Registered adopters";
        PaymentsFooter.Text = "Completed payments only, assumed PHP";
    }

    private async Task LoadSummaryAsync()
    {
        try
        {
            using var response = await ApiClient.Http.GetAsync("api/dashboard/summary");

            if (response.IsSuccessStatusCode)
            {
                var summary = await response.Content.ReadFromJsonAsync<DashboardSummary>();

                if (summary is null)
                {
                    ShowError("The API sent an empty reply.");
                    ClearCards();
                    return;
                }

                ShowSummary(summary);
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthSession.Clear();
                RootNavigation.GoToLogin();
            }
            else
            {
                string traceId = await ReadTraceIdAsync(response);
                ShowError($"The API reported an error (HTTP {(int)response.StatusCode}). " +
                          $"Trace ID: {traceId}");
                ClearCards();
            }
        }
        catch (TaskCanceledException)
        {
            ShowError("The API did not answer in time. Check that PAWS.api is running " +
                      "and that your VPN is on, then try once more.");
            ClearCards();
        }
        catch (HttpRequestException ex)
        {
            ShowError($"Could not reach the API. {ex.Message}");
            ClearCards();
        }
        catch (JsonException)
        {
            ShowError("The API reply was not in the expected format.");
            ClearCards();
        }
    }

    private void ShowSummary(DashboardSummary s)
    {
        _summaryShown = true;

        int total = Convert.ToInt32(s.TotalPets);
        int available = Convert.ToInt32(s.AvailablePets);
        int adopted = Convert.ToInt32(s.AdoptedPets);
        int other = Math.Max(0, total - available - adopted);
        int open = Convert.ToInt32(s.CustomerServiceRequestsOpen);

        _summaryTotal = total;
        _summaryAvailable = available;
        _summaryAdopted = adopted;

        // Administrator and Staff get the pet total from the pet list (the same list as the donut and the
        // Pets page), so the card and the donut always match. Veterinarians use the summary numbers.
        if (!IsAdminOrStaff)
        {
            ShowNumber(TotalPetsValue, total, false, 200);
        }

        ShowNumber(PendingApplicationsValue, Convert.ToInt32(s.PendingApplications), false, 270);
        ShowNumber(TotalCustomersValue, Convert.ToInt32(s.TotalCustomers), false, 340);
        ShowNumber(PaymentsValue, Convert.ToDecimal(s.TotalPaymentsCompleted), true, 410);

        if (!IsAdminOrStaff)
        {
            PetsFooter.Text = $"{available} available \u2022 {adopted} adopted \u2022 {other} others";
        }

        SetDelta(CustomersDelta,
            open > 0 ? $"{open} open service request{(open == 1 ? "" : "s")}" : "No open service requests",
            open == 0);

        // Veterinarians do not see the pet list, so the donut falls back to the summary counts.
        if (!IsAdminOrStaff)
        {
            DrawDonut(new List<(string, int, Color)>
            {
                ("Available", available, ThemeColor("PawsSuccessColor")),
                ("Adopted", adopted, ThemeColor("PawsInfoColor")),
                ("Pending / other", other, ThemeColor("PawsWarningColor"))
            });
        }
    }

    private void ClearCards()
    {
        _countTimer.Stop();
        _counters.Clear();

        TotalPetsValue.Text = Placeholder;
        PendingApplicationsValue.Text = Placeholder;
        TotalCustomersValue.Text = Placeholder;
        PaymentsValue.Text = Placeholder;

        PetsSpark.Children.Clear();
        AppsSpark.Children.Clear();
        PaymentsSpark.Children.Clear();

        DonutCanvas.Children.Clear();
        DonutLegend.Children.Clear();
        PipelineContainer.Children.Clear();
        DonutCenter.Visibility = Visibility.Collapsed;
        DonutMessage.Text = "No data to show.";
        DonutMessage.Visibility = Visibility.Visible;
        PipelineMessage.Text = "No data to show.";
        PipelineMessage.Visibility = Visibility.Visible;
    }

    // Fetches a list. Returns null if the API did not answer or refused (the panel shows its own message).
    private async Task<List<T>?> GetListAsync<T>(string url)
    {
        try
        {
            using var response = await ApiClient.Http.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<T>>() ?? new List<T>();
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthSession.Clear();
                RootNavigation.GoToLogin();
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or JsonException)
        {
            // Fall through to null.
        }

        return null;
    }

    private void SetDelta(TextBlock target, string text, bool neutral)
    {
        target.Text = text;
        target.Foreground = neutral ? Res("PawsTextSecondaryBrush") : Res("PawsSuccessBrush");
    }

    // =====================================================================
    // PETS: card, sparkline, donut
    // =====================================================================

    private async Task LoadPetsAsync()
    {
        if (!IsAdminOrStaff)
        {
            PetsSpark.Children.Clear();
            return;
        }

        var pets = await GetListAsync<PetItem>("api/pets");

        if (pets is null)
        {
            PetsSpark.Children.Clear();
            DonutMessage.Text = "Could not load pet data.";
            DonutMessage.Visibility = Visibility.Visible;
            DonutCenter.Visibility = Visibility.Collapsed;

            // The list failed, so the card falls back to the summary numbers instead of staying empty.
            ShowPetCard(_summaryTotal, _summaryAvailable, _summaryAdopted);
            return;
        }

        ShowPetCard(
            pets.Count,
            pets.Count(p => p.Status == "Available"),
            pets.Count(p => p.Status == "Adopted"));

        var daily = DailyBuckets(pets.Select(p => (p.CreatedAt, 1.0)));
        DrawSpark(PetsSpark, daily, ThemeColor("PawsPrimaryColor"));

        int week = (int)daily.Skip(7).Sum();
        SetDelta(PetsDelta, week > 0 ? $"\u2191 {week} added this week" : "No new pets this week", week == 0);

        int Count(string status) => pets.Count(p => p.Status == status);

        DrawDonut(new List<(string, int, Color)>
        {
            ("Available", Count("Available"), ThemeColor("PawsSuccessColor")),
            ("Adopted", Count("Adopted"), ThemeColor("PawsInfoColor")),
            ("Pending", Count("Pending"), ThemeColor("PawsWarningColor")),
            ("Under Treatment", Count("Under Treatment"), ThemeColor("PawsPrimaryColor")),
            ("Unavailable", Count("Unavailable"), ThemeColor("PawsSlateColor"))
        });
    }

    // Fills the Total Pets card and its footer from one set of counts.
    private void ShowPetCard(int total, int available, int adopted)
    {
        ShowNumber(TotalPetsValue, total, false, 200);
        PetsFooter.Text =
            $"{available} available \u2022 {adopted} adopted \u2022 {Math.Max(0, total - available - adopted)} others";
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
            Stroke = Res("PawsSurfaceBrush"),
            StrokeThickness = 2
        };
    }

    private void DrawDonut(List<(string Label, int Value, Color Color)> parts)
    {
        DonutCanvas.Children.Clear();
        DonutLegend.Children.Clear();
        ResetVisual(DonutCanvas);
        ResetVisual(DonutCenter);

        int total = parts.Sum(p => p.Value);

        if (total == 0)
        {
            DonutCenter.Visibility = Visibility.Collapsed;
            DonutMessage.Text = "No pets yet.";
            DonutMessage.Visibility = Visibility.Visible;
            return;
        }

        DonutMessage.Visibility = Visibility.Collapsed;
        DonutCenter.Visibility = Visibility.Visible;

        const double radius = 90;
        const double center = 90;

        double startAngle = 0;
        var legendRows = new List<FrameworkElement>();

        foreach (var part in parts)
        {
            if (part.Value <= 0) continue;

            double sweep = part.Value / (double)total * 360.0;
            double endAngle = startAngle + sweep;
            double percent = Math.Round(part.Value / (double)total * 100, 1);

            // One status holding 100% of the pets is a full circle; an arc with identical
            // start and end points draws nothing, so use an Ellipse.
            Shape slice = sweep >= 359.9
                ? new Ellipse { Width = radius * 2, Height = radius * 2, Fill = new SolidColorBrush(part.Color) }
                : BuildPieSlice(center, center, radius, startAngle, endAngle, part.Color);

            if (slice is Ellipse)
            {
                Canvas.SetLeft(slice, center - radius);
                Canvas.SetTop(slice, center - radius);
            }

            ToolTipService.SetToolTip(slice, $"{part.Label}: {part.Value} ({percent}%)");
            DonutCanvas.Children.Add(slice);

            // Legend row: dot, name, count, percent.
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });

            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(part.Color),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dot, 0);

            var name = new TextBlock
            {
                Text = part.Label,
                FontSize = 13,
                Foreground = Res("PawsTextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(name, 1);

            var count = new TextBlock
            {
                Text = part.Value.ToString(),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Res("PawsTextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(count, 2);

            var pct = new TextBlock
            {
                Text = percent.ToString("0.0", CultureInfo.InvariantCulture) + "%",
                FontSize = 12,
                Foreground = Res("PawsTextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(pct, 3);

            row.Children.Add(dot);
            row.Children.Add(name);
            row.Children.Add(count);
            row.Children.Add(pct);

            DonutLegend.Children.Add(row);
            legendRows.Add(row);

            startAngle = endAngle;
        }

        if (!_animate)
        {
            DonutCenterValue.Text = total.ToString("N0", CultureInfo.CurrentCulture);
            return;
        }

        // The donut spins in while growing; the total counts up and the legend fades in row by row.
        try
        {
            var v = VisualOf(DonutCanvas);
            var c = v.Compositor;
            var ease = EaseOut(c);

            v.CenterPoint = new Vector3((float)center, (float)center, 0f);
            v.Opacity = 0f;
            v.Scale = new Vector3(0.7f, 0.7f, 1f);
            v.RotationAngleInDegrees = -90f;

            var fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 1f, ease);
            fade.Duration = TimeSpan.FromMilliseconds(900);
            fade.DelayTime = TimeSpan.FromMilliseconds(250);

            var spin = c.CreateScalarKeyFrameAnimation();
            spin.InsertKeyFrame(1f, 0f, ease);
            spin.Duration = TimeSpan.FromMilliseconds(900);
            spin.DelayTime = TimeSpan.FromMilliseconds(250);

            var grow = c.CreateVector3KeyFrameAnimation();
            grow.InsertKeyFrame(1f, Vector3.One, ease);
            grow.Duration = TimeSpan.FromMilliseconds(900);
            grow.DelayTime = TimeSpan.FromMilliseconds(250);

            v.StartAnimation("Opacity", fade);
            v.StartAnimation("RotationAngleInDegrees", spin);
            v.StartAnimation("Scale", grow);
        }
        catch
        {
            ResetVisual(DonutCanvas);
        }

        PlayFade(DonutCenter, 750, 400);
        ShowNumber(DonutCenterValue, total, false, 700);

        for (int i = 0; i < legendRows.Count; i++)
        {
            PlayFade(legendRows[i], 600 + i * 120, 350);
        }
    }

    // =====================================================================
    // APPLICATIONS: sparkline, pipeline
    // =====================================================================

    private async Task LoadApplicationsAsync()
    {
        if (!IsAdminOrStaff)
        {
            AppsSpark.Children.Clear();
            PipelineContainer.Children.Clear();
            PipelineMessage.Text = "Available to Administrator and Staff.";
            PipelineMessage.Visibility = Visibility.Visible;
            return;
        }

        var apps = await GetListAsync<AdoptionApplicationItem>("api/adoption-applications");

        if (apps is null)
        {
            AppsSpark.Children.Clear();
            PipelineContainer.Children.Clear();
            PipelineMessage.Text = "Could not load application data.";
            PipelineMessage.Visibility = Visibility.Visible;
            return;
        }

        PipelineMessage.Visibility = Visibility.Collapsed;

        var daily = DailyBuckets(apps.Select(a => (a.AppliedAt, 1.0)));
        DrawSpark(AppsSpark, daily, ThemeColor("PawsWarningColor"));

        int week = (int)daily.Skip(7).Sum();
        SetDelta(AppsDelta, week > 0 ? $"\u2191 {week} submitted this week" : "No new applications this week", week == 0);

        int Count(string status) => apps.Count(a => a.ApplicationStatus == status);

        int approved = Count("Approved");
        int completed = Count("Completed");
        int rejected = Count("Rejected");
        AppsFooter.Text = $"{approved} approved \u2022 {completed} completed \u2022 {rejected} rejected";

        DrawPipeline(new List<(string, string, int, Color)>
        {
            ("Submitted", "Awaiting review", Count("Submitted"), ThemeColor("PawsPrimaryColor")),
            ("Under Review", "In progress", Count("Under Review") + Count("Interview"), ThemeColor("PawsWarningColor")),
            ("Approved", "Ready to complete", approved, ThemeColor("PawsSuccessColor")),
            ("Completed", "Adopted", completed, ThemeColor("PawsInfoColor"))
        });
    }

    private void DrawPipeline(List<(string Label, string Caption, int Value, Color Color)> stages)
    {
        PipelineContainer.Children.Clear();

        const double maxBarHeight = 130;
        const double barWidth = 60;

        int max = Math.Max(1, stages.Max(s => s.Value));
        var animated = new List<(Rectangle Bar, TextBlock Label)>();

        foreach (var stage in stages)
        {
            double barHeight = stage.Value == 0 ? 2 : Math.Max(6, stage.Value / (double)max * maxBarHeight);

            var column = new StackPanel
            {
                Width = 92,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            var valueLabel = new TextBlock
            {
                Text = stage.Value.ToString("N0", CultureInfo.CurrentCulture),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Res("PawsTextPrimaryBrush")
            };
            column.Children.Add(valueLabel);

            var bar = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                RadiusX = 8,
                RadiusY = 8,
                Fill = new SolidColorBrush(stage.Color),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ToolTipService.SetToolTip(bar, $"{stage.Label}: {stage.Value}");
            column.Children.Add(bar);

            column.Children.Add(new TextBlock
            {
                Text = stage.Label,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Res("PawsTextPrimaryBrush")
            });

            column.Children.Add(new TextBlock
            {
                Text = stage.Caption,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = Res("PawsTextSecondaryBrush")
            });

            PipelineContainer.Children.Add(column);

            if (_animate)
            {
                PrepareGrow(bar, barWidth, barHeight);
                PrepareFade(valueLabel);
                animated.Add((bar, valueLabel));
            }
        }

        // Bars grow one after another; labels fade in as each one finishes.
        for (int i = 0; i < animated.Count; i++)
        {
            PlayGrow(animated[i].Bar, 500 + i * BarStaggerMs);
            PlayFade(animated[i].Label, 500 + i * BarStaggerMs + BarGrowMs * 0.55);
        }
    }

    // =====================================================================
    // PAYMENTS: sparkline
    // =====================================================================

    private async Task LoadPaymentsAsync()
    {
        if (!IsAdminOrStaff)
        {
            PaymentsSpark.Children.Clear();
            return;
        }

        var payments = await GetListAsync<PaymentItem>("api/payments");

        if (payments is null)
        {
            PaymentsSpark.Children.Clear();
            return;
        }

        var completed = payments.Where(p => p.Status == "Completed");
        var daily = DailyBuckets(completed.Select(p => (p.PaidAt ?? p.CreatedAt, (double)p.Amount)));
        DrawSpark(PaymentsSpark, daily, ThemeColor("PawsSuccessColor"));

        decimal week = (decimal)daily.Skip(7).Sum();
        SetDelta(PaymentsDelta,
            week > 0 ? $"\u2191 \u20b1{week.ToString("N2", CultureInfo.InvariantCulture)} this week" : "No completed payments this week",
            week == 0);
    }

    // =====================================================================
    // CUSTOMERS: retention split bar
    // =====================================================================

    private async Task LoadRetentionAsync()
    {
        if (!IsAdminOrStaff)
        {
            return;
        }

        var customers = await GetListAsync<CustomerRetentionItem>("api/customer-retention");

        if (customers is null)
        {
            return;
        }

        int Count(string status) => customers.Count(c => c.RetentionStatus == status);

        int overdue = Count("Overdue");
        int dueSoon = Count("Due Soon");
        int onTrack = Count("On Track");
        int notScheduled = Count("Not Scheduled");

        RetOverdueCol.Width = new GridLength(overdue, GridUnitType.Star);
        RetDueSoonCol.Width = new GridLength(dueSoon, GridUnitType.Star);
        RetOnTrackCol.Width = new GridLength(onTrack, GridUnitType.Star);
        RetNotSchedCol.Width = new GridLength(notScheduled, GridUnitType.Star);

        CustomersFooter.Text = $"{overdue} overdue \u2022 {dueSoon} due soon \u2022 {onTrack} on track";

        if (_animate)
        {
            SweepIn(RetentionBar, 500);
        }
        else
        {
            ResetVisual(RetentionBar);
        }
    }

    // =====================================================================
    // SPARKLINES
    // =====================================================================

    // Adds up values per day for the last 14 days (index 13 = today).
    private static double[] DailyBuckets(IEnumerable<(DateTime Date, double Amount)> items)
    {
        var buckets = new double[14];
        var today = DateTime.Today;

        foreach (var (date, amount) in items)
        {
            int daysAgo = (today - date.Date).Days;
            if (daysAgo >= 0 && daysAgo < 14)
            {
                buckets[13 - daysAgo] += amount;
            }
        }

        return buckets;
    }

    private void DrawSpark(Canvas canvas, double[] values, Color color)
    {
        canvas.Children.Clear();

        var visual = VisualOf(canvas);
        visual.Clip = null;

        double w = canvas.Width;
        double h = canvas.Height;
        const double pad = 4;

        double max = Math.Max(1, values.Max());
        int n = values.Length;

        var line = new PointCollection();
        var area = new PointCollection();

        for (int i = 0; i < n; i++)
        {
            double x = pad + i * (w - 2 * pad) / (n - 1);
            double y = h - pad - values[i] / max * (h - 2 * pad);
            var point = new Windows.Foundation.Point(x, y);
            line.Add(point);
            area.Add(point);
        }

        area.Add(new Windows.Foundation.Point(w - pad, h));
        area.Add(new Windows.Foundation.Point(pad, h));

        canvas.Children.Add(new Polygon
        {
            Points = area,
            Fill = new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B))
        });

        canvas.Children.Add(new Polyline
        {
            Points = line,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2.2,
            StrokeLineJoin = PenLineJoin.Round
        });

        if (!_animate)
        {
            return;
        }

        // The line and its fill are revealed from left to right.
        try
        {
            var c = visual.Compositor;
            var clip = c.CreateInsetClip(0f, 0f, (float)w, 0f);
            visual.Clip = clip;

            var a = c.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(1f, 0f, EaseOut(c));
            a.Duration = TimeSpan.FromMilliseconds(SparkDrawMs);
            a.DelayTime = TimeSpan.FromMilliseconds(450);
            clip.StartAnimation("RightInset", a);
        }
        catch
        {
            visual.Clip = null;
        }
    }

    // =====================================================================
    // HELPERS
    // =====================================================================

    private static async Task<string> ReadTraceIdAsync(HttpResponseMessage response)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("traceId", out var value))
            {
                return value.ToString();
            }
        }
        catch (JsonException)
        {
            // The reply was not JSON. Fall through.
        }

        return "(none)";
    }

    private void ShowError(string message)
    {
        ErrorBar.Title = "Could not load the dashboard";
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}