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

public sealed partial class AdoptionApplicationsPage : Page
{
    // ----- Animation tuning -----
    private const int MaxAnimatedRows = 12;      // only the first rows animate; the rest are off-screen
    private const double RowStaggerMs = 45;
    private const double CountUpMs = 700;

    public class ApplicationRow : INotifyPropertyChanged
    {
        public AdoptionApplicationItem Source { get; }
        public ApplicationRow(AdoptionApplicationItem source) => Source = source;

        private static bool IsAdminOrStaff =>
            AuthSession.Roles.Contains("Administrator") || AuthSession.Roles.Contains("Staff");
        private bool IsOwn => Source.UserId == AuthSession.UserId;

        // Looks up a PAWS brush from PawsTheme.xaml
        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        // ----- Table cells -----
        public string AppNumberText => Source.ApplicationNumber;
        public string PetNameText => Source.PetName;

        // Admin/Staff see the applicant; an Adopter would only see their own name,
        // so that column shows who reviewed the application instead.
        public string ThirdColumnText => IsAdminOrStaff
            ? Source.ApplicantName
            : (string.IsNullOrWhiteSpace(Source.ReviewerName) ? "\u2014" : Source.ReviewerName);

        public string SubmittedText => Source.AppliedAt.ToString("MMM d, yyyy");

        // ----- Status badge -----
        public string StatusLabel => (Source.ApplicationStatus ?? string.Empty).ToUpperInvariant();

        public Brush StatusBadgeBackground => Source.ApplicationStatus switch
        {
            "Submitted" => Res("PawsInfoSoftBrush"),
            "Under Review" => Res("PawsWarningSoftBrush"),
            "Interview" => Res("PawsWarningSoftBrush"),
            "Approved" => Res("PawsSuccessSoftBrush"),
            "Completed" => Res("PawsNavyBrush"),
            "Rejected" => Res("PawsErrorSoftBrush"),
            _ => Res("PawsHoverSurfaceBrush")   // Withdrawn and anything else
        };

        public Brush StatusBadgeForeground => Source.ApplicationStatus switch
        {
            "Submitted" => Res("PawsInfoBrush"),
            "Under Review" => Res("PawsWarningBrush"),
            "Interview" => Res("PawsWarningBrush"),
            "Approved" => Res("PawsSuccessBrush"),
            "Completed" => new SolidColorBrush(Microsoft.UI.Colors.White),
            "Rejected" => Res("PawsErrorBrush"),
            _ => Res("PawsTextSecondaryBrush")
        };

        // ----- Detail lines under a row -----
        public string InterviewText => $"Interview: {Source.InterviewDate:MMM d, yyyy}" +
            (string.IsNullOrWhiteSpace(Source.InterviewNotes) ? "" : $" \u2014 {Source.InterviewNotes}");
        public Visibility InterviewVisibility =>
            Source.InterviewDate.HasValue ? Visibility.Visible : Visibility.Collapsed;
        public string RejectionText => $"Reason: {Source.RejectionReason}";
        public Visibility RejectionVisibility =>
            !string.IsNullOrWhiteSpace(Source.RejectionReason) ? Visibility.Visible : Visibility.Collapsed;

        // ----- Button visibility: SAME rules as the old page -----
        public Visibility WithdrawVisibility =>
            IsOwn && !IsAdminOrStaff &&
            (Source.ApplicationStatus == "Submitted" || Source.ApplicationStatus == "Under Review")
                ? Visibility.Visible : Visibility.Collapsed;

        public Visibility UnderReviewVisibility =>
            IsAdminOrStaff && Source.ApplicationStatus == "Submitted" ? Visibility.Visible : Visibility.Collapsed;

        public Visibility InterviewButtonVisibility =>
            IsAdminOrStaff && Source.ApplicationStatus == "Under Review" ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ApproveVisibility =>
            IsAdminOrStaff && (Source.ApplicationStatus == "Submitted" || Source.ApplicationStatus == "Under Review")
                ? Visibility.Visible : Visibility.Collapsed;

        public Visibility RejectVisibility =>
            IsAdminOrStaff && (Source.ApplicationStatus == "Submitted" || Source.ApplicationStatus == "Under Review")
                ? Visibility.Visible : Visibility.Collapsed;

        public Visibility CompleteVisibility =>
            IsAdminOrStaff && Source.ApplicationStatus == "Approved" ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private const string AllStatuses = "All Statuses";

    private readonly ObservableCollection<ApplicationRow> _rows = new();
    private List<ApplicationRow> _allRows = new();     // everything the API returned
    private ApplicationRow? _actingRow;
    private bool _hasLoaded;
    private bool _suppressFilter;

    // ----- Animation state -----
    private bool _animate = true;
    private bool _animateRows;                   // true only right after the list is (re)loaded
    private bool _overlayWasOpen;

    private readonly DispatcherTimer _rowsWindowTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    private readonly DispatcherTimer _countTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private DateTime _countStart;
    private int _countTarget;

    private static bool IsAdminOrStaff =>
        AuthSession.Roles.Contains("Administrator") || AuthSession.Roles.Contains("Staff");

