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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace PAWS.winui.Views;

public sealed partial class CustomerServicePage : Page
{
    // ----- Animation tuning -----
    private const int MaxAnimatedCards = 10;     // only the first cards animate; the rest are off-screen
    private const double CardStaggerMs = 55;
    private const double CountUpMs = 700;

    public class RequestRow : INotifyPropertyChanged
    {
        public CustomerServiceRequestItem Source { get; }
        public RequestRow(CustomerServiceRequestItem source) => Source = source;

        private static bool IsAdminOrStaff =>
            AuthSession.Roles.Contains("Administrator") || AuthSession.Roles.Contains("Staff");

        // Looks up a PAWS brush from PawsTheme.xaml
        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        // ----- Text on the card -----
        public string SubjectText => Source.Subject;
        public string MessageText => Source.Message;
        public string DateText => Source.CreatedAt.ToString("MMM d, yyyy");

        public string FromText => $"From: {Source.CustomerName}";
        public Visibility FromVisibility => IsAdminOrStaff ? Visibility.Visible : Visibility.Collapsed;

        public string ResponseText => Source.Response ?? string.Empty;
        public Visibility ResponseVisibility =>
            !string.IsNullOrWhiteSpace(Source.Response) ? Visibility.Visible : Visibility.Collapsed;

        // ----- Type badge -----
        public string TypeLabel => (Source.Type ?? string.Empty).ToUpperInvariant();

        public Brush TypeBadgeBackground => Source.Type switch
        {
            "Inquiry" => Res("PawsInfoSoftBrush"),
            "Complaint" => Res("PawsErrorSoftBrush"),
            "Feedback" => Res("PawsPrimarySoftBrush"),
            _ => Res("PawsHoverSurfaceBrush")
        };

        public Brush TypeBadgeForeground => Source.Type switch
        {
            "Inquiry" => Res("PawsInfoBrush"),
            "Complaint" => Res("PawsErrorBrush"),
            "Feedback" => Res("PawsPrimaryBrush"),
            _ => Res("PawsTextSecondaryBrush")
        };

        // ----- Status badge -----
        public string StatusLabel => (Source.Status ?? string.Empty).ToUpperInvariant();

        public Brush StatusBadgeBackground => Source.Status switch
        {
            "New" => Res("PawsInfoSoftBrush"),
            "In Progress" => Res("PawsWarningSoftBrush"),
            "Resolved" => Res("PawsSuccessSoftBrush"),
            _ => Res("PawsHoverSurfaceBrush")   // Closed and anything else
        };

        public Brush StatusBadgeForeground => Source.Status switch
        {
            "New" => Res("PawsInfoBrush"),
            "In Progress" => Res("PawsWarningBrush"),
            "Resolved" => Res("PawsSuccessBrush"),
            _ => Res("PawsTextSecondaryBrush")
        };

        // ----- Button visibility: SAME rules as the old page -----
        public Visibility RespondVisibility =>
            IsAdminOrStaff && (Source.Status == "New" || Source.Status == "In Progress")
                ? Visibility.Visible : Visibility.Collapsed;

        public Visibility CloseVisibility =>
            IsAdminOrStaff && Source.Status == "Resolved" ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private const string AnyStatus = "All Statuses";
    private const string AnyType = "All Types";

    private readonly ObservableCollection<RequestRow> _rows = new();
    private RequestRow? _actingRow;
    private bool _hasLoaded;
    private bool _suppressFilter;

    // ----- Animation state -----
    private bool _animate = true;
    private bool _animateCards;                  // true only right after the list is (re)loaded
    private bool _overlayWasOpen;

    private readonly DispatcherTimer _cardsWindowTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    private readonly DispatcherTimer _countTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private DateTime _countStart;
    private int _countTarget;

    private static bool IsAdminOrStaff =>
        AuthSession.Roles.Contains("Administrator") || AuthSession.Roles.Contains("Staff");
    private static bool IsAdopter => AuthSession.Roles.Contains("Adopter");

