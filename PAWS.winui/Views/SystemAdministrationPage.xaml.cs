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
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace PAWS.winui.Views;

public sealed partial class SystemAdministrationPage : Page
{
    // ----- Animation tuning (all times in milliseconds) -----
    private const int MaxAnimatedRows = 12;      // only the first rows animate; the rest are off-screen
    private const double RowStaggerMs = 60;
    private const double CountUpMs = 700;
    private const double BarGrowMs = 650;
    private const double BarStaggerMs = 90;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Column widths of the performance table (the header in the XAML uses the same numbers).
    private static readonly double[] PerfWidths = { 1.7, 0.8, 0.8, 1.0, 1.4, 0.9 };

    public class CompanyRow : INotifyPropertyChanged
    {
        public CompanyItem Source { get; }
        public CompanyRow(CompanyItem source) => Source = source;

        // Looks up a PAWS brush from PawsTheme.xaml
        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        // ----- Company -----
        public string CodeText => Source.CompanyCode;
        public string NameText => Source.CompanyName;

        // ----- Status badge -----
        public string StatusLabel => Source.IsActive ? "ACTIVE" : "SUSPENDED";
        public Brush StatusBadgeBackground => Source.IsActive ? Res("PawsSuccessSoftBrush") : Res("PawsErrorSoftBrush");
        public Brush StatusBadgeForeground => Source.IsActive ? Res("PawsSuccessBrush") : Res("PawsErrorBrush");

        // ----- Terms acknowledgment -----
        public string TermsLabel => Source.TermsAccepted ? "Terms accepted" : "Terms pending";
        public Brush TermsForeground => Source.TermsAccepted ? Res("PawsSuccessBrush") : Res("PawsErrorBrush");

        // ----- Plan badge + branch limit -----
        public string PlanLabel => (Source.SubscriptionPlan ?? string.Empty).ToUpperInvariant();

        public Brush PlanBadgeBackground => Source.SubscriptionPlan switch
        {
            "Premium" => Res("PawsPrimarySoftBrush"),
            "Basic" => Res("PawsInfoSoftBrush"),
            _ => Res("PawsHoverSurfaceBrush")
        };

        public Brush PlanBadgeForeground => Source.SubscriptionPlan switch
        {
            "Premium" => Res("PawsPrimaryBrush"),
            "Basic" => Res("PawsInfoBrush"),
            _ => Res("PawsTextSecondaryBrush")
        };

        public string BranchLimitText => Source.BranchLimit >= int.MaxValue
            ? "Unlimited branches"
            : Source.BranchLimit == 1 ? "1 branch" : $"{Source.BranchLimit} branches";

        // ----- Database -----
        public string DatabaseName => Source.DatabaseName;
        public string ServerText => $"{Source.ServerName} \u2022 {Source.CredentialKey}";

        public string CreatedText => Source.CreatedAt.ToString("MMM d, yyyy");

        public Visibility DeactivateVisibility => Source.IsActive ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ActivateVisibility => !Source.IsActive ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // One choice in the Change Plan dropdown, built from a real SubscriptionPlan.
    public class PlanOption
    {
        public int Id { get; }
        public string Name { get; }
        public string DisplayText { get; }
        public string Summary { get; }

        public PlanOption(SubscriptionPlanItem plan)
        {
            Id = plan.SubscriptionPlanId;
            Name = plan.Name;

            DisplayText = plan.Price == 0
                ? $"{plan.Name} (Free)"
                : $"{plan.Name} (\u20b1{plan.Price.ToString("N0", CultureInfo.InvariantCulture)}/mo)";

            var price = plan.Price == 0
                ? "Free"
                : "\u20b1" + plan.Price.ToString("N2", CultureInfo.InvariantCulture) + " per month";

            var limit = plan.BranchLimit >= int.MaxValue
                ? "unlimited branches"
                : plan.BranchLimit == 1 ? "1 branch" : $"{plan.BranchLimit} branches";

            Summary = $"{plan.Name}: {price} \u2022 {limit}";
        }
    }

    private readonly ObservableCollection<CompanyRow> _rows = new();
    private CompanyRow? _editingRow;
    private CompanyRow? _planRow;
    private bool _hasLoaded;

    // ----- Animation state -----
    private bool _animate = true;
    private bool _animateRows;                   // true only right after the list is (re)loaded
    private bool _overlayWasOpen;

    private readonly DispatcherTimer _rowsWindowTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    // A tween runs a value from 0 to 1 (eased) over a set time and hands it to 'Apply'.
    // The last call always passes exactly 1.0, so the final numbers are exact.
    private sealed class Tween
    {
        public object Key = null!;
        public DateTime Start;
        public Action<double> Apply = null!;
    }

    private readonly List<Tween> _tweens = new();
    private readonly DispatcherTimer _tweenTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    // Business Intelligence animations wait here until the tab is showing, then play once.
    private readonly List<Action> _biPending = new();

    // Layout state for the responsive arrangement.
    private int _biKpiCols, _biRow2Cols, _biRow3Cols;

    // Each company keeps one color in every chart.
    private readonly Dictionary<string, Color> _companyColors = new();

    private readonly List<FrameworkElement> _biCards = new();

    public SystemAdministrationPage()
    {
        InitializeComponent();
        CompaniesList.ItemsSource = _rows;

        // The popup background must follow whichever form panel is showing.
        WatchPanel(EditFormPanel);
        WatchPanel(PlanFormPanel);

        _tweenTimer.Tick += TweenTimer_Tick;

        // After the rows have been created, stop animating them (so later updates are instant).
        _rowsWindowTimer.Tick += (s, e) =>
        {
            _rowsWindowTimer.Stop();
            _animateRows = false;
        };

        _biCards.AddRange(new FrameworkElement[]
        {
            KpiPets, KpiUsers, KpiAdoptions, KpiIncome,
            BiPetsCard, BiUsersCard, BiTableCard,
            BiAdoptionsCard, BiIncomeCard
        });
    }

