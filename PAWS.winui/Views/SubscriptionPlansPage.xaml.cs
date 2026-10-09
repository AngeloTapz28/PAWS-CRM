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

public sealed partial class SubscriptionPlansPage : Page
{
    // ----- Animation tuning -----
    private const int MaxAnimatedRows = 12;
    private const double RowStaggerMs = 60;
    private const double CountUpMs = 700;

    public class PlanRow : INotifyPropertyChanged
    {
        public SubscriptionPlanItem Source { get; }
        public PlanRow(SubscriptionPlanItem source) => Source = source;

        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        public string NameText => Source.Name;

        public string PriceText =>
            Source.Price == 0 ? "Free" : "\u20b1" + Source.Price.ToString("N2", CultureInfo.InvariantCulture);

        public string LimitText =>
            Source.BranchLimit >= int.MaxValue ? "Unlimited" : Source.BranchLimit.ToString();

        public string CompaniesText => Source.CompanyCount.ToString();

        public string StatusLabel => Source.IsActive ? "ACTIVE" : "INACTIVE";
        public Brush StatusBadgeBackground => Source.IsActive ? Res("PawsSuccessSoftBrush") : Res("PawsErrorSoftBrush");
        public Brush StatusBadgeForeground => Source.IsActive ? Res("PawsSuccessBrush") : Res("PawsErrorBrush");

        public Visibility DeactivateVisibility => Source.IsActive ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ActivateVisibility => !Source.IsActive ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<PlanRow> _rows = new();
    private int? _editingPlanId;

    // ----- Animation state -----
    private bool _animate = true;
    private bool _animateRows;                   // true only right after the list is (re)loaded

    private readonly DispatcherTimer _rowsWindowTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    private readonly DispatcherTimer _countTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private DateTime _countStart;
    private int _countTarget;

    public SubscriptionPlansPage()
    {
        InitializeComponent();
        PlansList.ItemsSource = _rows;

        _countTimer.Tick += CountTimer_Tick;

        // After the rows have been created, stop animating them (so later updates are instant).
        _rowsWindowTimer.Tick += (s, e) =>
        {
            _rowsWindowTimer.Stop();
            _animateRows = false;
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _animate = ReadAnimationSetting();

        if (_animate)
        {
            // The header fades in, then the table card rises into place (they do not depend on data).
            PrepareFade(HeaderPanel);
            PlayFade(HeaderPanel, 0, 420);
            PlaySlideFadeIn(TableCard, 16, 200, 450);
        }

        await LoadAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Never leave a timer running after the page is gone.
        _countTimer.Stop();
        _rowsWindowTimer.Stop();
        _animateRows = false;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

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

    // ----- Rows: slide up and fade in right after a (re)load -----

    private void Row_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_animateRows || sender is not Border row || row.DataContext is not PlanRow data)
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

    // ----- Count-up for "N plans" -----

    private static string CountLabel(int n) => n == 1 ? "1 plan" : $"{n} plans";

    private void StartCountUp(int target)
    {
        _countTarget = target;
        _countStart = DateTime.UtcNow;
        TotalText.Text = CountLabel(0);
        _countTimer.Start();
    }

    private void CountTimer_Tick(object? sender, object e)
    {
        double t = Math.Min(1.0, (DateTime.UtcNow - _countStart).TotalMilliseconds / CountUpMs);

        // Ease-out: fast at first, slowing down at the end.
        double eased = 1.0 - Math.Pow(1.0 - t, 3);

        // The last tick always writes the exact value.
        int shown = t >= 1.0 ? _countTarget : (int)Math.Round(_countTarget * eased);
        TotalText.Text = CountLabel(shown);

        if (t >= 1.0)
        {
            _countTimer.Stop();
        }
    }

    // ----- Table: fill the window, keep columns aligned -----

    private void TableScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        TableGrid.Width = Math.Max(TableGrid.MinWidth, e.NewSize.Width);
    }

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

    // ----- Load -----