    public AdoptionApplicationsPage()
    {
        InitializeComponent();
        ApplicationsList.ItemsSource = _rows;

        // The popup background must follow whichever form panel is showing.
        WatchPanel(UnderReviewFormPanel);
        WatchPanel(InterviewFormPanel);
        WatchPanel(ApproveFormPanel);
        WatchPanel(RejectFormPanel);

        _countTimer.Tick += CountTimer_Tick;

        // After the rows have been created, stop animating them (so later updates are instant).
        _rowsWindowTimer.Tick += (s, e) =>
        {
            _rowsWindowTimer.Stop();
            _animateRows = false;
        };

        PageTitle.Text = IsAdminOrStaff ? "Adoption Applications" : "My Applications";
        TableTitle.Text = IsAdminOrStaff ? "All Applications" : "My Applications";
        ThirdColumnHeader.Text = IsAdminOrStaff ? "APPLICANT" : "REVIEWED BY";

        StatusFilterBox.ItemsSource = new List<string>
        {
            AllStatuses, "Submitted", "Under Review", "Approved", "Rejected", "Completed", "Withdrawn"
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

        await LoadApplicationsAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Never leave a timer running after the page is gone.
        _countTimer.Stop();
        _rowsWindowTimer.Stop();
        _animateRows = false;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadApplicationsAsync();

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilter)
        {
            return;
        }

        await LoadApplicationsAsync();
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

    // ----- Rows: slide up and fade in right after a (re)load -----

    private void Row_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_animateRows || sender is not Border row || row.DataContext is not ApplicationRow data)
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

    // ----- Count-up for "N applications" -----

    private static string CountLabel(int n) => n == 1 ? "1 application" : $"{n} applications";

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

    // ----- Form popup overlay -----

    // The popup background (FormOverlay) must be visible whenever one of the four
    // form panels is visible. Instead of editing every place that shows or hides a
    // panel, we listen for any change to a panel's Visibility and update the overlay.
    private void WatchPanel(Border panel)
    {
        panel.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (s, dp) => UpdateFormOverlay());
    }

    private void UpdateFormOverlay()
    {
        bool anyOpen =
            UnderReviewFormPanel.Visibility == Visibility.Visible ||
            InterviewFormPanel.Visibility == Visibility.Visible ||
            ApproveFormPanel.Visibility == Visibility.Visible ||
            RejectFormPanel.Visibility == Visibility.Visible;

        FormOverlay.Visibility = anyOpen ? Visibility.Visible : Visibility.Collapsed;

        // Start every popup with a clean slate.
        PopupErrorBar.IsOpen = false;

        // Animate only when the popup goes from closed to open.
        if (anyOpen && !_overlayWasOpen && _animate)
        {
            PrepareFade(FormOverlay);
            PlayFade(FormOverlay, 0, 180);

            Border openPanel =
                UnderReviewFormPanel.Visibility == Visibility.Visible ? UnderReviewFormPanel
                : InterviewFormPanel.Visibility == Visibility.Visible ? InterviewFormPanel
                : ApproveFormPanel.Visibility == Visibility.Visible ? ApproveFormPanel
                : RejectFormPanel;

            PlaySlideFadeIn(openPanel, 18, 40, 260);
        }

        _overlayWasOpen = anyOpen;
    }

    // ----- Table: fill the window, keep columns aligned -----

