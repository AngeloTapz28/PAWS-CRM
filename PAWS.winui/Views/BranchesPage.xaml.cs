using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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

public sealed partial class BranchesPage : Page
{
    // ----- Animation tuning -----
    private const int MaxAnimatedRows = 12;
    private const double RowStaggerMs = 55;
    private const double CountUpMs = 700;

    public class BranchRow : INotifyPropertyChanged
    {
        public BranchItem Source { get; }
        public BranchRow(BranchItem source) => Source = source;

        // Looks up a PAWS brush from PawsTheme.xaml
        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        public string NameText => Source.BranchName;

        // ----- Status badge -----
        public string StatusLabel => Source.IsActive ? "ACTIVE" : "INACTIVE";
        public Brush StatusBadgeBackground => Source.IsActive ? Res("PawsSuccessSoftBrush") : Res("PawsErrorSoftBrush");
        public Brush StatusBadgeForeground => Source.IsActive ? Res("PawsSuccessBrush") : Res("PawsErrorBrush");

        public string AddressDisplay =>
            !string.IsNullOrWhiteSpace(Source.Address) ? Source.Address : "\u2014";

        public string PetCountValue => Source.PetCount.ToString();

        public Visibility DeactivateVisibility => Source.IsActive ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ActivateVisibility => !Source.IsActive ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<BranchRow> _rows = new();
    private int? _editingBranchId;
    private bool _hasLoaded;

    // The current plan's name and terms, kept for the View Terms popup.
    private string _planName = string.Empty;
    private string _termsText = string.Empty;
    private bool _termsAccepted;

    // ----- Animation state -----
    private bool _animate = true;
    private bool _animateRows;                   // true only right after the list is (re)loaded
    private bool _overlayWasOpen;

    private readonly DispatcherTimer _rowsWindowTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    // A tween runs a value from 0 to 1 (eased) over a set time and hands it to 'Apply'.
    // The last call always passes exactly 1.0, so final numbers are exact.
    private sealed class Tween
    {
        public object Key = null!;
        public DateTime Start;
        public double Ms;
        public Action<double> Apply = null!;
    }

    private readonly List<Tween> _tweens = new();
    private readonly DispatcherTimer _tweenTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    public BranchesPage()
    {
        InitializeComponent();
        BranchesList.ItemsSource = _rows;

        // The popup background must follow whichever panel is showing.
        WatchPanel(FormPanel);
        WatchPanel(TermsPanel);

        _tweenTimer.Tick += TweenTimer_Tick;

        // After the rows have been created, stop animating them (so later updates are instant).
        _rowsWindowTimer.Tick += (s, e) =>
        {
            _rowsWindowTimer.Stop();
            _animateRows = false;
        };
    }

    private static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _animate = ReadAnimationSetting();

        if (_animate)
        {
            // The header fades in, then the plan card and the table card rise into place.
            PrepareFade(HeaderPanel);
            PlayFade(HeaderPanel, 0, 420);
            PlaySlideFadeIn(PlanCard, 16, 110, 450);
            PlaySlideFadeIn(TableCard, 16, 220, 450);
        }

        await LoadPlanInfoAsync();
        await LoadAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Never leave a timer running after the page is gone.
        _tweenTimer.Stop();
        _tweens.Clear();
        _rowsWindowTimer.Stop();
        _animateRows = false;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadPlanInfoAsync();
        await LoadAsync();
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

    // Fade in while sliding up from a small offset.
    private static void PlaySlideFadeIn(FrameworkElement element, double fromY, double delayMs, double durationMs)
    {
        var v = VisualOf(element);

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);

            v.Opacity = 0f;
            element.Translation = new Vector3(0f, (float)fromY, 0f);

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

    // ----- Tweens (count-ups and the plan usage bar) -----

