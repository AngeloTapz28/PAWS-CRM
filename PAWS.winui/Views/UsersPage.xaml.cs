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

public sealed partial class UsersPage : Page
{
    private const string AllRoles = "All Roles";

    // ----- Animation tuning -----
    private const int MaxAnimatedRows = 12;      // only the first rows animate; the rest are off-screen
    private const double RowStaggerMs = 45;
    private const double CountUpMs = 700;

    // One row's worth of display logic, kept separate from the raw API model
    // (UserListItem) so the XAML can bind to simple computed properties.
    public class UserRow : INotifyPropertyChanged
    {
        public UserListItem Source { get; }

        public UserRow(UserListItem source)
        {
            Source = source;
        }

        // Looks up a PAWS brush from PawsTheme.xaml
        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        public int UserId => Source.UserId;
        public string FullName => Source.FullName;
        public string Email => Source.Email;

        public string Initials
        {
            get
            {
                var parts = (Source.FullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "?";
                if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpperInvariant();
                return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
            }
        }

        public string PhoneText => string.IsNullOrWhiteSpace(Source.Phone) ? "\u2014" : Source.Phone!;

        // ----- Role badge -----
        public string RoleLabel => (Source.Role ?? string.Empty).ToUpperInvariant();

        public Brush RoleBadgeBackground => Source.Role switch
        {
            "Administrator" => Res("PawsPrimarySoftBrush"),
            "Staff" => Res("PawsInfoSoftBrush"),
            "Veterinarian" => Res("PawsSuccessSoftBrush"),
            _ => Res("PawsHoverSurfaceBrush")
        };

        public Brush RoleBadgeForeground => Source.Role switch
        {
            "Administrator" => Res("PawsPrimaryBrush"),
            "Staff" => Res("PawsInfoBrush"),
            "Veterinarian" => Res("PawsSuccessBrush"),
            _ => Res("PawsTextSecondaryBrush")
        };

        // ----- Status badge -----
        public string StatusLabel => (Source.Status ?? string.Empty).ToUpperInvariant();
        public Brush StatusBadgeBackground => Source.IsActive ? Res("PawsSuccessSoftBrush") : Res("PawsErrorSoftBrush");
        public Brush StatusBadgeForeground => Source.IsActive ? Res("PawsSuccessBrush") : Res("PawsErrorBrush");

        // Never show action buttons for your own row, to avoid inviting a click
        // that the API will refuse anyway (self-protection is enforced server-side,
        // Step 15.7a; this is convenience only).
        private bool IsSelf => Source.UserId == AuthSession.UserId;

        public Visibility SelfVisibility => IsSelf ? Visibility.Visible : Visibility.Collapsed;

        public Visibility EditButtonVisibility =>
            IsSelf ? Visibility.Collapsed : Visibility.Visible;

        public Visibility DeactivateButtonVisibility =>
            (!IsSelf && Source.IsActive) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ActivateButtonVisibility =>
            (!IsSelf && !Source.IsActive) ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private ObservableCollection<UserRow> _rows = new();
    private List<UserListItem> _allUsers = new();
    private bool _hasLoaded;
    private bool _suppressFilter;

    // Null while adding a new user; holds the UserId being edited otherwise.
    private int? _editingUserId;

    // ----- Animation state -----
    private bool _animate = true;
    private bool _animateRows;                   // true only right after the list is (re)loaded
    private bool _overlayWasOpen;

    private readonly DispatcherTimer _rowsWindowTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    private readonly DispatcherTimer _countTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private DateTime _countStart;
    private int _countTarget;

    public UsersPage()
    {
        InitializeComponent();
        UsersList.ItemsSource = _rows;

        // The popup background must follow the form panel.
        WatchPanel(FormPanel);

        _countTimer.Tick += CountTimer_Tick;

        // After the list has been created, stop animating rows (so later filtering is instant).
        _rowsWindowTimer.Tick += (s, e) =>
        {
            _rowsWindowTimer.Stop();
            _animateRows = false;
        };

        RoleFilterBox.ItemsSource = new List<string> { AllRoles, "Administrator", "Staff", "Veterinarian", "Adopter" };
        RoleFilterBox.SelectedIndex = 0;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _animate = ReadAnimationSetting();

        if (_animate)
        {
            // The three blocks fade in one after another (they do not depend on data).
            PrepareFade(HeaderPanel);
            PrepareFade(ToolbarCard);
            PrepareFade(TableCard);
            PlayFade(HeaderPanel, 0, 420);
            PlayFade(ToolbarCard, 110, 420);
            PlayFade(TableCard, 220, 480);
        }

        await LoadUsersAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Never leave a timer running after the page is gone.
        _countTimer.Stop();
        _rowsWindowTimer.Stop();
        _animateRows = false;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadUsersAsync();
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
            // An animation problem must never leave a row invisible.
            v.Opacity = 1f;
            element.Translation = Vector3.Zero;
        }
    }

