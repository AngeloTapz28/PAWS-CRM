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

public sealed partial class PaymentsPage : Page
{
    private const string AllStatuses = "All Statuses";

    // The Record Payment form only records adoption fees.
    private const string FeeType = "Adoption Fee";

    // ----- Animation tuning -----
    private const int MaxAnimatedRows = 12;      // only the first rows animate; the rest are off-screen
    private const double RowStaggerMs = 45;
    private const double CountUpMs = 700;

    private static string Peso(decimal amount) =>
        "\u20b1" + amount.ToString("N2", CultureInfo.InvariantCulture);

    public class PaymentRow : INotifyPropertyChanged
    {
        public PaymentItem Source { get; }
        public PaymentRow(PaymentItem source) => Source = source;

        private static bool IsAdminOrStaff =>
            AuthSession.Roles.Contains("Administrator") || AuthSession.Roles.Contains("Staff");

        // Looks up a PAWS brush from PawsTheme.xaml
        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        // ----- Table cells -----
        public string ReferenceText => Source.ReferenceNumber;
        public string ApplicationText => Source.ApplicationNumber;
        public string AmountText => Peso(Source.Amount);
        public string MethodText => Source.Method;
        public string CreatedText => Source.CreatedAt.ToString("MMM d, yyyy");

        public string PaidText => $"Paid {Source.PaidAt:MMM d, yyyy}";
        public Visibility PaidVisibility =>
            Source.PaidAt.HasValue ? Visibility.Visible : Visibility.Collapsed;

        public string NotesText => $"Notes: {Source.Notes}";
        public Visibility NotesVisibility =>
            !string.IsNullOrWhiteSpace(Source.Notes) ? Visibility.Visible : Visibility.Collapsed;

        // ----- Type badge -----
        public string TypeLabel => (Source.Type ?? string.Empty).ToUpperInvariant();

        public Brush TypeBadgeBackground => Source.Type switch
        {
            "Adoption Fee" => Res("PawsInfoSoftBrush"),
            "Donation" => Res("PawsPrimarySoftBrush"),
            "Medical Fee" => Res("PawsWarningSoftBrush"),
            _ => Res("PawsHoverSurfaceBrush")
        };

        public Brush TypeBadgeForeground => Source.Type switch
        {
            "Adoption Fee" => Res("PawsInfoBrush"),
            "Donation" => Res("PawsPrimaryBrush"),
            "Medical Fee" => Res("PawsWarningBrush"),
            _ => Res("PawsTextSecondaryBrush")
        };

        // ----- Status badge -----
        public string StatusLabel => (Source.Status ?? string.Empty).ToUpperInvariant();

        public Brush StatusBadgeBackground => Source.Status switch
        {
            "Completed" => Res("PawsSuccessSoftBrush"),
            "Failed" => Res("PawsErrorSoftBrush"),
            "Refunded" => Res("PawsHoverSurfaceBrush"),
            _ => Res("PawsWarningSoftBrush")      // Pending and anything else
        };

        public Brush StatusBadgeForeground => Source.Status switch
        {
            "Completed" => Res("PawsSuccessBrush"),
            "Failed" => Res("PawsErrorBrush"),
            "Refunded" => Res("PawsTextSecondaryBrush"),
            _ => Res("PawsWarningBrush")
        };

        // ----- Button visibility: SAME rules as the old page -----
        public Visibility CompleteVisibility =>
            IsAdminOrStaff && Source.Status == "Pending" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FailVisibility =>
            IsAdminOrStaff && Source.Status == "Pending" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RefundVisibility =>
            IsAdminOrStaff && Source.Status == "Completed" ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<PaymentRow> _rows = new();
    private bool _hasLoaded;
    private bool _suppressFilter;

    // True while code (not the person) is changing the form's dropdowns.
    private bool _suppressFormEvents;

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

    private static bool IsAdminOrStaff =>
        AuthSession.Roles.Contains("Administrator") || AuthSession.Roles.Contains("Staff");