    public CustomerServicePage()
    {
        InitializeComponent();
        RequestsList.ItemsSource = _rows;

        // The popup background must follow whichever form panel is showing.
        WatchPanel(NewFormPanel);
        WatchPanel(RespondFormPanel);

        _countTimer.Tick += CountTimer_Tick;

        // After the cards have been created, stop animating them (so later updates are instant).
        _cardsWindowTimer.Tick += (s, e) =>
        {
            _cardsWindowTimer.Stop();
            _animateCards = false;
        };

        PageTitle.Text = IsAdopter ? "My Requests" : "Customer Service";
        NewRequestButton.Visibility = IsAdopter ? Visibility.Visible : Visibility.Collapsed;

        StatusFilterBox.ItemsSource = new List<string> { AnyStatus, "New", "In Progress", "Resolved", "Closed" };
        StatusFilterBox.SelectedIndex = 0;
        TypeFilterBox.ItemsSource = new List<string> { AnyType, "Inquiry", "Complaint", "Feedback" };
        TypeFilterBox.SelectedIndex = 0;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _animate = ReadAnimationSetting();

        if (_animate)
        {
            // The header and the filter card fade in one after another (they do not depend on data).
            PrepareFade(HeaderPanel);
            PrepareFade(ToolbarCard);
            PlayFade(HeaderPanel, 0, 420);
            PlayFade(ToolbarCard, 110, 420);
        }

        await LoadRequestsAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Never leave a timer running after the page is gone.
        _countTimer.Stop();
        _cardsWindowTimer.Stop();
        _animateCards = false;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadRequestsAsync();

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilter)
        {
            return;
        }

        await LoadRequestsAsync();
    }

    private async void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        // Changing two boxes would otherwise trigger two separate reloads.
        _suppressFilter = true;
        try
        {
            StatusFilterBox.SelectedIndex = 0;
            TypeFilterBox.SelectedIndex = 0;
        }
        finally
        {
            _suppressFilter = false;
        }

        await LoadRequestsAsync();
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

    // Smoothly moves an element to a vertical offset (used for the card hover lift).
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

    // ----- Request cards: slide up and fade in right after a (re)load -----