    // Each row slides up and fades in when it is created, but only right after the
    // list was (re)loaded, and only for the first few rows.
    private void Row_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_animateRows || sender is not Border row || row.DataContext is not UserRow data)
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

    // ----- Count-up for the user count under the title -----

    private static string CountLabel(int n) => n == 1 ? "1 user" : $"{n} users";

    private void StartCountUp(int target)
    {
        _countTarget = target;
        _countStart = DateTime.UtcNow;
        CountText.Text = CountLabel(0);
        _countTimer.Start();
    }

    private void CountTimer_Tick(object? sender, object e)
    {
        double t = Math.Min(1.0, (DateTime.UtcNow - _countStart).TotalMilliseconds / CountUpMs);

        // Ease-out: fast at first, slowing down at the end.
        double eased = 1.0 - Math.Pow(1.0 - t, 3);

        // The last tick always writes the exact value.
        int shown = t >= 1.0 ? _countTarget : (int)Math.Round(_countTarget * eased);
        CountText.Text = CountLabel(shown);

        if (t >= 1.0)
        {
            _countTimer.Stop();
        }
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

    // ----- Search and role filter (applied to the users already loaded) -----

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

    private void RoleFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilter)
        {
            return;
        }

        ApplyFilters();
    }

    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        _suppressFilter = true;
        try
        {
            SearchBox.Text = string.Empty;
            RoleFilterBox.SelectedIndex = 0;
        }
        finally
        {
            _suppressFilter = false;
        }

        ApplyFilters();
    }

    private bool FiltersActive =>
        !string.IsNullOrWhiteSpace(SearchBox.Text) ||
        (RoleFilterBox.SelectedItem is string r && r != AllRoles);

    // 'afterLoad' is true only when the list has just been loaded from the API,
    // which is the only time the count and rows animate.
    private void ApplyFilters(bool afterLoad = false)
    {
        var searchText = SearchBox.Text?.Trim() ?? string.Empty;
        var roleFilter = RoleFilterBox.SelectedItem as string ?? AllRoles;

        var filtered = _allUsers
            .Where(u =>
                (roleFilter == AllRoles || u.Role == roleFilter) &&
                (searchText.Length == 0 ||
                 (u.FullName ?? string.Empty).Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                 (u.Email ?? string.Empty).Contains(searchText, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(u => u.FullName)
            .ToList();

        // Rows animate only right after a load; give them a short window to be created.
        if (afterLoad && _animate)
        {
            _animateRows = true;
            _rowsWindowTimer.Stop();
            _rowsWindowTimer.Start();
        }

        _rows.Clear();
        foreach (var user in filtered)
        {
            _rows.Add(new UserRow(user));
        }

        var total = CountLabel(_allUsers.Count);

        if (afterLoad && _animate && _allUsers.Count > 0)
        {
            StartCountUp(_allUsers.Count);
        }
        else
        {
            _countTimer.Stop();
            CountText.Text = total;
        }

        TotalText.Text = filtered.Count == _allUsers.Count ? total : $"Showing {filtered.Count} of {_allUsers.Count}";

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        bool isEmpty = _hasLoaded && _rows.Count == 0 && !Busy.IsActive;

        if (isEmpty)
        {
            if (FiltersActive)
            {
                EmptyTitleText.Text = "No users match your filters";
                EmptyHintText.Text = "Try a different search or role, or press Reset.";
            }
            else
            {
                EmptyTitleText.Text = "No users yet";
                EmptyHintText.Text = "Use Add User to create the first account.";
            }
        }

        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        TableCard.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    // ----- Load -----

    private async Task LoadUsersAsync()
    {
        RefreshButton.IsEnabled = false;
        Busy.IsActive = true;
        ListHost.Opacity = 0.5;
        ErrorBar.IsOpen = false;

        _animate = ReadAnimationSetting();

        try
        {
            using var response = await ApiClient.Http.GetAsync("api/users");

            if (response.IsSuccessStatusCode)
            {
                var users = await response.Content.ReadFromJsonAsync<List<UserListItem>>();
                _allUsers = users ?? new List<UserListItem>();
                _hasLoaded = true;
                ApplyFilters(afterLoad: true);
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthSession.Clear();
                RootNavigation.GoToLogin();
                return;
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                ShowError("You do not have permission to manage users.");
            }
            else
            {
                ShowError($"Could not load users (HTTP {(int)response.StatusCode}).");
            }
        }
        catch (TaskCanceledException)
        {
            ShowError("The API did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            ShowError($"Could not reach the API. {ex.Message}");
        }
        finally
        {
            Busy.IsActive = false;
            ListHost.Opacity = 1;
            RefreshButton.IsEnabled = true;
            UpdateEmptyState();
        }
    }

    // ----- Add / Edit form -----

    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        _editingUserId = null;
        FormTitle.Text = "Add User";
        FullNameBox.Text = string.Empty;
        EmailBox.Text = string.Empty;
        EmailBox.IsEnabled = true;
        PhoneBox.Text = string.Empty;
        PasswordPanel.Visibility = Visibility.Visible;
        PasswordInput.Password = string.Empty;
        ConfirmPasswordInput.Password = string.Empty;
        RoleBox.SelectedIndex = -1;

        FormPanel.Visibility = Visibility.Visible;
    }

    private void EditUser_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not UserRow row)
        {
            return;
        }

        _editingUserId = row.UserId;
        FormTitle.Text = $"Edit {row.FullName}";
        FullNameBox.Text = row.FullName;
        EmailBox.Text = row.Email;
        EmailBox.IsEnabled = false; // Email is not editable (Step 15.7a design decision).
        PhoneBox.Text = row.Source.Phone ?? string.Empty;
        PasswordPanel.Visibility = Visibility.Collapsed; // Not changing passwords here.

        SelectRole(row.Source.Role);

        FormPanel.Visibility = Visibility.Visible;
    }

    private void SelectRole(string roleName)
    {
        foreach (var item in RoleBox.Items)
        {
            if (item is ComboBoxItem comboItem && (comboItem.Content as string) == roleName)
            {
                RoleBox.SelectedItem = comboItem;
                return;
            }
        }
        RoleBox.SelectedIndex = -1;
    }

    private void CancelForm_Click(object sender, RoutedEventArgs e)
    {
        FormPanel.Visibility = Visibility.Collapsed;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var fullName = FullNameBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        var phone = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim();
        var role = (RoleBox.SelectedItem as ComboBoxItem)?.Content as string;

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(role))
        {
            ShowError("Please fill in Full Name and Role.");
            return;
        }

        SaveButton.IsEnabled = false;
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            HttpResponseMessage response;

            if (_editingUserId is int userId)
            {
                var updateRequest = new UpdateUserRequest
                {
                    FullName = fullName,
                    Phone = phone,
                    Role = role
                };
                response = await ApiClient.Http.PutAsJsonAsync($"api/users/{userId}", updateRequest);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(email))
                {
                    ShowError("Please fill in Email.");
                    return;
                }

                var password = PasswordInput.Password;
                var confirmPassword = ConfirmPasswordInput.Password;

                if (string.IsNullOrWhiteSpace(password) || password != confirmPassword)
                {
                    ShowError("Password and Confirm Password must match and cannot be empty.");
                    return;
                }

                var createRequest = new CreateUserRequest
                {
                    FullName = fullName,
                    Email = email,
                    Phone = phone,
                    Password = password,
                    ConfirmPassword = confirmPassword,
                    Role = role
                };
                response = await ApiClient.Http.PostAsJsonAsync("api/users", createRequest);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<SimpleMessage>();
                    ShowSuccess(result?.Message ?? "Saved.");
                    FormPanel.Visibility = Visibility.Collapsed;
                    await LoadUsersAsync();
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    AuthSession.Clear();
                    RootNavigation.GoToLogin();
                }
                else
                {
                    var json = await response.Content.ReadAsStringAsync();
                    ShowError(ReadField(json, "message") is string msg && msg != "(not provided)"
                        ? msg
                        : ReadFirstValidationMessage(json) ?? "Could not save the user.");
                }
            }
        }
        catch (TaskCanceledException)
        {
            ShowError("The API did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            ShowError($"Could not reach the API. {ex.Message}");
        }
        finally
        {
            Busy.IsActive = false;
            SaveButton.IsEnabled = true;
        }
    }

    // ----- Activate / Deactivate -----

    private async void DeactivateUser_Click(object sender, RoutedEventArgs e)
    {
        await SetActiveStatus(sender, activate: false);
    }

    private async void ActivateUser_Click(object sender, RoutedEventArgs e)
    {
        await SetActiveStatus(sender, activate: true);
    }

    private async Task SetActiveStatus(object sender, bool activate)
    {
        if ((sender as Button)?.Tag is not UserRow row)
        {
            return;
        }

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            var action = activate ? "activate" : "deactivate";
            using var response = await ApiClient.Http.PostAsync($"api/users/{row.UserId}/{action}", null);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SimpleMessage>();
                ShowSuccess(result?.Message ?? "Updated.");
                await LoadUsersAsync();
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthSession.Clear();
                RootNavigation.GoToLogin();
            }
            else
            {
                var json = await response.Content.ReadAsStringAsync();
                ShowError(ReadField(json, "message") is string msg && msg != "(not provided)"
                    ? msg
                    : $"Could not {action} the user.");
            }
        }
        catch (TaskCanceledException)
        {
            ShowError("The API did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            ShowError($"Could not reach the API. {ex.Message}");
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    // ----- Helpers -----

    private static string ReadField(string json, string fieldName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(fieldName, out var value))
            {
                return value.ToString();
            }
        }
        catch (JsonException)
        {
            // Not JSON. Fall through.
        }
        return "(not provided)";
    }

    private static string? ReadFirstValidationMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                foreach (var field in errors.EnumerateObject())
                {
                    foreach (var message in field.Value.EnumerateArray())
                    {
                        return message.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON. Fall through.
        }
        return null;
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