    public PaymentsPage()
    {
        InitializeComponent();
        PaymentsList.ItemsSource = _rows;

        // The popup background must follow the form panel.
        WatchPanel(FormPanel);

        _tweenTimer.Tick += TweenTimer_Tick;

        // After the rows have been created, stop animating them (so later updates are instant).
        _rowsWindowTimer.Tick += (s, e) =>
        {
            _rowsWindowTimer.Stop();
            _animateRows = false;
        };

        PageTitle.Text = IsAdminOrStaff ? "Payments" : "My Payments";
        TableTitle.Text = IsAdminOrStaff ? "All Payments" : "My Payments";
        AddPaymentButton.Visibility = IsAdminOrStaff ? Visibility.Visible : Visibility.Collapsed;

        StatusFilterBox.ItemsSource = new List<string> { AllStatuses, "Pending", "Completed", "Failed", "Refunded" };
        StatusFilterBox.SelectedIndex = 0;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _animate = ReadAnimationSetting();

        if (_animate)
        {
            // The header and the filter card fade in one after another, and the table card rises into place.
            PrepareFade(HeaderPanel);
            PrepareFade(ToolbarCard);
            PlayFade(HeaderPanel, 0, 420);
            PlayFade(ToolbarCard, 110, 420);
            PlaySlideFadeIn(TableCard, 16, 220, 450);
        }

        await LoadPaymentsAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Never leave a timer running after the page is gone.
        _tweenTimer.Stop();
        _tweens.Clear();
        _rowsWindowTimer.Stop();
        _animateRows = false;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadPaymentsAsync();

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilter)
        {
            return;
        }

        await LoadPaymentsAsync();
    }

    private async void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        _suppressFilter = true;
        try
        {
            StatusFilterBox.SelectedIndex = 0;
        }
        finally
        {
            _suppressFilter = false;
        }

        await LoadPaymentsAsync();
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

    // ----- Tweens (the count-ups in the summary line) -----

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

    private static string PaymentCountLabel(int n) => n == 1 ? "1 payment" : $"{n} payments";

    // ----- Rows: slide up and fade in right after a (re)load -----