    private void Card_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_animateCards || sender is not Border card || card.DataContext is not RequestRow data)
        {
            return;
        }

        int index = _rows.IndexOf(data);
        if (index < 0 || index >= MaxAnimatedCards)
        {
            return;
        }

        PlaySlideFadeIn(card, 14, index * CardStaggerMs, 400);
    }

    // ----- Card hover: the border turns coral and the card lifts 3 px -----

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            AnimateLift(card, -3f);
            card.BorderBrush = (Brush)Application.Current.Resources["PawsPrimaryBrush"];
        }
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            AnimateLift(card, 0f);
            card.BorderBrush = (Brush)Application.Current.Resources["PawsBorderBrush"];
        }
    }

    // ----- Count-up for "N requests" -----

    private static string CountLabel(int n) => n == 1 ? "1 request" : $"{n} requests";

    private void StartCountUp(int target)
    {
        _countTarget = target;
        _countStart = DateTime.UtcNow;
        ResultCountText.Text = CountLabel(0);
        _countTimer.Start();
    }

    private void CountTimer_Tick(object? sender, object e)
    {
        double t = Math.Min(1.0, (DateTime.UtcNow - _countStart).TotalMilliseconds / CountUpMs);

        // Ease-out: fast at first, slowing down at the end.
        double eased = 1.0 - Math.Pow(1.0 - t, 3);

        // The last tick always writes the exact value.
        int shown = t >= 1.0 ? _countTarget : (int)Math.Round(_countTarget * eased);
        ResultCountText.Text = CountLabel(shown);

        if (t >= 1.0)
        {
            _countTimer.Stop();
        }
    }

    // ----- Form popup overlay -----

    // The popup background (FormOverlay) must be visible whenever one of the
    // form panels is visible. We listen for any change to a panel's Visibility
    // and update the overlay.
    private void WatchPanel(Border panel)
    {
        panel.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (s, dp) => UpdateFormOverlay());
    }

    private void UpdateFormOverlay()
    {
        bool anyOpen =
            NewFormPanel.Visibility == Visibility.Visible ||
            RespondFormPanel.Visibility == Visibility.Visible;

        FormOverlay.Visibility = anyOpen ? Visibility.Visible : Visibility.Collapsed;

        // Start every popup with a clean slate.
        PopupErrorBar.IsOpen = false;

        // Animate only when the popup goes from closed to open.
        if (anyOpen && !_overlayWasOpen && _animate)
        {
            PrepareFade(FormOverlay);
            PlayFade(FormOverlay, 0, 180);

            Border openPanel = NewFormPanel.Visibility == Visibility.Visible
                ? NewFormPanel
                : RespondFormPanel;

            PlaySlideFadeIn(openPanel, 18, 40, 260);
        }

        _overlayWasOpen = anyOpen;
    }

    // ----- Empty state -----

    private bool FiltersActive =>
        (StatusFilterBox.SelectedItem is string s && s != AnyStatus) ||
        (TypeFilterBox.SelectedItem is string t && t != AnyType);

    private void UpdateEmptyState()
    {
        bool isEmpty = _hasLoaded && _rows.Count == 0 && !Busy.IsActive;

        if (isEmpty)
        {
            if (FiltersActive)
            {
                EmptyTitleText.Text = "No requests match your filters";
                EmptyHintText.Text = "Try a different status or type, or press Reset.";
            }
            else if (IsAdopter)
            {
                EmptyTitleText.Text = "You have no requests yet";
                EmptyHintText.Text = "Use New Request to send an inquiry, complaint or feedback.";
            }
            else
            {
                EmptyTitleText.Text = "No requests yet";
                EmptyHintText.Text = "Requests from adopters will appear here.";
            }
        }

        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    // ----- Load -----

    // 'animate' is true when the page opens, Refresh is pressed or a filter changes;
    // false after a submit, a response or a close, so the list updates in place.
    private async Task LoadRequestsAsync(bool animate = true)
    {
        Busy.IsActive = true;
        ListHost.Opacity = 0.5;
        ErrorBar.IsOpen = false;

        _animate = ReadAnimationSetting();

        try
        {
            var query = new List<string>();
            if (StatusFilterBox.SelectedItem is string status && status != AnyStatus)
            {
                query.Add($"status={Uri.EscapeDataString(status)}");
            }
            if (TypeFilterBox.SelectedItem is string type && type != AnyType)
            {
                query.Add($"type={Uri.EscapeDataString(type)}");
            }

            var url = "api/customer-service" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

            using var response = await ApiClient.Http.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var items = await response.Content.ReadFromJsonAsync<List<CustomerServiceRequestItem>>() ?? new();

                // Cards animate only right after a load; give them a short window to be created.
                if (animate && _animate)
                {
                    _animateCards = true;
                    _cardsWindowTimer.Stop();
                    _cardsWindowTimer.Start();
                }

                _rows.Clear();
                foreach (var i in items)
                {
                    _rows.Add(new RequestRow(i));
                }
                _hasLoaded = true;

                if (animate && _animate && _rows.Count > 0)
                {
                    StartCountUp(_rows.Count);
                }
                else
                {
                    _countTimer.Stop();
                    ResultCountText.Text = CountLabel(_rows.Count);
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
            ListHost.Opacity = 1;
            UpdateEmptyState();
        }
    }

    // ----- New request (Adopter) -----

    private void NewRequest_Click(object sender, RoutedEventArgs e)
    {
        NewTypeBox.SelectedIndex = -1;
        NewSubjectBox.Text = string.Empty;
        NewMessageBox.Text = string.Empty;
        NewFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelNewForm_Click(object sender, RoutedEventArgs e)
    {
        NewFormPanel.Visibility = Visibility.Collapsed;
    }

    private async void SubmitNew_Click(object sender, RoutedEventArgs e)
    {
        var type = (NewTypeBox.SelectedItem as ComboBoxItem)?.Content as string;
        var subject = NewSubjectBox.Text.Trim();
        var message = NewMessageBox.Text.Trim();

        if (type is null || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(message))
        {
            ShowError("Please choose a type and fill in the subject and message.");
            return;
        }

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            var request = new SubmitCustomerServiceRequest { Type = type, Subject = subject, Message = message };
            using var response = await ApiClient.Http.PostAsJsonAsync("api/customer-service", request);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                NewFormPanel.Visibility = Visibility.Collapsed;
                await LoadRequestsAsync(animate: false);
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

    // ----- Respond (Staff/Admin) -----

    private void Respond_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RequestRow row) return;
        _actingRow = row;
        RespondFormTitle.Text = $"Respond to: {row.Source.Subject}";
        RespondTextBox.Text = row.Source.Response ?? string.Empty;
        RespondStatusBox.SelectedIndex = -1;
        RespondFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelRespondForm_Click(object sender, RoutedEventArgs e)
    {
        RespondFormPanel.Visibility = Visibility.Collapsed;
        _actingRow = null;
    }

    private async void SaveResponse_Click(object sender, RoutedEventArgs e)
    {
        if (_actingRow is null) return;

        var responseText = RespondTextBox.Text.Trim();
        var status = (RespondStatusBox.SelectedItem as ComboBoxItem)?.Content as string;

        if (string.IsNullOrWhiteSpace(responseText) || status is null)
        {
            ShowError("Please enter a response and choose a new status.");
            return;
        }

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            var request = new RespondRequest { Response = responseText, Status = status };
            using var response = await ApiClient.Http.PostAsJsonAsync(
                $"api/customer-service/{_actingRow.Source.CustomerServiceRequestId}/respond", request);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                RespondFormPanel.Visibility = Visibility.Collapsed;
                _actingRow = null;
                await LoadRequestsAsync(animate: false);
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

    // ----- Close (Staff/Admin) -----

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not RequestRow row) return;

        Busy.IsActive = true;
        try
        {
            var request = new SetRequestStatus { Status = "Closed" };
            using var response = await ApiClient.Http.PostAsJsonAsync(
                $"api/customer-service/{row.Source.CustomerServiceRequestId}/status", request);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                await LoadRequestsAsync(animate: false);
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