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

public sealed partial class CustomerRetentionPage : Page
{
    private const string AllStatuses = "All Statuses";

    // ----- Animation tuning -----
    private const int MaxAnimatedRows = 12;      // only the first rows animate; the rest are off-screen
    private const double RowStaggerMs = 45;
    private const double CountUpMs = 700;

    // Looks up a PAWS brush from PawsTheme.xaml
    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public class CustomerRow : INotifyPropertyChanged
    {
        public CustomerRetentionItem Source { get; }
        public CustomerRow(CustomerRetentionItem source) => Source = source;

        public string NameText => Source.CustomerName;
        public string EmailText => Source.CustomerEmail;

        public string Initials
        {
            get
            {
                var parts = (Source.CustomerName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "?";
                if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpperInvariant();
                return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
            }
        }

        // ----- Status badge -----
        public string StatusLabel => (Source.RetentionStatus ?? string.Empty).ToUpperInvariant();

        public Brush StatusBadgeBackground => Source.RetentionStatus switch
        {
            "Overdue" => Res("PawsErrorSoftBrush"),
            "Due Soon" => Res("PawsWarningSoftBrush"),
            "On Track" => Res("PawsSuccessSoftBrush"),
            _ => Res("PawsHoverSurfaceBrush")
        };

        public Brush StatusBadgeForeground => Source.RetentionStatus switch
        {
            "Overdue" => Res("PawsErrorBrush"),
            "Due Soon" => Res("PawsWarningBrush"),
            "On Track" => Res("PawsSuccessBrush"),
            _ => Res("PawsTextSecondaryBrush")
        };

        public string FollowUpText => Source.NextFollowUpDate.HasValue
            ? Source.NextFollowUpDate.Value.ToString("MMM d, yyyy")
            : "\u2014";

        public string NotesDisplay => !string.IsNullOrWhiteSpace(Source.Notes) ? Source.Notes! : "\u2014";

        public string LastContactText => Source.LastContactedAt.HasValue
            ? $"{Source.LastContactedAt:MMM d, yyyy} by {Source.LastContactedByName}"
            : "\u2014";

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class PromoRow : INotifyPropertyChanged
    {
        public PromoItem Source { get; }
        public PromoRow(PromoItem source) => Source = source;

        public string TitleText => Source.Title;

        public string DetailText
        {
            get
            {
                var valueText = Source.DiscountType switch
                {
                    "Percentage" => $"{Source.DiscountValue}% off",
                    "FixedAmount" => $"\u20b1{Source.DiscountValue:N2} off",
                    _ => "Non-monetary perk"
                };
                var expiryText = Source.ExpiryDate.HasValue ? $" \u2022 Expires: {Source.ExpiryDate:MMM d, yyyy}" : "";
                var descText = string.IsNullOrWhiteSpace(Source.Description) ? "" : $" \u2022 {Source.Description}";
                return $"{valueText}{expiryText}{descText}";
            }
        }

        // ----- Status badge -----
        public string StatusLabel => (Source.PromoStatus ?? string.Empty).ToUpperInvariant();

        public Brush StatusBadgeBackground => Source.PromoStatus switch
        {
            "Active" => Res("PawsSuccessSoftBrush"),
            "Expired" => Res("PawsErrorSoftBrush"),
            _ => Res("PawsHoverSurfaceBrush")   // Redeemed and anything else
        };

        public Brush StatusBadgeForeground => Source.PromoStatus switch
        {
            "Active" => Res("PawsSuccessBrush"),
            "Expired" => Res("PawsErrorBrush"),
            _ => Res("PawsTextSecondaryBrush")
        };

        public string RedeemedText => $"Redeemed by {Source.RedeemedByName} on {Source.RedeemedAt:MMM d, yyyy}";
        public Visibility RedeemedVisibility =>
            Source.RedeemedAt.HasValue ? Visibility.Visible : Visibility.Collapsed;

        public Visibility RedeemVisibility =>
            Source.PromoStatus == "Active" ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<CustomerRow> _rows = new();
    private readonly ObservableCollection<PromoRow> _promoRows = new();
    private List<CustomerRetentionItem> _allCustomers = new();
    private CustomerRow? _editingRow;
    private CustomerRow? _promosCustomer;
    private bool _hasLoaded;
    private bool _dateCleared = false;

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

    public CustomerRetentionPage()
    {
        InitializeComponent();
        CustomersList.ItemsSource = _rows;
        PromosList.ItemsSource = _promoRows;

        // The popup background must follow whichever panel is showing.
        WatchPanel(EditFormPanel);
        WatchPanel(PromosPanel);

        _tweenTimer.Tick += TweenTimer_Tick;

        // After the rows have been created, stop animating them (so later updates are instant).
        _rowsWindowTimer.Tick += (s, e) =>
        {
            _rowsWindowTimer.Stop();
            _animateRows = false;
        };

        StatusFilterBox.SelectedIndex = 0;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _animate = ReadAnimationSetting();

        if (_animate)
        {
            // The header and the toolbar fade in one after another, and the table card rises into place.
            PrepareFade(HeaderPanel);
            PrepareFade(ToolbarCard);
            PlayFade(HeaderPanel, 0, 420);
            PlayFade(ToolbarCard, 110, 420);
            PlaySlideFadeIn(TableCard, 16, 220, 450);
        }

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

    private static string CustomerCountLabel(int n) => n == 1 ? "1 customer" : $"{n} customers";

    // ----- Rows: slide up and fade in right after a (re)load -----

    private void Row_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_animateRows || sender is not Border row || row.DataContext is not CustomerRow data)
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
        bool anyOpen =
            EditFormPanel.Visibility == Visibility.Visible ||
            PromosPanel.Visibility == Visibility.Visible;

        FormOverlay.Visibility = anyOpen ? Visibility.Visible : Visibility.Collapsed;

        // Start every popup with a clean slate.
        PopupErrorBar.IsOpen = false;
        PopupSuccessBar.IsOpen = false;

        // Animate only when the popup goes from closed to open.
        if (anyOpen && !_overlayWasOpen && _animate)
        {
            PrepareFade(FormOverlay);
            PlayFade(FormOverlay, 0, 180);

            Border openPanel = EditFormPanel.Visibility == Visibility.Visible
                ? EditFormPanel
                : PromosPanel;

            PlaySlideFadeIn(openPanel, 18, 40, 260);
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
            row.Background = Res("PawsBackgroundBrush");
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
    // false after a save, so the table updates in place.
    private async Task LoadAsync(bool animate = true)
    {
        Busy.IsActive = true;
        ListHost.Opacity = 0.5;
        ErrorBar.IsOpen = false;

        _animate = ReadAnimationSetting();

        try
        {
            using var response = await ApiClient.Http.GetAsync("api/customer-retention");

            if (response.IsSuccessStatusCode)
            {
                _allCustomers = await response.Content.ReadFromJsonAsync<List<CustomerRetentionItem>>() ?? new();
                _hasLoaded = true;

                // Rows animate only right after a load; give them a short window to be created.
                if (animate && _animate)
                {
                    _animateRows = true;
                    _rowsWindowTimer.Stop();
                    _rowsWindowTimer.Start();
                }

                ApplyFilters(afterLoad: animate);
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
        }
    }

    private void Filter_Changed(object sender, object e) => ApplyFilters();

    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        StatusFilterBox.SelectedIndex = 0;
        ApplyFilters();
    }

    // 'afterLoad' is true only when the list has just been loaded from the API and the
    // caller asked for animation. Searching and filtering are always instant.
    private void ApplyFilters(bool afterLoad = false)
    {
        var searchText = SearchBox.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var statusFilter = (StatusFilterBox.SelectedItem as ComboBoxItem)?.Content as string ?? AllStatuses;

        var filtered = _allCustomers.Where(c =>
            (statusFilter == AllStatuses || c.RetentionStatus == statusFilter) &&
            (string.IsNullOrEmpty(searchText) ||
             c.CustomerName.ToLowerInvariant().Contains(searchText) ||
             c.CustomerEmail.ToLowerInvariant().Contains(searchText))
        ).ToList();

        _rows.Clear();
        foreach (var c in filtered)
        {
            _rows.Add(new CustomerRow(c));
        }

        // The stat cards always count every customer, whatever the filters say.
        int overdue = _allCustomers.Count(c => c.RetentionStatus == "Overdue");
        int dueSoon = _allCustomers.Count(c => c.RetentionStatus == "Due Soon");
        int onTrack = _allCustomers.Count(c => c.RetentionStatus == "On Track");
        int notScheduled = _allCustomers.Count(c => c.RetentionStatus == "Not Scheduled");

        StartTween(OverdueCountText, 150, afterLoad, t => OverdueCountText.Text = ((int)Math.Round(overdue * t)).ToString());
        StartTween(DueSoonCountText, 250, afterLoad, t => DueSoonCountText.Text = ((int)Math.Round(dueSoon * t)).ToString());
        StartTween(OnTrackCountText, 350, afterLoad, t => OnTrackCountText.Text = ((int)Math.Round(onTrack * t)).ToString());
        StartTween(NotScheduledCountText, 450, afterLoad, t => NotScheduledCountText.Text = ((int)Math.Round(notScheduled * t)).ToString());

        int total = _allCustomers.Count;

        // Header: "62 customers".
        StartTween(SummaryText, 200, afterLoad, t => SummaryText.Text = CustomerCountLabel((int)Math.Round(total * t)));

        // Table card subtitle: "62 customers" or "Showing 10 of 62".
        if (filtered.Count == total)
        {
            StartTween(TotalText, 300, afterLoad, t => TotalText.Text = CustomerCountLabel((int)Math.Round(total * t)));
        }
        else
        {
            _tweens.RemoveAll(t => t.Key == TotalText);
            TotalText.Text = $"Showing {filtered.Count} of {total}";
        }

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        bool isEmpty = _hasLoaded && _rows.Count == 0;

        if (isEmpty)
        {
            if (_allCustomers.Count == 0)
            {
                EmptyTitleText.Text = "No customers yet";
                EmptyHintText.Text = "Customers will appear here once adopters register.";
            }
            else
            {
                EmptyTitleText.Text = "No customers match your filters";
                EmptyHintText.Text = "Try a different search or status, or press Reset.";
            }
        }

        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        TableCard.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    // ----- Retention notes edit form -----

    private void ResetDateState()
    {
        _dateCleared = false;
        FollowUpDatePicker.IsEnabled = true;
        DateClearedHint.Visibility = Visibility.Collapsed;
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not CustomerRow row) return;

        PromosPanel.Visibility = Visibility.Collapsed;
        _editingRow = row;
        ResetDateState();

        EditFormTitle.Text = $"Update Retention Notes \u2014 {row.Source.CustomerName}";
        NotesBox.Text = row.Source.Notes ?? string.Empty;
        FollowUpDatePicker.Date = row.Source.NextFollowUpDate.HasValue
            ? new DateTimeOffset(row.Source.NextFollowUpDate.Value)
            : DateTimeOffset.Now.AddDays(7);

        EditFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelForm_Click(object sender, RoutedEventArgs e)
    {
        EditFormPanel.Visibility = Visibility.Collapsed;
        _editingRow = null;
        ResetDateState();
    }

    private void ClearDate_Click(object sender, RoutedEventArgs e)
    {
        _dateCleared = true;
        FollowUpDatePicker.IsEnabled = false;
        DateClearedHint.Visibility = Visibility.Visible;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_editingRow is null) return;

        var request = new SaveCustomerRetentionRequest
        {
            Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
            NextFollowUpDate = _dateCleared ? null : FollowUpDatePicker.Date.DateTime
        };

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            using var response = await ApiClient.Http.PutAsJsonAsync(
                $"api/customer-retention/{_editingRow.Source.UserId}", request);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                EditFormPanel.Visibility = Visibility.Collapsed;
                _editingRow = null;
                ResetDateState();
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

    // ----- Promos panel -----

    private async void ShowPromos_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not CustomerRow row) return;

        EditFormPanel.Visibility = Visibility.Collapsed;
        _promosCustomer = row;
        PromosPanelTitle.Text = $"Promos \u2014 {row.Source.CustomerName}";

        PromoTitleBox.Text = string.Empty;
        PromoDescriptionBox.Text = string.Empty;
        PromoDiscountTypeBox.SelectedIndex = -1;
        PromoDiscountValueBox.Text = string.Empty;
        PromoDiscountValueBox.IsEnabled = true;
        PromoNoExpiryBox.IsChecked = true;
        PromoExpiryDatePicker.IsEnabled = false;

        _promoRows.Clear();
        NoPromosText.Visibility = Visibility.Collapsed;

        PromosPanel.Visibility = Visibility.Visible;

        await LoadPromosAsync();
    }

    private void ClosePromosPanel_Click(object sender, RoutedEventArgs e)
    {
        PromosPanel.Visibility = Visibility.Collapsed;
        _promosCustomer = null;
    }

    private async Task LoadPromosAsync()
    {
        if (_promosCustomer is null) return;

        Busy.IsActive = true;
        try
        {
            using var response = await ApiClient.Http.GetAsync($"api/promos?userId={_promosCustomer.Source.UserId}");

            if (response.IsSuccessStatusCode)
            {
                var items = await response.Content.ReadFromJsonAsync<List<PromoItem>>() ?? new();
                _promoRows.Clear();
                foreach (var i in items)
                {
                    _promoRows.Add(new PromoRow(i));
                }
                NoPromosText.Visibility = _promoRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private void PromoDiscountType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = (PromoDiscountTypeBox.SelectedItem as ComboBoxItem)?.Content as string;
        PromoDiscountValueBox.IsEnabled = selected != "Other (non-monetary)";
        if (!PromoDiscountValueBox.IsEnabled)
        {
            PromoDiscountValueBox.Text = string.Empty;
        }
    }

    private void PromoNoExpiry_Changed(object sender, RoutedEventArgs e)
    {
        PromoExpiryDatePicker.IsEnabled = PromoNoExpiryBox.IsChecked != true;
    }

    private async void GivePromo_Click(object sender, RoutedEventArgs e)
    {
        if (_promosCustomer is null) return;

        var title = PromoTitleBox.Text.Trim();
        var selectedLabel = (PromoDiscountTypeBox.SelectedItem as ComboBoxItem)?.Content as string;

        if (string.IsNullOrWhiteSpace(title) || selectedLabel is null)
        {
            ShowError("Please enter a title and choose a discount type.");
            return;
        }

        var discountType = selectedLabel switch
        {
            "Percentage" => "Percentage",
            "Fixed Amount (PHP)" => "FixedAmount",
            _ => "Other"
        };

        decimal? discountValue = null;
        if (discountType != "Other")
        {
            if (!decimal.TryParse(PromoDiscountValueBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedValue))
            {
                ShowError("Please enter a valid discount value.");
                return;
            }
            discountValue = parsedValue;
        }

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            var request = new CreatePromoRequest
            {
                Title = title,
                Description = string.IsNullOrWhiteSpace(PromoDescriptionBox.Text) ? null : PromoDescriptionBox.Text.Trim(),
                DiscountType = discountType,
                DiscountValue = discountValue,
                ExpiryDate = PromoNoExpiryBox.IsChecked == true ? null : PromoExpiryDatePicker.Date.DateTime
            };

            using var response = await ApiClient.Http.PostAsJsonAsync(
                $"api/promos?userId={_promosCustomer.Source.UserId}", request);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                PromoTitleBox.Text = string.Empty;
                PromoDescriptionBox.Text = string.Empty;
                PromoDiscountTypeBox.SelectedIndex = -1;
                PromoDiscountValueBox.Text = string.Empty;
                await LoadPromosAsync();
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

    private async void RedeemPromo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PromoRow row) return;

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            using var response = await ApiClient.Http.PostAsync($"api/promos/{row.Source.PromoId}/redeem", null);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                await LoadPromosAsync();
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

        // If a popup is open, the page-level bar is hidden behind it, so show the error there too.
        if (FormOverlay.Visibility == Visibility.Visible)
        {
            PopupSuccessBar.IsOpen = false;
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

        // The Promos panel stays open after giving or redeeming a promo,
        // so the confirmation must appear inside the popup as well.
        if (FormOverlay.Visibility == Visibility.Visible)
        {
            PopupErrorBar.IsOpen = false;
            PopupSuccessBar.Title = "Success";
            PopupSuccessBar.Message = message;
            PopupSuccessBar.IsOpen = true;
        }
    }
}