    // 'animate' is true when the page opens or Refresh is pressed;
    // false after a save or an activate/deactivate, so the table updates in place.
    private async Task LoadAsync(bool animate = true)
    {
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;

        _animate = ReadAnimationSetting();

        try
        {
            using var response = await ApiClient.Http.GetAsync("api/subscription-plans");

            if (response.IsSuccessStatusCode)
            {
                var items = await response.Content.ReadFromJsonAsync<List<SubscriptionPlanItem>>() ?? new();

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
                    _rows.Add(new PlanRow(i));
                }

                if (animate && _animate && _rows.Count > 0)
                {
                    StartCountUp(_rows.Count);
                }
                else
                {
                    _countTimer.Stop();
                    TotalText.Text = CountLabel(_rows.Count);
                }
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

    // ----- Add / Edit form -----

    private void OpenForm()
    {
        PopupErrorBar.IsOpen = false;
        FormOverlay.Visibility = Visibility.Visible;

        // The dimmed background fades in and the form card rises into place.
        if (_animate)
        {
            PlayFade(FormOverlay, 0, 180);
            PlaySlideFadeIn(FormPanel, 18, 40, 260);
        }
    }

    private void CloseForm()
    {
        FormOverlay.Visibility = Visibility.Collapsed;
        _editingPlanId = null;
    }

    private void AddPlan_Click(object sender, RoutedEventArgs e)
    {
        _editingPlanId = null;
        FormTitle.Text = "Add Plan";
        NameBox.Text = string.Empty;
        PriceBox.Text = string.Empty;
        LimitBox.Text = string.Empty;
        UnlimitedBox.IsChecked = false;
        LimitBox.IsEnabled = true;
        TermsBox.Text = string.Empty;
        OpenForm();
    }

    private void EditPlan_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PlanRow row) return;

        var plan = row.Source;
        _editingPlanId = plan.SubscriptionPlanId;
        FormTitle.Text = $"Edit {plan.Name}";
        NameBox.Text = plan.Name;
        PriceBox.Text = plan.Price.ToString("0.##", CultureInfo.InvariantCulture);

        bool unlimited = plan.BranchLimit >= int.MaxValue;
        UnlimitedBox.IsChecked = unlimited;
        LimitBox.IsEnabled = !unlimited;
        LimitBox.Text = unlimited ? string.Empty : plan.BranchLimit.ToString();

        TermsBox.Text = plan.TermsAndConditions;
        OpenForm();
    }

    private void CancelForm_Click(object sender, RoutedEventArgs e) => CloseForm();

    private void Unlimited_Changed(object sender, RoutedEventArgs e)
    {
        bool unlimited = UnlimitedBox.IsChecked == true;
        LimitBox.IsEnabled = !unlimited;
        if (unlimited)
        {
            LimitBox.Text = string.Empty;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Plan name is required.");
            return;
        }

        if (!decimal.TryParse(PriceBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0)
        {
            ShowError("Enter a valid price (0 or more), for example 499.");
            return;
        }

        int branchLimit;
        if (UnlimitedBox.IsChecked == true)
        {
            branchLimit = int.MaxValue;
        }
        else if (!int.TryParse(LimitBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out branchLimit) || branchLimit < 1)
        {
            ShowError("Enter a branch limit of 1 or more, or tick Unlimited branches.");
            return;
        }

        var terms = TermsBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(terms))
        {
            ShowError("Terms and conditions are required.");
            return;
        }

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            HttpResponseMessage response;
            string successText;

            if (_editingPlanId is int id)
            {
                var request = new UpdateSubscriptionPlanRequest
                {
                    Name = name,
                    Price = price,
                    BranchLimit = branchLimit,
                    TermsAndConditions = terms
                };
                response = await ApiClient.Http.PutAsJsonAsync($"api/subscription-plans/{id}", request);
                successText = $"{name} plan updated.";
            }
            else
            {
                var request = new CreateSubscriptionPlanRequest
                {
                    Name = name,
                    Price = price,
                    BranchLimit = branchLimit,
                    TermsAndConditions = terms
                };
                response = await ApiClient.Http.PostAsJsonAsync("api/subscription-plans", request);
                successText = $"{name} plan created.";
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    ShowSuccess(successText);
                    CloseForm();
                    await LoadAsync(animate: false);
                }
                else
                {
                    ShowError(await ReadMessageAsync(response));
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
        }
    }

    // ----- Activate / Deactivate -----

    private async void ActivatePlan_Click(object sender, RoutedEventArgs e) => await SetActive(sender, true);
    private async void DeactivatePlan_Click(object sender, RoutedEventArgs e) => await SetActive(sender, false);

    private async Task SetActive(object sender, bool activate)
    {
        if ((sender as Button)?.Tag is not PlanRow row) return;

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            var action = activate ? "activate" : "deactivate";
            using var response = await ApiClient.Http.PostAsync($"api/subscription-plans/{row.Source.SubscriptionPlanId}/{action}", null);

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
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            ShowError("Could not reach the API.");
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

        // If the popup is open, the page-level bar is hidden behind it, so show the error there too.
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