    private void StartTween(object key, double delayMs, Action<double> apply)
    {
        _tweens.RemoveAll(t => t.Key == key);

        if (!_animate)
        {
            apply(1.0);
            return;
        }

        apply(0.0);

        _tweens.Add(new Tween
        {
            Key = key,
            Start = DateTime.UtcNow.AddMilliseconds(delayMs),
            Ms = CountUpMs,
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

            double t = Math.Min(1.0, elapsed / tw.Ms);

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

    private static string BranchCountLabel(int n) => n == 1 ? "1 branch" : $"{n} branches";

    // ----- Rows: slide up and fade in right after a (re)load -----

    private void Row_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_animateRows || sender is not Border row || row.DataContext is not BranchRow data)
        {
            return;
        }

        int index = _rows.IndexOf(data);
        if (index < 0 || index >= MaxAnimatedRows)
        {
            return;
        }

        PlaySlideFadeIn(row, 14, index * RowStaggerMs, 380);
    }

    // ----- Form popup overlay -----

    private void WatchPanel(Border panel)
    {
        panel.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (s, dp) => UpdateFormOverlay());
    }

    private void UpdateFormOverlay()
    {
        bool formOpen = FormPanel.Visibility == Visibility.Visible;
        bool termsOpen = TermsPanel.Visibility == Visibility.Visible;
        bool anyOpen = formOpen || termsOpen;

        FormOverlay.Visibility = anyOpen ? Visibility.Visible : Visibility.Collapsed;

        // Start every popup with a clean slate.
        PopupErrorBar.IsOpen = false;

        // Animate only when the popup goes from closed to open.
        if (anyOpen && !_overlayWasOpen && _animate)
        {
            FrameworkElement panel = termsOpen ? TermsPanel : FormPanel;
            PrepareFade(FormOverlay);
            PlayFade(FormOverlay, 0, 180);
            PlaySlideFadeIn(panel, 18, 40, 260);
        }

        _overlayWasOpen = anyOpen;
    }

    // ----- Table: fill the window, keep columns aligned -----

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

    // ----- Plan card -----