    private void TableScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        TableGrid.Width = Math.Max(TableGrid.MinWidth, e.NewSize.Width);
    }

    // ----- Search (client-side, over the applications already loaded) -----

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();

    private static bool ContainsText(string? value, string term) =>
        value?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true;

    // 'animateCount' is true only when the list has just been loaded from the API
    // and the caller asked for animation; searching is always instant.
    private void ApplySearch(bool animateCount = false)
    {
        string term = SearchBox.Text.Trim();

        IEnumerable<ApplicationRow> matches = _allRows;
        if (term.Length > 0)
        {
            matches = _allRows.Where(r =>
                ContainsText(r.Source.ApplicationNumber, term) ||
                ContainsText(r.Source.PetName, term) ||
                (IsAdminOrStaff && ContainsText(r.Source.ApplicantName, term)));
        }

        var list = matches.ToList();

        _rows.Clear();
        foreach (var row in list)
        {
            _rows.Add(row);
        }

        if (animateCount && _animate && list.Count > 0)
        {
            StartCountUp(list.Count);
        }
        else
        {
            _countTimer.Stop();
            TotalText.Text = CountLabel(list.Count);
        }

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        bool isEmpty = _hasLoaded && _rows.Count == 0 && !Busy.IsActive;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        TableCard.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        _suppressFilter = true;
        try
        {
            SearchBox.Text = string.Empty;
            StatusFilterBox.SelectedIndex = 0;
        }
        finally
        {
            _suppressFilter = false;
        }

        await LoadApplicationsAsync();
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

    // ----- Load -----

    // 'animate' is true when the page opens, Refresh is pressed or the status filter changes;
    // false after a review action, so the table updates in place.
    private async Task LoadApplicationsAsync(bool animate = true)
    {
        Busy.IsActive = true;
        ListHost.Opacity = 0.5;
        ErrorBar.IsOpen = false;

        _animate = ReadAnimationSetting();

        try
        {
            var url = "api/adoption-applications";
            if (StatusFilterBox.SelectedItem is string status && status != AllStatuses)
            {
                url += $"?status={Uri.EscapeDataString(status)}";
            }

            using var response = await ApiClient.Http.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var apps = await response.Content.ReadFromJsonAsync<List<AdoptionApplicationItem>>() ?? new();
                _allRows = apps.Select(a => new ApplicationRow(a)).ToList();
                _hasLoaded = true;

                // Rows animate only right after a load; give them a short window to be created.
                if (animate && _animate)
                {
                    _animateRows = true;
                    _rowsWindowTimer.Stop();
                    _rowsWindowTimer.Start();
                }

                ApplySearch(animateCount: animate);
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

    // ----- Withdraw -----

    private async void Withdraw_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ApplicationRow row) return;
        await PostActionAsync($"api/adoption-applications/{row.Source.AdoptionApplicationId}/withdraw", null);
    }

    // ----- Under Review -----

    private void UnderReview_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ApplicationRow row) return;
        _actingRow = row;
        UnderReviewRemarksBox.Text = string.Empty;
        UnderReviewFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelUnderReviewForm_Click(object sender, RoutedEventArgs e)
    {
        UnderReviewFormPanel.Visibility = Visibility.Collapsed;
        _actingRow = null;
    }

    private async void ConfirmUnderReview_Click(object sender, RoutedEventArgs e)
    {
        if (_actingRow is null) return;
        var request = new SetUnderReviewRequest { Remarks = NullIfBlank(UnderReviewRemarksBox.Text) };
        UnderReviewFormPanel.Visibility = Visibility.Collapsed;
        await PostActionAsync($"api/adoption-applications/{_actingRow.Source.AdoptionApplicationId}/under-review", request);
        _actingRow = null;
    }

    // ----- Schedule Interview -----

    private void ScheduleInterview_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ApplicationRow row) return;
        _actingRow = row;
        InterviewDatePicker.Date = DateTimeOffset.Now.AddDays(7);
        InterviewNotesBox.Text = string.Empty;
        InterviewFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelInterviewForm_Click(object sender, RoutedEventArgs e)
    {
        InterviewFormPanel.Visibility = Visibility.Collapsed;
        _actingRow = null;
    }

    private async void ConfirmInterview_Click(object sender, RoutedEventArgs e)
    {
        if (_actingRow is null) return;
        var request = new ScheduleInterviewRequest
        {
            InterviewDate = InterviewDatePicker.Date.DateTime,
            InterviewNotes = NullIfBlank(InterviewNotesBox.Text)
        };
        InterviewFormPanel.Visibility = Visibility.Collapsed;
        await PostActionAsync($"api/adoption-applications/{_actingRow.Source.AdoptionApplicationId}/schedule-interview", request);
        _actingRow = null;
    }

    // ----- Approve -----

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ApplicationRow row) return;
        _actingRow = row;
        ApproveRemarksBox.Text = string.Empty;
        ApproveFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelApproveForm_Click(object sender, RoutedEventArgs e)
    {
        ApproveFormPanel.Visibility = Visibility.Collapsed;
        _actingRow = null;
    }

    private async void ConfirmApprove_Click(object sender, RoutedEventArgs e)
    {
        if (_actingRow is null) return;
        var request = new ApproveApplicationRequest { Remarks = NullIfBlank(ApproveRemarksBox.Text) };
        ApproveFormPanel.Visibility = Visibility.Collapsed;
        await PostActionAsync($"api/adoption-applications/{_actingRow.Source.AdoptionApplicationId}/approve", request);
        _actingRow = null;
    }

    // ----- Reject -----

    private void Reject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ApplicationRow row) return;
        _actingRow = row;
        RejectReasonBox.Text = string.Empty;
        RejectFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelRejectForm_Click(object sender, RoutedEventArgs e)
    {
        RejectFormPanel.Visibility = Visibility.Collapsed;
        _actingRow = null;
    }

    private async void ConfirmReject_Click(object sender, RoutedEventArgs e)
    {
        if (_actingRow is null) return;

        var reason = RejectReasonBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            ShowError("Please enter a rejection reason.");
            return;
        }

        var request = new RejectApplicationRequest { RejectionReason = reason };
        RejectFormPanel.Visibility = Visibility.Collapsed;
        await PostActionAsync($"api/adoption-applications/{_actingRow.Source.AdoptionApplicationId}/reject", request);
        _actingRow = null;
    }

    // ----- Complete -----

    private async void Complete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ApplicationRow row) return;
        await PostActionAsync($"api/adoption-applications/{row.Source.AdoptionApplicationId}/complete",
            new CompleteApplicationRequest());
    }

    // ----- Shared action runner -----

    private async Task PostActionAsync(string url, object? body)
    {
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            using var response = body is null
                ? await ApiClient.Http.PostAsync(url, null)
                : await ApiClient.Http.PostAsJsonAsync(url, body);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                await LoadApplicationsAsync(animate: false);
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