    private void Row_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_animateRows || sender is not Border row || row.DataContext is not PaymentRow data)
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
        bool anyOpen = FormPanel.Visibility == Visibility.Visible;

        FormOverlay.Visibility = anyOpen ? Visibility.Visible : Visibility.Collapsed;

        // Start every popup with a clean slate.
        PopupErrorBar.IsOpen = false;

        // Animate only when the popup goes from closed to open.
        if (anyOpen && !_overlayWasOpen && _animate)
        {
            PrepareFade(FormOverlay);
            PlayFade(FormOverlay, 0, 180);
            PlaySlideFadeIn(FormPanel, 18, 40, 260);
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

    // ----- Empty state -----

    private bool FilterActive => StatusFilterBox.SelectedItem is string s && s != AllStatuses;

    private void UpdateEmptyState()
    {
        bool isEmpty = _hasLoaded && _rows.Count == 0 && !Busy.IsActive;

        if (isEmpty)
        {
            if (FilterActive)
            {
                EmptyTitleText.Text = "No payments match your filter";
                EmptyHintText.Text = "Try a different status, or press Reset.";
            }
            else
            {
                EmptyTitleText.Text = "No payments yet";
                EmptyHintText.Text = IsAdminOrStaff
                    ? "Use Record Payment to add one."
                    : "Your payments will appear here.";
            }
        }

        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        TableCard.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    // ----- Load -----

    // 'animate' is true when the page opens, Refresh is pressed or the filter changes;
    // false after a save or a status change, so the table updates in place.
    private async Task LoadPaymentsAsync(bool animate = true)
    {
        Busy.IsActive = true;
        ListHost.Opacity = 0.5;
        ErrorBar.IsOpen = false;

        _animate = ReadAnimationSetting();

        try
        {
            var url = "api/payments";
            if (StatusFilterBox.SelectedItem is string status && status != AllStatuses)
            {
                url += $"?status={Uri.EscapeDataString(status)}";
            }

            using var response = await ApiClient.Http.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var payments = await response.Content.ReadFromJsonAsync<List<PaymentItem>>() ?? new();

                // Rows animate only right after a load; give them a short window to be created.
                if (animate && _animate)
                {
                    _animateRows = true;
                    _rowsWindowTimer.Stop();
                    _rowsWindowTimer.Start();
                }

                _rows.Clear();
                foreach (var p in payments) _rows.Add(new PaymentRow(p));
                _hasLoaded = true;

                int count = _rows.Count;
                decimal completedTotal = payments.Where(p => p.Status == "Completed").Sum(p => p.Amount);

                // The completed total is only meaningful when no status filter is applied.
                bool showTotal = !FilterActive;

                // "All Payments" card subtitle: "40 payments".
                StartTween(TotalText, 300, animate, t =>
                    TotalText.Text = PaymentCountLabel((int)Math.Round(count * t)));

                // Header summary: "40 payments • ₱23,898.00 completed".
                StartTween(SummaryText, 200, animate, t =>
                {
                    string countText = PaymentCountLabel((int)Math.Round(count * t));
                    SummaryText.Text = showTotal
                        ? $"{countText} \u2022 {Peso(completedTotal * (decimal)t)} completed"
                        : countText;
                });
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

    // ----- Add payment -----

    private async void AddPayment_Click(object sender, RoutedEventArgs e)
    {
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;

        try
        {
            using var response = await ApiClient.Http.GetAsync("api/payments/payable-applications");
            if (response.IsSuccessStatusCode)
            {
                var applications = await response.Content.ReadFromJsonAsync<List<PayableApplicationFeeItem>>() ?? new();
                ApplicationBox.ItemsSource = applications;

                if (applications.Count == 0)
                {
                    ShowError("No applications are currently eligible for a new payment.");
                    return;
                }
            }
            else
            {
                ShowError(await ReadMessageAsync(response));
                return;
            }
        }
        finally
        {
            Busy.IsActive = false;
        }

        _suppressFormEvents = true;
        try
        {
            ApplicationBox.SelectedIndex = -1;
            MethodBox.SelectedIndex = -1;
        }
        finally
        {
            _suppressFormEvents = false;
        }

        // Type and Amount are filled in by the application chosen; the person only
        // picks the application, the method and (optionally) notes.
        TypeBox.Text = FeeType;
        AmountBox.Text = string.Empty;
        AmountHintText.Text = "Choose an application. The adoption fee fills in automatically.";
        NotesBox.Text = string.Empty;
        FormPanel.Visibility = Visibility.Visible;
    }

    // Choosing an application fills in the amount from the pet's adoption fee.
    private void ApplicationBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFormEvents)
        {
            return;
        }

        FillFromApplication();
    }

    private void FillFromApplication()
    {
        TypeBox.Text = FeeType;

        if (ApplicationBox.SelectedItem is not PayableApplicationFeeItem application)
        {
            AmountBox.Text = string.Empty;
            AmountHintText.Text = "Choose an application. The adoption fee fills in automatically.";
            return;
        }

        if (application.AdoptionFee is decimal fee && fee > 0)
        {
            AmountBox.Text = fee.ToString("0.00", CultureInfo.InvariantCulture);
            AmountHintText.Text = $"Locked to {application.PetName}'s adoption fee ({Peso(fee)}). It cannot be edited.";
        }
        else
        {
            AmountBox.Text = string.Empty;
            AmountHintText.Text = $"{application.PetName} has no adoption fee set. Set the fee on the Pets page first.";
        }
    }

    private void CancelForm_Click(object sender, RoutedEventArgs e)
    {
        FormPanel.Visibility = Visibility.Collapsed;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var application = ApplicationBox.SelectedItem as PayableApplicationFeeItem;
        var method = (MethodBox.SelectedItem as ComboBoxItem)?.Content as string;

        if (application is null || method is null)
        {
            ShowError("Please choose an application and a method.");
            return;
        }

        // The amount is never read from the text box: it is always the pet's fee.
        if (application.AdoptionFee is not decimal fee || fee <= 0)
        {
            ShowError($"{application.PetName} has no adoption fee set. Set the fee on the Pets page first.");
            return;
        }

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            var request = new CreatePaymentRequest
            {
                AdoptionApplicationId = application.AdoptionApplicationId,
                Type = FeeType,
                Amount = fee,
                Method = method,
                Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim()
            };

            using var response = await ApiClient.Http.PostAsJsonAsync("api/payments", request);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                FormPanel.Visibility = Visibility.Collapsed;
                await LoadPaymentsAsync(animate: false);
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

    // ----- Status changes -----

    private async void MarkCompleted_Click(object sender, RoutedEventArgs e) =>
        await SetStatus(sender, "Completed");

    private async void MarkFailed_Click(object sender, RoutedEventArgs e) =>
        await SetStatus(sender, "Failed");

    private async void Refund_Click(object sender, RoutedEventArgs e) =>
        await SetStatus(sender, "Refunded");

    private async Task SetStatus(object sender, string status)
    {
        if ((sender as Button)?.Tag is not PaymentRow row) return;

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            var request = new SetPaymentStatusRequest { Status = status };
            using var response = await ApiClient.Http.PostAsJsonAsync($"api/payments/{row.Source.PaymentId}/status", request);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                await LoadPaymentsAsync(animate: false);
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