    // 'animate' is true when the page opens or Refresh is pressed; false after a save or activate.
    private async Task LoadPlanInfoAsync(bool animate = true)
    {
        try
        {
            using var response = await ApiClient.Http.GetAsync("api/branches/my-plan");
            if (response.IsSuccessStatusCode)
            {
                var plan = await response.Content.ReadFromJsonAsync<MyPlan>();
                if (plan is not null)
                {
                    ShowPlan(plan, animate);
                }
            }
            else
            {
                ShowPlanUnavailable("Could not load plan info.");
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            ShowPlanUnavailable("Could not reach the API for plan info.");
        }
    }

    private void ShowPlan(MyPlan plan, bool animate)
    {
        bool unlimited = plan.BranchLimit >= int.MaxValue;
        int count = plan.CurrentBranchCount;

        PlanBadgeText.Text = (plan.SubscriptionPlan ?? string.Empty).ToUpperInvariant();
        PlanBadge.Visibility = Visibility.Visible;

        switch (plan.SubscriptionPlan)
        {
            case "Premium":
                PlanBadge.Background = ThemeBrush("PawsPrimarySoftBrush");
                PlanBadgeText.Foreground = ThemeBrush("PawsPrimaryBrush");
                break;
            case "Basic":
                PlanBadge.Background = ThemeBrush("PawsInfoSoftBrush");
                PlanBadgeText.Foreground = ThemeBrush("PawsInfoBrush");
                break;
            default:
                PlanBadge.Background = ThemeBrush("PawsHoverSurfaceBrush");
                PlanBadgeText.Foreground = ThemeBrush("PawsTextSecondaryBrush");
                break;
        }

        // ----- Price, days remaining and terms (informational only; nothing is ever blocked) -----
        _planName = plan.SubscriptionPlan ?? string.Empty;
        _termsText = plan.TermsAndConditions ?? string.Empty;

        PlanPriceText.Text = plan.Price == 0
            ? "Free plan"
            : "\u20b1" + plan.Price.ToString("N2", CultureInfo.InvariantCulture) + " per month";

        PlanRemainingText.Text = plan.RemainingDays > 0
            ? (plan.RemainingDays == 1
                ? "1 day remaining in this 30-day cycle"
                : $"{plan.RemainingDays} days remaining in this 30-day cycle")
            : "This 30-day cycle has ended";

        // ----- Terms acknowledgment status -----
        _termsAccepted = plan.TermsAccepted;
        bool isAdministrator = AuthSession.Roles.Contains("Administrator");

        if (plan.TermsAccepted && plan.TermsAcceptedAt is DateTime acceptedAt)
        {
            var local = DateTime.SpecifyKind(acceptedAt, DateTimeKind.Utc).ToLocalTime();
            PlanTermsStatusText.Text = $"Terms accepted by {plan.TermsAcceptedBy} on {local:MMM d, yyyy}";
            PlanTermsStatusText.Foreground = ThemeBrush("PawsSuccessBrush");
        }
        else
        {
            PlanTermsStatusText.Text = isAdministrator
                ? "Terms pending acceptance. Open View Terms and click I Agree to add branches."
                : "Terms pending acceptance. Ask your Administrator to accept them before adding branches.";
            PlanTermsStatusText.Foreground = ThemeBrush("PawsErrorBrush");
        }

        PlanDetailsRow.Visibility = Visibility.Visible;

        // Animate the badge only on a real (re)load.
        if (animate && _animate)
        {
            PlaySlideFadeIn(PlanBadge, 8, 200, 350);
        }

        bool saved = _animate;
        if (!animate)
        {
            _animate = false;     // update in place, with no count-up
        }

        if (unlimited)
        {
            PlanUsageBar.Visibility = Visibility.Collapsed;

            StartTween(PlanInfoText, 250, t =>
            {
                int n = (int)Math.Round(count * t);
                PlanInfoText.Text = $"{BranchCountLabel(n)} \u2014 unlimited allowed on this plan";
            });
        }
        else
        {
            PlanUsageBar.Maximum = Math.Max(1, plan.BranchLimit);
            double target = Math.Min(count, PlanUsageBar.Maximum);
            PlanUsageBar.Visibility = Visibility.Visible;

            StartTween(PlanInfoText, 250, t =>
            {
                int n = (int)Math.Round(count * t);
                PlanInfoText.Text = $"{n} of {plan.BranchLimit} branches used";
                PlanUsageBar.Value = target * t;
            });
        }

        _animate = saved;
    }

    private void ShowPlanUnavailable(string message)
    {
        PlanInfoText.Text = message;
        PlanBadge.Visibility = Visibility.Collapsed;
        PlanUsageBar.Visibility = Visibility.Collapsed;
        PlanDetailsRow.Visibility = Visibility.Collapsed;
        _termsText = string.Empty;
    }

    // ----- View Terms popup -----

    private void ViewTerms_Click(object sender, RoutedEventArgs e)
    {
        TermsTitle.Text = string.IsNullOrWhiteSpace(_planName)
            ? "Terms and Conditions"
            : $"{_planName} Plan \u2014 Terms and Conditions";

        TermsBody.Text = string.IsNullOrWhiteSpace(_termsText)
            ? "No terms and conditions have been set for this plan."
            : _termsText;

        // Only an Administrator can accept, and only while the terms are pending.
        AgreeTermsButton.Visibility =
            !_termsAccepted && AuthSession.Roles.Contains("Administrator")
                ? Visibility.Visible
                : Visibility.Collapsed;

        TermsPanel.Visibility = Visibility.Visible;
    }

    private async void AgreeTerms_Click(object sender, RoutedEventArgs e)
    {
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            using var response = await ApiClient.Http.PostAsync("api/branches/accept-terms", null);

            if (response.IsSuccessStatusCode)
            {
                var message = await ReadMessageAsync(response);
                TermsPanel.Visibility = Visibility.Collapsed;
                ShowSuccess(message);
                await LoadPlanInfoAsync(animate: false);
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthSession.Clear();
                RootNavigation.GoToLogin();
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

    private void CloseTerms_Click(object sender, RoutedEventArgs e)
    {
        TermsPanel.Visibility = Visibility.Collapsed;
    }

    // ----- Branch list -----

    // 'animate' is true when the page opens or Refresh is pressed; false after a save or activate.
    private async Task LoadAsync(bool animate = true)
    {
        Busy.IsActive = true;
        ListHost.Opacity = 0.5;
        ErrorBar.IsOpen = false;

        _animate = ReadAnimationSetting();

        try
        {
            using var response = await ApiClient.Http.GetAsync("api/branches");

            if (response.IsSuccessStatusCode)
            {
                var items = await response.Content.ReadFromJsonAsync<List<BranchItem>>() ?? new();

                // Rows animate only right after a load; give them a short window to be created.
                if (animate && _animate)
                {
                    _animateRows = true;
                    _rowsWindowTimer.Stop();
                    _rowsWindowTimer.Start();
                }

                _rows.Clear();
                foreach (var i in items)
                {
                    _rows.Add(new BranchRow(i));
                }
                _hasLoaded = true;

                int total = _rows.Count;
                bool saved = _animate;
                if (!animate)
                {
                    _animate = false;
                }

                StartTween(TotalText, 300, t => TotalText.Text = BranchCountLabel((int)Math.Round(total * t)));

                _animate = saved;
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthSession.Clear();
                RootNavigation.GoToLogin();
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
            ListHost.Opacity = 1;
            UpdateEmptyState();
        }
    }

    // ----- Add / Edit -----

    private void AddBranch_Click(object sender, RoutedEventArgs e)
    {
        _editingBranchId = null;
        FormTitle.Text = "Add Branch";
        BranchNameBox.Text = string.Empty;
        AddressBox.Text = string.Empty;
        FormPanel.Visibility = Visibility.Visible;
    }

    private void EditBranch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not BranchRow row) return;

        _editingBranchId = row.Source.BranchId;
        FormTitle.Text = $"Edit {row.Source.BranchName}";
        BranchNameBox.Text = row.Source.BranchName;
        AddressBox.Text = row.Source.Address ?? string.Empty;
        FormPanel.Visibility = Visibility.Visible;
    }

    private void CancelForm_Click(object sender, RoutedEventArgs e)
    {
        FormPanel.Visibility = Visibility.Collapsed;
        _editingBranchId = null;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = BranchNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Branch name is required.");
            return;
        }

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            HttpResponseMessage response;

            if (_editingBranchId is int id)
            {
                var updateRequest = new UpdateBranchRequest
                {
                    BranchName = name,
                    Address = NullIfBlank(AddressBox.Text)
                };
                response = await ApiClient.Http.PutAsJsonAsync($"api/branches/{id}", updateRequest);
            }
            else
            {
                var createRequest = new CreateBranchRequest
                {
                    BranchName = name,
                    Address = NullIfBlank(AddressBox.Text)
                };
                response = await ApiClient.Http.PostAsJsonAsync("api/branches", createRequest);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    ShowSuccess(await ReadMessageAsync(response));
                    FormPanel.Visibility = Visibility.Collapsed;
                    _editingBranchId = null;
                    await LoadPlanInfoAsync(animate: false);
                    await LoadAsync(animate: false);
                }
                else
                {
                    ShowError(await ReadMessageAsync(response));
                }
            }
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    // ----- Activate / Deactivate -----

    private async void ActivateBranch_Click(object sender, RoutedEventArgs e) => await SetActive(sender, true);
    private async void DeactivateBranch_Click(object sender, RoutedEventArgs e) => await SetActive(sender, false);

    private async Task SetActive(object sender, bool activate)
    {
        if ((sender as Button)?.Tag is not BranchRow row) return;

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            var action = activate ? "activate" : "deactivate";
            using var response = await ApiClient.Http.PostAsync($"api/branches/{row.Source.BranchId}/{action}", null);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                await LoadPlanInfoAsync(animate: false);
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

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

        // If the form popup is open, the page-level bar is hidden behind it, so show the error there too.
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