    // ----- PAWS theme lookups (colors come from PawsTheme.xaml) -----

    private static Color ThemeColor(string key) => (Color)Application.Current.Resources[key];
    private static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

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
            PlaySlideFadeIn(TableCard, 0, 16, 220, 450);
        }

        await LoadAsync();
        await LoadBiAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Never leave a timer running after the page is gone.
        _tweenTimer.Stop();
        _tweens.Clear();
        _rowsWindowTimer.Stop();
        _animateRows = false;
        _biPending.Clear();
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ArrangeLayout(e.NewSize.Width);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
        await LoadBiAsync();
    }

    // Plays the Business Intelligence animations the moment that tab is opened.
    private void MainPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainPivot.SelectedIndex == 1)
        {
            RunBiAnimations();
        }
    }

    // =====================================================================
    // RESPONSIVE LAYOUT
    // =====================================================================

    private void ArrangeLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        // The banner picture shrinks on medium windows and hides on narrow ones, so the text always has room.
        if (width >= 1400)
        {
            HeroPicture.Visibility = Visibility.Visible;
            HeroImage.Height = 170;
        }
        else if (width >= 1050)
        {
            HeroPicture.Visibility = Visibility.Visible;
            HeroImage.Height = 140;
        }
        else
        {
            HeroPicture.Visibility = Visibility.Collapsed;
        }

        // Summary cards: 4 / 2 / 1 across.
        int kpiCols = width >= 1150 ? 4 : width >= 700 ? 2 : 1;
        if (kpiCols != _biKpiCols)
        {
            _biKpiCols = kpiCols;
            ArrangeGrid(BiKpiGrid, new FrameworkElement[] { KpiPets, KpiUsers, KpiAdoptions, KpiIncome },
                kpiCols, null, false);
        }

        // Second row: 3 across (table wider), or 2 across with the table below, or 1.
        int row2Cols = width >= 1450 ? 3 : width >= 850 ? 2 : 1;
        if (row2Cols != _biRow2Cols)
        {
            _biRow2Cols = row2Cols;
            ArrangeGrid(BiRow2Grid, new FrameworkElement[] { BiPetsCard, BiUsersCard, BiTableCard },
                row2Cols, row2Cols == 3 ? new[] { 1.0, 1.0, 1.4 } : null, row2Cols == 2);
        }

        // Third row: 2 across or 1.
        int row3Cols = width >= 850 ? 2 : 1;
        if (row3Cols != _biRow3Cols)
        {
            _biRow3Cols = row3Cols;
            ArrangeGrid(BiRow3Grid, new FrameworkElement[] { BiAdoptionsCard, BiIncomeCard },
                row3Cols, null, false);
        }
    }

    // Lays out cards in a grid: 'cols' columns (optionally weighted); the last card can span the rest of its row.
    private static void ArrangeGrid(Grid grid, IList<FrameworkElement> items, int cols, double[]? weights, bool lastSpansRow)
    {
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();

        for (int c = 0; c < cols; c++)
        {
            double w = weights is not null && c < weights.Length ? weights[c] : 1.0;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        }

        int rows = (int)Math.Ceiling(items.Count / (double)cols);
        for (int r = 0; r < rows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int i = 0; i < items.Count; i++)
        {
            int col = i % cols;
            int span = lastSpansRow && i == items.Count - 1 ? cols - col : 1;

            Grid.SetRow(items[i], i / cols);
            Grid.SetColumn(items[i], col);
            Grid.SetColumnSpan(items[i], span);
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

    // Puts a static card back to normal, so an old (cancelled) animation can never leave it hidden.
    private static void ResetCard(FrameworkElement element)
    {
        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            element.Translation = Vector3.Zero;
        }
        catch
        {
            // Ignore; opacity below is what matters.
        }

        VisualOf(element).Opacity = 1f;
    }

    private static void ResetVisual(UIElement element)
    {
        var v = VisualOf(element);
        v.Opacity = 1f;
        v.Scale = Vector3.One;
        v.RotationAngleInDegrees = 0f;
    }

    // ----- Vertical bars: grow from the baseline -----

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

    // ----- Horizontal bars: sweep in from the left edge -----

    private static void PrepareSweep(UIElement bar)
    {
        var v = VisualOf(bar);
        v.CenterPoint = Vector3.Zero;
        v.Scale = new Vector3(0f, 1f, 1f);
    }

    private static void PlaySweep(UIElement bar, double delayMs)
    {
        var v = VisualOf(bar);

        try
        {
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

    private void RunBiAnimations()
    {
        var pending = _biPending.ToList();
        _biPending.Clear();

        foreach (var play in pending)
        {
            try
            {
                play();
            }
            catch
            {
                // An animation problem must never break the page.
            }
        }
    }

    // ----- Tweens (the count-ups) -----

    private void StartTween(object key, double delayMs, bool animate, Action<double> apply)
    {
        _tweens.RemoveAll(t => t.Key == key);

        if (!animate || !_animate)
        {
            apply(1.0);
            return;
        }

        apply(0.0);

        _tweens.Add(new Tween
        {
            Key = key,
            Start = DateTime.UtcNow.AddMilliseconds(delayMs),
            Apply = apply
        });

        if (!_tweenTimer.IsEnabled)
        {
            _tweenTimer.Start();
        }
    }

    private void TweenTimer_Tick(object? sender, object e)
    {
        var now = DateTime.UtcNow;

        for (int i = _tweens.Count - 1; i >= 0; i--)
        {
            var tw = _tweens[i];
            double elapsed = (now - tw.Start).TotalMilliseconds;
            if (elapsed < 0)
            {
                continue;
            }

            double t = Math.Min(1.0, elapsed / CountUpMs);

            // Ease-out: fast at first, slowing down at the end. The last call passes exactly 1.0.
            double eased = t >= 1.0 ? 1.0 : 1.0 - Math.Pow(1.0 - t, 3);
            tw.Apply(eased);

            if (t >= 1.0)
            {
                _tweens.RemoveAt(i);
            }
        }

        if (_tweens.Count == 0)
        {
            _tweenTimer.Stop();
        }
    }

    private static string CompanyCountLabel(int n) => n == 1 ? "1 company" : $"{n} companies";

    // ----- Rows: slide up and fade in right after a (re)load -----

    private void Row_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_animateRows || sender is not Border row || row.DataContext is not CompanyRow data)
        {
            return;
        }

        int index = _rows.IndexOf(data);
        if (index < 0 || index >= MaxAnimatedRows)
        {
            return;
        }

        PlaySlideFadeIn(row, 0, 14, index * RowStaggerMs, 380);
    }

    // ----- Form popup overlay -----

    // The popup background (FormOverlay) must be visible whenever one of the form
    // panels is visible. Instead of editing every place that shows or hides a
    // panel, we listen for any change to a panel's Visibility and update the overlay.
    private void WatchPanel(Border panel)
    {
        panel.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (s, dp) => UpdateFormOverlay());
    }

    private void UpdateFormOverlay()
    {
        bool anyOpen =
            EditFormPanel.Visibility == Visibility.Visible ||
            PlanFormPanel.Visibility == Visibility.Visible;

        FormOverlay.Visibility = anyOpen ? Visibility.Visible : Visibility.Collapsed;

        // Start every popup with a clean slate.
        PopupErrorBar.IsOpen = false;

        // Animate only when the popup goes from closed to open.
        if (anyOpen && !_overlayWasOpen && _animate)
        {
            PrepareFade(FormOverlay);
            PlayFade(FormOverlay, 0, 180);

            Border openPanel = EditFormPanel.Visibility == Visibility.Visible
                ? EditFormPanel
                : PlanFormPanel;

            PlaySlideFadeIn(openPanel, 0, 18, 40, 260);
        }

        _overlayWasOpen = anyOpen;
    }

    // ----- Table: fill the window, keep columns aligned -----

    // Make the table fill the window when it is wider than the minimum, so the
    // star-sized columns divide the visible width and every row lines up.
    private void TableScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        TableGrid.Width = Math.Max(TableGrid.MinWidth, e.NewSize.Width);
    }

    // ----- Row hover highlight -----

    private void Row_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border row)
        {
            row.Background = (Brush)Application.Current.Resources["PawsBackgroundBrush"];
        }
    }

    private void Row_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border row)
        {
            row.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    private void UpdateEmptyState()
    {
        bool isEmpty = _hasLoaded && _rows.Count == 0 && !Busy.IsActive;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        TableCard.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    // ----- Load companies and system stats -----

    // 'animate' is true when the page opens or Refresh is pressed;
    // false after an edit, a plan change or an activate/deactivate, so the table updates in place.
    private async Task LoadAsync(bool animate = true)
    {
        Busy.IsActive = true;
        ListHost.Opacity = 0.5;
        ErrorBar.IsOpen = false;

        _animate = ReadAnimationSetting();

        try
        {
            using var companiesResponse = await ApiClient.Http.GetAsync("api/companies");

            if (companiesResponse.IsSuccessStatusCode)
            {
                var items = await companiesResponse.Content.ReadFromJsonAsync<List<CompanyItem>>() ?? new();

                // Rows animate only right after a load; give them a short window to be created.
                if (animate && _animate)
                {
                    _animateRows = true;
                    _rowsWindowTimer.Stop();
                    _rowsWindowTimer.Start();
                }

                _rows.Clear();
                foreach (var i in items) _rows.Add(new CompanyRow(i));
                _hasLoaded = true;

                int total = _rows.Count;
                StartTween(TotalText, 300, animate, t => TotalText.Text = CompanyCountLabel((int)Math.Round(total * t)));
            }
            else if (companiesResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthSession.Clear();
                RootNavigation.GoToLogin();
                return;
            }
            else
            {
                ShowError(await ReadMessageAsync(companiesResponse));
                return;
            }

            using var statsResponse = await ApiClient.Http.GetAsync("api/companies/system-stats");
            if (statsResponse.IsSuccessStatusCode)
            {
                var stats = await statsResponse.Content.ReadFromJsonAsync<SystemStats>();
                if (stats is not null)
                {
                    int totalCompanies = Convert.ToInt32(stats.TotalCompanies);
                    int activeCompanies = Convert.ToInt32(stats.ActiveCompanies);
                    int totalUsers = Convert.ToInt32(stats.TotalUsersAcrossAllCompanies);

                    StartTween(TotalCompaniesText, 150, animate,
                        t => TotalCompaniesText.Text = ((int)Math.Round(totalCompanies * t)).ToString());
                    StartTween(ActiveCompaniesText, 250, animate,
                        t => ActiveCompaniesText.Text = ((int)Math.Round(activeCompanies * t)).ToString());
                    StartTween(TotalUsersText, 350, animate,
                        t => TotalUsersText.Text = ((int)Math.Round(totalUsers * t)).ToString());
                }
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            ShowError("Could not reach the API.");
        }
        finally
        {
            Busy.IsActive = false;
            ListHost.Opacity = 1;
            UpdateEmptyState();
        }
    }

    // =====================================================================
    // BUSINESS INTELLIGENCE
    // =====================================================================

    // 'animate' is true when the page opens or Refresh is pressed; false after a plan change.
    private async Task LoadBiAsync(bool animate = true)
    {
        try
        {
            using var response = await ApiClient.Http.GetAsync("api/companies/super-admin-bi");
            if (!response.IsSuccessStatusCode)
            {
                ShowBiMessage("Could not load business intelligence data.");
                return;
            }

            var bi = await response.Content.ReadFromJsonAsync<SuperAdminBi>();
            if (bi is null)
            {
                ShowBiMessage("Could not load business intelligence data.");
                return;
            }

            BiMessage.Visibility = Visibility.Collapsed;

            var pets = ToMap(bi.PetsByCompany);
            var users = ToMap(bi.UsersByCompany);
            var adoptions = ToMap(bi.CompletedAdoptionsByCompany);
            var income = ToMap(bi.IncomeByCompany);
            var branches = ToMap(bi.BranchesByCompany);

            // Every company that appears in any list, sorted by code (PAWS-A, PAWS-B, ...).
            var codes = pets.Keys.Concat(users.Keys).Concat(adoptions.Keys).Concat(income.Keys).Concat(branches.Keys)
                .Distinct()
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _companyColors.Clear();
            for (int i = 0; i < codes.Count; i++)
            {
                _companyColors[codes[i]] = PaletteColor(i);
            }

            // Old animations point at elements that are about to be replaced.
            _biPending.Clear();

            ShowBi(codes, pets, users, adoptions, income, branches, animate && _animate);

            // If the Business Intelligence tab is already showing, play right away;
            // otherwise everything waits and plays when the tab is opened.
            if (MainPivot.SelectedIndex == 1)
            {
                RunBiAnimations();
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or JsonException)
        {
            // Best-effort only; the Companies tab already shows real data,
            // so a failed BI load should not block the rest of the page.
            ShowBiMessage("Could not load business intelligence data.");
        }
    }

    private void ShowBiMessage(string text)
    {
        BiMessage.Text = text;
        BiMessage.Visibility = Visibility.Visible;
    }

    private static Dictionary<string, decimal> ToMap(List<CompanyMetric>? list) =>
        (list ?? new List<CompanyMetric>())
            .GroupBy(m => m.CompanyCode)
            .ToDictionary(g => g.Key, g => g.Sum(m => Convert.ToDecimal(m.Value)));

    private static decimal Get(Dictionary<string, decimal> map, string code) =>
        map.TryGetValue(code, out var v) ? v : 0m;

    // One color per company: coral, amber, blue, purple, then green, pink, teal, slate.
    private static Color PaletteColor(int index)
    {
        switch (index % 8)
        {
            case 0: return ThemeColor("PawsPrimaryColor");
            case 1: return ThemeColor("PawsWarningColor");
            case 2: return ThemeColor("PawsInfoColor");
            case 3: return Color.FromArgb(255, 139, 92, 246);
            case 4: return ThemeColor("PawsSuccessColor");
            case 5: return Color.FromArgb(255, 236, 72, 153);
            case 6: return Color.FromArgb(255, 20, 184, 166);
            default: return ThemeColor("PawsSlateColor");
        }
    }

    private Color ColorOf(string code) =>
        _companyColors.TryGetValue(code, out var c) ? c : ThemeColor("PawsSlateColor");

    private static Color SoftOf(Color c) => Color.FromArgb(40, c.R, c.G, c.B);

    private static TextBlock Txt(string text, double size, Brush foreground, bool bold = false) =>
        new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = foreground,
            FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
        };

    private void ShowBi(
        List<string> codes,
        Dictionary<string, decimal> pets,
        Dictionary<string, decimal> users,
        Dictionary<string, decimal> adoptions,
        Dictionary<string, decimal> income,
        Dictionary<string, decimal> branches,
        bool run)
    {
        // Static cards first go back to normal, so an animation that never played cannot leave one hidden.
        foreach (var card in _biCards)
        {
            ResetCard(card);
        }

        ShowKpi(BiPetsValue, BiPetsSub, BiPetsShare, codes, pets, false, run, 150);
        ShowKpi(BiUsersValue, BiUsersSub, BiUsersShare, codes, users, false, run, 250);
        ShowKpi(BiAdoptionsValue, BiAdoptionsSub, BiAdoptionsShare, codes, adoptions, false, run, 350);
        ShowKpi(BiIncomeValue, BiIncomeSub, BiIncomeShare, codes, income, true, run, 450);

        BuildColumnChart(PetsChart, codes, pets, run, 500);
        DrawUsersDonut(codes, users, run);
        BuildPerformanceTable(codes, pets, users, adoptions, income, branches, run);
        BuildColumnChart(AdoptionsChart, codes, adoptions, run, 700);
        BuildIncomeBars(codes, income, run);

        if (run)
        {
            // The nine cards rise into place one after another.
            for (int i = 0; i < _biCards.Count; i++)
            {
                var card = _biCards[i];
                int index = i;
                PrepareFade(card);
                _biPending.Add(() => PlaySlideFadeIn(card, 0, 18, index * 70, 450));
            }
        }
    }

    // ----- Summary card: number, "top company" line, share bar -----

    private void ShowKpi(TextBlock valueBlock, TextBlock subBlock, Border shareHost, List<string> codes,
        Dictionary<string, decimal> map, bool isMoney, bool run, double delayMs)
    {
        decimal total = codes.Sum(c => Get(map, c));

        string Fmt(decimal v) => isMoney
            ? "\u20b1" + v.ToString("N2", Inv)
            : Math.Round(v).ToString("N0", Inv);

        // Line under the number (real data only).
        if (total <= 0 || codes.Count == 0)
        {
            subBlock.Text = "No data yet";
        }
        else
        {
            var top = codes.OrderByDescending(c => Get(map, c)).First();
            var percent = Math.Round(Get(map, top) / total * 100);
            subBlock.Text = $"{CompanyCountLabel(codes.Count)} \u2022 Top: {top} ({percent}%)";
        }

        // Share bar: one colored segment per company.
        var segments = new Grid();
        var withValue = codes.Where(c => Get(map, c) > 0).ToList();
        for (int i = 0; i < withValue.Count; i++)
        {
            var code = withValue[i];
            segments.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength((double)Get(map, code), GridUnitType.Star)
            });

            var seg = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(ColorOf(code)),
                Margin = new Thickness(i > 0 ? 1 : 0, 0, i < withValue.Count - 1 ? 1 : 0, 0)
            };
            ToolTipService.SetToolTip(seg, $"{code}: {Fmt(Get(map, code))}");
            Grid.SetColumn(seg, i);
            segments.Children.Add(seg);
        }

        shareHost.Child = segments;
        ResetVisual(shareHost);

        if (run)
        {
            valueBlock.Text = Fmt(0);
            PrepareSweep(shareHost);

            _biPending.Add(() =>
            {
                StartTween(valueBlock, delayMs, true, t => valueBlock.Text = Fmt(total * (decimal)t));
                PlaySweep(shareHost, delayMs + 200);
            });
        }
        else
        {
            _tweens.RemoveAll(t => t.Key == valueBlock);
            valueBlock.Text = Fmt(total);
        }
    }

    // ----- Vertical bar chart with a Y axis and gridlines -----

    private static (decimal Max, decimal Step) NiceScale(decimal rawMax, bool integerAxis)
    {
        if (rawMax <= 0)
        {
            return (4m, 1m);
        }

        double raw = (double)rawMax;
        double rough = raw / 5.0;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double norm = rough / mag;
        double stepN = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10;
        double step = stepN * mag;

        if (integerAxis && step < 1)
        {
            step = 1;
        }

        double max = Math.Ceiling(raw / step) * step;
        return ((decimal)max, (decimal)step);
    }

    private void BuildColumnChart(Grid host, List<string> codes, Dictionary<string, decimal> values, bool run, double startDelayMs)
    {
        host.Children.Clear();
        host.RowDefinitions.Clear();
        host.ColumnDefinitions.Clear();

        if (codes.Count == 0)
        {
            host.Children.Add(new TextBlock
            {
                Text = "No data yet.",
                Foreground = ThemeBrush("PawsTextSecondaryBrush")
            });
            return;
        }

        const double plotHeight = 200;
        const double topRoom = 24;       // room above the plot for the value labels
        const double axisWidth = 44;
        const double barWidth = 44;

        decimal rawMax = codes.Max(c => Get(values, c));
        var (max, step) = NiceScale(rawMax, true);
        int ticks = Math.Clamp((int)Math.Round((double)(max / step)), 1, 12);

        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(plotHeight + topRoom) });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(axisWidth) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ----- Gridlines and axis labels -----
        var gridArea = new Grid { Height = plotHeight, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetRow(gridArea, 0);
        Grid.SetColumn(gridArea, 0);
        Grid.SetColumnSpan(gridArea, 2);
        gridArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(axisWidth) });
        gridArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < ticks; i++)
        {
            gridArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        for (int i = 0; i < ticks; i++)
        {
            var line = new Border
            {
                Height = 1,
                VerticalAlignment = VerticalAlignment.Top,
                Background = ThemeBrush("PawsBorderBrush")
            };
            Grid.SetRow(line, i);
            Grid.SetColumn(line, 1);
            gridArea.Children.Add(line);

            var label = Txt(((ticks - i) * step).ToString("0.##", Inv), 11, ThemeBrush("PawsTextSecondaryBrush"));
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = VerticalAlignment.Top;
            label.Margin = new Thickness(0, -8, 8, 0);
            Grid.SetRow(label, i);
            Grid.SetColumn(label, 0);
            gridArea.Children.Add(label);
        }

        var baseLine = new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = ThemeBrush("PawsBorderBrush")
        };
        Grid.SetRow(baseLine, ticks - 1);
        Grid.SetColumn(baseLine, 1);
        gridArea.Children.Add(baseLine);

        var zeroLabel = Txt("0", 11, ThemeBrush("PawsTextSecondaryBrush"));
        zeroLabel.HorizontalAlignment = HorizontalAlignment.Right;
        zeroLabel.VerticalAlignment = VerticalAlignment.Bottom;
        zeroLabel.Margin = new Thickness(0, 0, 8, -8);
        Grid.SetRow(zeroLabel, ticks - 1);
        Grid.SetColumn(zeroLabel, 0);
        gridArea.Children.Add(zeroLabel);

        host.Children.Add(gridArea);

        // ----- Bars (one column per company) and company names -----
        var bars = new Grid();
        Grid.SetRow(bars, 0);
        Grid.SetColumn(bars, 1);

        var names = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetRow(names, 1);
        Grid.SetColumn(names, 1);

        var animated = new List<(Rectangle Bar, TextBlock Label)>();

        for (int i = 0; i < codes.Count; i++)
        {
            string code = codes[i];
            decimal value = Get(values, code);
            double barHeight = value <= 0 ? 2 : Math.Max(4, (double)(value / max) * plotHeight);

            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            names.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var column = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 4
            };

            var valueLabel = Txt(value.ToString("N0", Inv), 13, ThemeBrush("PawsTextPrimaryBrush"), true);
            valueLabel.HorizontalAlignment = HorizontalAlignment.Center;
            column.Children.Add(valueLabel);

            var bar = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                RadiusX = 6,
                RadiusY = 6,
                Fill = new SolidColorBrush(ColorOf(code))
            };
            bar.PointerEntered += (s, e) => bar.Opacity = 0.75;
            bar.PointerExited += (s, e) => bar.Opacity = 1.0;
            ToolTipService.SetToolTip(bar, $"{code}: {value.ToString("N0", Inv)}");
            column.Children.Add(bar);

            Grid.SetColumn(column, i);
            bars.Children.Add(column);

            var name = Txt(code, 11, ThemeBrush("PawsTextSecondaryBrush"));
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, i);
            names.Children.Add(name);

            if (run)
            {
                PrepareGrow(bar, barWidth, barHeight);
                PrepareFade(valueLabel);
                animated.Add((bar, valueLabel));
            }
        }

        host.Children.Add(bars);
        host.Children.Add(names);

        if (run)
        {
            _biPending.Add(() =>
            {
                for (int i = 0; i < animated.Count; i++)
                {
                    PlayGrow(animated[i].Bar, startDelayMs + i * BarStaggerMs);
                    PlayFade(animated[i].Label, startDelayMs + i * BarStaggerMs + BarGrowMs * 0.55);
                }
            });
        }
    }

    // ----- Users by Company: donut with the total in the middle -----

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

    private void DrawUsersDonut(List<string> codes, Dictionary<string, decimal> users, bool run)
    {
        DonutCanvas.Children.Clear();
        DonutLegend.Children.Clear();
        ResetVisual(DonutCanvas);
        ResetVisual(DonutCenter);

        decimal total = codes.Sum(c => Get(users, c));

        if (total <= 0)
        {
            DonutCenter.Visibility = Visibility.Collapsed;
            DonutMessage.Text = "No users yet.";
            DonutMessage.Visibility = Visibility.Visible;
            return;
        }

        DonutMessage.Visibility = Visibility.Collapsed;
        DonutCenter.Visibility = Visibility.Visible;

        const double radius = 90;
        const double center = 90;

        double startAngle = 0;
        var legendRows = new List<FrameworkElement>();

        foreach (var code in codes)
        {
            decimal value = Get(users, code);
            if (value <= 0) continue;

            var color = ColorOf(code);
            double sweep = (double)(value / total) * 360.0;
            double endAngle = startAngle + sweep;
            double percent = Math.Round((double)(value / total) * 100, 1);

            // One company holding 100% of the users is a full circle; an arc with identical
            // start and end points draws nothing, so use an Ellipse.
            Shape slice = sweep >= 359.9
                ? new Ellipse { Width = radius * 2, Height = radius * 2, Fill = new SolidColorBrush(color) }
                : BuildPieSlice(center, center, radius, startAngle, endAngle, color);

            if (slice is Ellipse)
            {
                Canvas.SetLeft(slice, center - radius);
                Canvas.SetTop(slice, center - radius);
            }

            ToolTipService.SetToolTip(slice, $"{code}: {value.ToString("N0", Inv)} ({percent}%)");
            DonutCanvas.Children.Add(slice);

            // Legend row: dot, company, count, percent.
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });

            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dot, 0);

            var name = Txt(code, 13, ThemeBrush("PawsTextPrimaryBrush"));
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);

            var count = Txt(value.ToString("N0", Inv), 13, ThemeBrush("PawsTextPrimaryBrush"), true);
            count.HorizontalAlignment = HorizontalAlignment.Right;
            count.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(count, 2);

            var pct = Txt(percent.ToString("0.0", Inv) + "%", 12, ThemeBrush("PawsTextSecondaryBrush"));
            pct.HorizontalAlignment = HorizontalAlignment.Right;
            pct.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(pct, 3);

            row.Children.Add(dot);
            row.Children.Add(name);
            row.Children.Add(count);
            row.Children.Add(pct);

            DonutLegend.Children.Add(row);
            legendRows.Add(row);

            startAngle = endAngle;
        }

        if (!run)
        {
            _tweens.RemoveAll(t => t.Key == DonutCenterValue);
            DonutCenterValue.Text = Math.Round(total).ToString("N0", Inv);
            return;
        }

        // Start hidden: the donut is small, tilted and transparent; the center and legend are transparent.
        var v = VisualOf(DonutCanvas);
        v.CenterPoint = new Vector3((float)center, (float)center, 0f);
        v.Opacity = 0f;
        v.Scale = new Vector3(0.7f, 0.7f, 1f);
        v.RotationAngleInDegrees = -90f;

        PrepareFade(DonutCenter);
        foreach (var row in legendRows)
        {
            PrepareFade(row);
        }
        DonutCenterValue.Text = "0";

        _biPending.Add(() =>
        {
            try
            {
                var vis = VisualOf(DonutCanvas);
                var c = vis.Compositor;
                var ease = EaseOut(c);

                var fade = c.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(1f, 1f, ease);
                fade.Duration = TimeSpan.FromMilliseconds(900);
                fade.DelayTime = TimeSpan.FromMilliseconds(600);

                var spin = c.CreateScalarKeyFrameAnimation();
                spin.InsertKeyFrame(1f, 0f, ease);
                spin.Duration = TimeSpan.FromMilliseconds(900);
                spin.DelayTime = TimeSpan.FromMilliseconds(600);

                var grow = c.CreateVector3KeyFrameAnimation();
                grow.InsertKeyFrame(1f, Vector3.One, ease);
                grow.Duration = TimeSpan.FromMilliseconds(900);
                grow.DelayTime = TimeSpan.FromMilliseconds(600);

                vis.StartAnimation("Opacity", fade);
                vis.StartAnimation("RotationAngleInDegrees", spin);
                vis.StartAnimation("Scale", grow);
            }
            catch
            {
                ResetVisual(DonutCanvas);
            }

            PlayFade(DonutCenter, 1100, 400);
            StartTween(DonutCenterValue, 1050, true,
                t => DonutCenterValue.Text = Math.Round(total * (decimal)t).ToString("N0", Inv));

            for (int i = 0; i < legendRows.Count; i++)
            {
                PlayFade(legendRows[i], 950 + i * 120, 350);
            }
        });
    }

    // ----- Company Performance Overview (table with a Total row) -----

    private static Grid MakePerfGrid()
    {
        var g = new Grid { ColumnSpacing = 8 };
        foreach (var w in PerfWidths)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        }
        return g;
    }

    private static void AddNumberCell(Grid grid, int column, string text, bool bold)
    {
        var cell = Txt(text, 13, ThemeBrush("PawsTextPrimaryBrush"), bold);
        cell.HorizontalAlignment = HorizontalAlignment.Right;
        cell.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void BuildPerformanceTable(
        List<string> codes,
        Dictionary<string, decimal> pets,
        Dictionary<string, decimal> users,
        Dictionary<string, decimal> adoptions,
        Dictionary<string, decimal> income,
        Dictionary<string, decimal> branches,
        bool run)
    {
        PerfRows.Children.Clear();

        if (codes.Count == 0)
        {
            var empty = Txt("No companies yet.", 13, ThemeBrush("PawsTextSecondaryBrush"));
            empty.Margin = new Thickness(20, 16, 20, 16);
            PerfRows.Children.Add(empty);
            return;
        }

        var animatedRows = new List<FrameworkElement>();

        foreach (var code in codes)
        {
            var color = ColorOf(code);
            var grid = MakePerfGrid();

            // Company chip: colored dot and code on a soft background.
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center
            };
            var chipText = new TextBlock
            {
                Text = code,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var chipContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            chipContent.Children.Add(dot);
            chipContent.Children.Add(chipText);

            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 3, 8, 3),
                Background = new SolidColorBrush(SoftOf(color)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = chipContent
            };
            Grid.SetColumn(chip, 0);
            grid.Children.Add(chip);

            AddNumberCell(grid, 1, Get(pets, code).ToString("N0", Inv), false);
            AddNumberCell(grid, 2, Get(users, code).ToString("N0", Inv), false);
            AddNumberCell(grid, 3, Get(adoptions, code).ToString("N0", Inv), false);
            AddNumberCell(grid, 4, Get(income, code).ToString("N2", Inv), false);
            AddNumberCell(grid, 5, Get(branches, code).ToString("N0", Inv), false);

            var row = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                BorderBrush = ThemeBrush("PawsBorderBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid
            };
            PerfRows.Children.Add(row);
            animatedRows.Add(row);
        }

        // Total row: matches the four summary cards.
        var totalGrid = MakePerfGrid();
        var totalLabel = Txt("Total", 13, ThemeBrush("PawsTextPrimaryBrush"), true);
        totalLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(totalLabel, 0);
        totalGrid.Children.Add(totalLabel);

        AddNumberCell(totalGrid, 1, codes.Sum(c => Get(pets, c)).ToString("N0", Inv), true);
        AddNumberCell(totalGrid, 2, codes.Sum(c => Get(users, c)).ToString("N0", Inv), true);
        AddNumberCell(totalGrid, 3, codes.Sum(c => Get(adoptions, c)).ToString("N0", Inv), true);
        AddNumberCell(totalGrid, 4, codes.Sum(c => Get(income, c)).ToString("N2", Inv), true);
        AddNumberCell(totalGrid, 5, codes.Sum(c => Get(branches, c)).ToString("N0", Inv), true);

        var totalRow = new Border
        {
            Padding = new Thickness(12, 12, 12, 8),
            Background = ThemeBrush("PawsBackgroundBrush"),
            Child = totalGrid
        };
        PerfRows.Children.Add(totalRow);
        animatedRows.Add(totalRow);

        if (run)
        {
            foreach (var row in animatedRows)
            {
                PrepareFade(row);
            }

            _biPending.Add(() =>
            {
                for (int i = 0; i < animatedRows.Count; i++)
                {
                    PlaySlideFadeIn(animatedRows[i], 0, 12, 800 + i * 70, 380);
                }
            });
        }
    }

    // ----- Income by Company: horizontal bars -----

    private void BuildIncomeBars(List<string> codes, Dictionary<string, decimal> income, bool run)
    {
        IncomeBars.Children.Clear();

        if (codes.Count == 0)
        {
            IncomeBars.Children.Add(new TextBlock
            {
                Text = "No data yet.",
                Foreground = ThemeBrush("PawsTextSecondaryBrush")
            });
            return;
        }

        decimal max = Math.Max(1m, codes.Max(c => Get(income, c)));
        var animated = new List<(Border Bar, TextBlock Label)>();

        foreach (var code in codes)
        {
            decimal value = Get(income, code);
            var color = ColorOf(code);

            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = Txt(code, 12, ThemeBrush("PawsTextPrimaryBrush"), true);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, 0);
            row.Children.Add(name);

            // The track has two star columns, so the bar is automatically in proportion to the track width.
            double weight = Math.Max((double)value, (double)max * 0.01);
            var track = new Grid { VerticalAlignment = VerticalAlignment.Center };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(0, (double)max - weight), GridUnitType.Star)
            });

            var bar = new Border
            {
                Height = 20,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(color)
            };
            ToolTipService.SetToolTip(bar, $"{code}: \u20b1{value.ToString("N2", Inv)}");
            Grid.SetColumn(bar, 0);
            track.Children.Add(bar);

            Grid.SetColumn(track, 1);
            row.Children.Add(track);

            var amount = Txt("\u20b1" + value.ToString("N2", Inv), 12, ThemeBrush("PawsTextPrimaryBrush"), true);
            amount.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(amount, 2);
            row.Children.Add(amount);

            IncomeBars.Children.Add(row);

            if (run)
            {
                PrepareSweep(bar);
                PrepareFade(amount);
                animated.Add((bar, amount));
            }
        }

        if (run)
        {
            _biPending.Add(() =>
            {
                for (int i = 0; i < animated.Count; i++)
                {
                    PlaySweep(animated[i].Bar, 900 + i * BarStaggerMs);
                    PlayFade(animated[i].Label, 900 + i * BarStaggerMs + 450);
                }
            });
        }
    }

    // ----- Edit company -----

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not CompanyRow row) return;

        PlanFormPanel.Visibility = Visibility.Collapsed;
        _editingRow = row;
        EditFormTitle.Text = $"Edit {row.Source.CompanyCode}";
        CompanyNameBox.Text = row.Source.CompanyName;
        EditFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        EditFormPanel.Visibility = Visibility.Collapsed;
        _editingRow = null;
    }

    private async void SaveEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_editingRow is null) return;

        var name = CompanyNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Company name is required.");
            return;
        }

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            var request = new UpdateCompanyRequest { CompanyName = name };
            using var response = await ApiClient.Http.PutAsJsonAsync($"api/companies/{_editingRow.Source.CompanyId}", request);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                EditFormPanel.Visibility = Visibility.Collapsed;
                _editingRow = null;
                await LoadAsync(animate: false);
            }
            else
            {
                ShowError(await ReadMessageAsync(response));
            }
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    // ----- Change plan -----

    // Loads the ACTIVE plans from the API every time the popup opens,
    // so new, edited and deactivated plans are always reflected.
    private async void ChangePlan_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not CompanyRow row) return;

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            using var response = await ApiClient.Http.GetAsync("api/subscription-plans");

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    AuthSession.Clear();
                    RootNavigation.GoToLogin();
                    return;
                }

                ShowError(await ReadMessageAsync(response));
                return;
            }

            var plans = await response.Content.ReadFromJsonAsync<List<SubscriptionPlanItem>>() ?? new();
            var options = plans
                .Where(p => p.IsActive)
                .OrderBy(p => p.Price)
                .ThenBy(p => p.Name)
                .Select(p => new PlanOption(p))
                .ToList();

            if (options.Count == 0)
            {
                ShowError("There are no active subscription plans. Create or activate one first.");
                return;
            }

            EditFormPanel.Visibility = Visibility.Collapsed;
            _planRow = row;
            PlanFormTitle.Text = $"Change Plan \u2014 {row.Source.CompanyCode}";

            PlanBox.ItemsSource = options;

            // Preselect the company's current plan (matched by name).
            PlanBox.SelectedItem = options.FirstOrDefault(o => o.Name == row.Source.SubscriptionPlan) ?? options[0];

            PlanFormPanel.Visibility = Visibility.Visible;
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

    private void PlanBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PlanSummaryText.Text = (PlanBox.SelectedItem as PlanOption)?.Summary
            ?? "Choose a plan to see its price and branch limit.";
    }

    private void CancelPlan_Click(object sender, RoutedEventArgs e)
    {
        PlanFormPanel.Visibility = Visibility.Collapsed;
        _planRow = null;
    }

    private async void SavePlan_Click(object sender, RoutedEventArgs e)
    {
        if (_planRow is null) return;

        if (PlanBox.SelectedItem is not PlanOption selected)
        {
            ShowError("Please select a plan.");
            return;
        }

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            // The API now expects the plan's ID, not its name.
            var request = new { subscriptionPlanId = selected.Id };
            using var response = await ApiClient.Http.PostAsJsonAsync($"api/companies/{_planRow.Source.CompanyId}/subscription-plan", request);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                PlanFormPanel.Visibility = Visibility.Collapsed;
                _planRow = null;
                await LoadAsync(animate: false);
                await LoadBiAsync(animate: false);
            }
            else
            {
                ShowError(await ReadMessageAsync(response));
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

    // ----- Activate / Deactivate -----

    private async void Activate_Click(object sender, RoutedEventArgs e) => await SetActive(sender, true);
    private async void Deactivate_Click(object sender, RoutedEventArgs e) => await SetActive(sender, false);

    private async Task SetActive(object sender, bool activate)
    {
        if ((sender as Button)?.Tag is not CompanyRow row) return;

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            var action = activate ? "activate" : "deactivate";
            using var response = await ApiClient.Http.PostAsync($"api/companies/{row.Source.CompanyId}/{action}", null);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                await LoadAsync(animate: false);
            }
            else
            {
                ShowError(await ReadMessageAsync(response));
            }
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    // ----- Helpers -----

    private static async Task<string> ReadMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("message", out var value))
            {
                return value.ToString();
            }
        }
        catch (JsonException)
        {
            // Not JSON. Fall through.
        }
        return $"Request failed (HTTP {(int)response.StatusCode}).";
    }

    private void ShowError(string message)
    {
        ErrorBar.Title = "Error";
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;

        // If a form popup is open, the page-level bar is hidden behind it, so show the error there too.
        if (FormOverlay.Visibility == Visibility.Visible)
        {
            PopupErrorBar.Title = "Error";
            PopupErrorBar.Message = message;
            PopupErrorBar.IsOpen = true;
        }
    }

    private void ShowSuccess(string message)
    {
        SuccessBar.Title = "Success";
        SuccessBar.Message = message;
        SuccessBar.IsOpen = true;
    }
}