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
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace PAWS.winui.Views;

public sealed partial class ShellPage : Page
{
    // One entry in the page search list (built from the sidebar items this role can open).
    public sealed class PageEntry
    {
        public string Title { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public override string ToString() => Title;
    }

    public static ShellPage? Current { get; private set; }
    private NavigationViewItem? _notificationsMenuItem;
    private List<PageEntry> _pageEntries = new();

    // Checks the API and refreshes the unread count every 30 seconds.
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public ShellPage()
    {
        InitializeComponent();
        Current = this;
        PopulateUserCard();

        BuildMenuForCurrentRole();

        // The search list uses exactly the sidebar items this role can open.
        _pageEntries = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .Select(i => new PageEntry { Title = i.Content as string ?? string.Empty, Tag = i.Tag as string ?? string.Empty })
            .ToList();

        CompanyPillText.Text = AuthSession.IsSuperAdmin ? "Platform" : (AuthSession.CompanyCode ?? string.Empty);
        BellButton.Visibility = AuthSession.IsSuperAdmin ? Visibility.Collapsed : Visibility.Visible;

        _statusTimer.Tick += async (s, e) => await RefreshUnreadBadgeAsync();
        Loaded += ShellPage_Loaded;
        Unloaded += ShellPage_Unloaded;

        // Select the first real item (skip section labels, which are not selectable).
        var firstItem = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
        if (firstItem is not null)
        {
            NavView.SelectedItem = firstItem;
        }

        _ = RefreshUnreadBadgeAsync();
    }

    private void ShellPage_Loaded(object sender, RoutedEventArgs e)
    {
        _statusTimer.Start();

        // The top bar fades in once. If Windows animations are off, it simply appears.
        if (ReadAnimationSetting())
        {
            var v = ElementCompositionPreview.GetElementVisual(TopBar);

            try
            {
                v.Opacity = 0f;

                var c = v.Compositor;
                var a = c.CreateScalarKeyFrameAnimation();
                a.InsertKeyFrame(1f, 1f, c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.8f), new Vector2(0.2f, 1.0f)));
                a.Duration = TimeSpan.FromMilliseconds(450);
                a.DelayTime = TimeSpan.FromMilliseconds(100);
                v.StartAnimation("Opacity", a);
            }
            catch
            {
                v.Opacity = 1f;
            }
        }
    }

    private void ShellPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Never leave a timer running after the shell is gone (for example after sign-out).
        _statusTimer.Stop();

        // NEW: forget this shell so other pages cannot navigate through a page that is gone.
        if (Current == this) Current = null;
    }

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

    // ------------------------------------------------------------
    // User card (bottom of the sidebar)
    // ------------------------------------------------------------
    private void PopulateUserCard()
    {
        if (AuthSession.IsSuperAdmin)
        {
            UserNameText.Text = AuthSession.Email;
            UserRoleText.Text = "Super Admin";
            UserCompanyText.Visibility = Visibility.Collapsed;
            AvatarInitialsText.Text = "SA";
        }
        else
        {
            UserNameText.Text = AuthSession.FullName;
            UserRoleText.Text = string.Join(", ", AuthSession.Roles);
            UserCompanyText.Text = AuthSession.CompanyCode;
            AvatarInitialsText.Text = GetInitials(AuthSession.FullName);
        }
    }

    private static string GetInitials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return "?";
        }

        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return parts[0].Substring(0, 1).ToUpperInvariant();
        }

        return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
    }

    // ------------------------------------------------------------
    // Role-based menu (same permission rules as before)
    // ------------------------------------------------------------
    private void BuildMenuForCurrentRole()
    {
        NavView.MenuItems.Clear();

        if (AuthSession.IsSuperAdmin)
        {
            AddSection("SYSTEM", new List<NavigationViewItem>
            {
                CreateMenuItem("System Administration", "SystemAdministration", Symbol.Setting),
                CreateMenuItem("Subscription Plans", "SubscriptionPlans", Symbol.Shop)
            });
            return;
        }

        var roles = AuthSession.Roles;
        bool isAdminOrStaff = roles.Contains("Administrator") || roles.Contains("Staff");
        bool isVeterinarian = roles.Contains("Veterinarian");
        bool isAdopter = roles.Contains("Adopter");
        bool isInternalUser = isAdminOrStaff || isVeterinarian;

        // ----- MAIN -----
        var main = new List<NavigationViewItem>();
        if (isAdminOrStaff || isVeterinarian)
        {
            main.Add(CreateMenuItem("Dashboard", "Dashboard", Symbol.Home));
        }
        AddSection("MAIN", main);

        // ----- MANAGEMENT (or ADOPTION for adopter-only users) -----
        var management = new List<NavigationViewItem>();
        if (roles.Contains("Administrator"))
        {
            management.Add(CreateMenuItem("Users", "Users", Symbol.People));
        }
        if (isAdminOrStaff)
        {
            management.Add(CreateMenuItem("Pet Categories", "PetCategories", Symbol.Tag));
        }
        if (isAdminOrStaff)
        {
            management.Add(CreateMenuItem("Branches", "Branches", Symbol.MapDrive));
        }
        if (isAdminOrStaff || isAdopter || isVeterinarian)
        {
            management.Add(CreateMenuItem(
                isInternalUser ? "Pets" : "Browse Pets", "Pets", Symbol.Contact2));
        }
        AddSection(isInternalUser ? "MANAGEMENT" : "ADOPTION", management);

        // ----- OPERATIONS (or MY ACTIVITY for adopter-only users) -----
        var operations = new List<NavigationViewItem>();
        if (isAdminOrStaff || isAdopter || isVeterinarian)
        {
            operations.Add(CreateMenuItem("Medical Records", "MedicalRecords", Symbol.Document));
        }
        if (isAdminOrStaff || isAdopter)
        {
            operations.Add(CreateMenuItem(
                isAdminOrStaff ? "Payments" : "My Payments", "Payments", Symbol.Shop));
        }
        if (isAdminOrStaff || isAdopter)
        {
            operations.Add(CreateMenuItem(
                isAdminOrStaff ? "Applications" : "My Applications", "Applications", Symbol.Document));
        }
        if (isAdminOrStaff || isAdopter)
        {
            operations.Add(CreateMenuItem("Customer Service", "CustomerService", Symbol.Message));
        }
        if (isAdminOrStaff)
        {
            operations.Add(CreateMenuItem("Customer Retention", "CustomerRetention", Symbol.Contact));
        }
        AddSection(isInternalUser ? "OPERATIONS" : "MY ACTIVITY", operations);

        // ----- REPORTING -----
        var reporting = new List<NavigationViewItem>();
        if (roles.Contains("Administrator"))
        {
            reporting.Add(CreateMenuItem("Reports", "BusinessIntelligence", Symbol.ReportHacked));
        }
        AddSection("REPORTING", reporting);

        // ----- SYSTEM: Notifications, everyone signed in gets their own -----
        var notificationsItem = CreateMenuItem("Notifications", "Notifications", Symbol.Emoji2);
        _notificationsMenuItem = notificationsItem;
        AddSection("SYSTEM", new List<NavigationViewItem> { notificationsItem });
    }

    // Adds a small section label followed by its items. Empty sections are skipped.
    private void AddSection(string header, List<NavigationViewItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        NavView.MenuItems.Add(new NavigationViewItemHeader { Content = header });

        foreach (var item in items)
        {
            NavView.MenuItems.Add(item);
        }
    }

    private static NavigationViewItem CreateMenuItem(string content, string tag, Symbol icon)
    {
        return new NavigationViewItem
        {
            Content = content,
            Tag = tag,
            Icon = new SymbolIcon(icon),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(8, 1, 8, 1)
        };
    }

    // ------------------------------------------------------------
    // Top bar: API status pill, unread count (sidebar badge + bell)
    // ------------------------------------------------------------

    // One request does both jobs. Any answer from the API (even "not signed in") proves the API is
    // reachable; no answer in 8 seconds, or a failed connection, means it is offline.
    private async Task RefreshUnreadBadgeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var response = await ApiClient.Http.GetAsync("api/notifications/unread-count", cts.Token);

            SetApiStatus(true);

            if (response.IsSuccessStatusCode && !AuthSession.IsSuperAdmin && _notificationsMenuItem is not null)
            {
                var result = await response.Content.ReadFromJsonAsync<UnreadCount>();
                SetUnreadCount(result?.Count ?? 0);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
        {
            SetApiStatus(false);
        }
        catch
        {
            // Best-effort only; a failed refresh should never block navigation.
        }
    }

    private void SetApiStatus(bool connected)
    {
        ApiPill.Background = connected ? Res("PawsSuccessSoftBrush") : Res("PawsErrorSoftBrush");
        ApiDot.Fill = connected ? Res("PawsSuccessBrush") : Res("PawsErrorBrush");
        ApiPillText.Foreground = connected ? Res("PawsSuccessBrush") : Res("PawsErrorBrush");
        ApiPillText.Text = connected ? "API connected" : "API offline";
    }

    private void SetUnreadCount(int count)
    {
        // Bell
        BellCountBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BellCountText.Text = count > 99 ? "99+" : count.ToString();
        ToolTipService.SetToolTip(BellButton, count > 0 ? $"{count} unread notifications" : "Notifications");

        // Sidebar badge
        if (_notificationsMenuItem is null)
        {
            return;
        }

        if (count > 0)
        {
            _notificationsMenuItem.InfoBadge = new InfoBadge
            {
                Value = count,
                Style = (Style)Application.Current.Resources["AttentionValueInfoBadgeStyle"]
            };
        }
        else
        {
            _notificationsMenuItem.InfoBadge = null;
        }
    }

    private void Bell_Click(object sender, RoutedEventArgs e) => GoToTag("Notifications");

    // Clicking "PAWS" in the breadcrumb goes home: the Dashboard if this role has one,
    // otherwise the first page in the sidebar (Browse Pets for Adopters, System Administration for Super Admin).
    private void Home_Click(object sender, RoutedEventArgs e)
    {
        var home = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(i => (i.Tag as string) == "Dashboard")
            ?? NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();

        if (home is not null)
        {
            NavView.SelectedItem = home;
        }
    }

    // NEW: lets other pages (the Dashboard quick actions) switch the sidebar and open a page.
    public static void GoTo(string tag) => Current?.GoToTag(tag);

    // Selects the sidebar item with this tag, which navigates to its page.
    private void GoToTag(string tag)
    {
        var item = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(i => (i.Tag as string) == tag);

        if (item is not null)
        {
            NavView.SelectedItem = item;
        }
    }

    // ------------------------------------------------------------
    // Top bar: page search (Ctrl+K)
    // ------------------------------------------------------------
    private void SearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        PageSearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void UpdateSuggestions()
    {
        var text = PageSearchBox.Text?.Trim() ?? string.Empty;

        PageSearchBox.ItemsSource = _pageEntries
            .Where(p => text.Length == 0 ||
                        p.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    private void PageSearch_GotFocus(object sender, RoutedEventArgs e)
    {
        UpdateSuggestions();
        PageSearchBox.IsSuggestionListOpen = true;
    }

    private void PageSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        UpdateSuggestions();
    }

    private void PageSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText?.Trim() ?? string.Empty;

        // A clicked suggestion wins; otherwise the first page whose name contains the typed text.
        PageEntry? entry = args.ChosenSuggestion as PageEntry;

        if (entry is null && query.Length > 0)
        {
            entry = _pageEntries.FirstOrDefault(p =>
                p.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        if (entry is null)
        {
            return;
        }

        GoToTag(entry.Tag);

        sender.Text = string.Empty;
        sender.IsSuggestionListOpen = false;
    }

    // ------------------------------------------------------------
    // Navigation (unchanged, plus the breadcrumb line)
    // ------------------------------------------------------------
    private void NavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            CurrentPageText.Text = args.SelectedItemContainer.Content as string ?? string.Empty;

            NavigateTo(tag);
            _ = RefreshUnreadBadgeAsync();
        }
    }

    private void NavigateTo(string tag)
    {
        switch (tag)
        {
            case "Dashboard":
                ContentFrame.Navigate(typeof(DashboardPage));
                break;

            case "Users":
                ContentFrame.Navigate(typeof(UsersPage));
                break;

            case "PetCategories":
                ContentFrame.Navigate(typeof(PetCategoriesPage));
                break;

            case "Branches":
                ContentFrame.Navigate(typeof(BranchesPage));
                break;

            case "Pets":
                ContentFrame.Navigate(typeof(PetsPage));
                break;

            case "MedicalRecords":
                ContentFrame.Navigate(typeof(MedicalRecordsPage));
                break;

            case "Payments":
                ContentFrame.Navigate(typeof(PaymentsPage));
                break;

            case "Applications":
                ContentFrame.Navigate(typeof(AdoptionApplicationsPage));
                break;

            case "CustomerService":
                ContentFrame.Navigate(typeof(CustomerServicePage));
                break;

            case "CustomerRetention":
                ContentFrame.Navigate(typeof(CustomerRetentionPage));
                break;

            case "BusinessIntelligence":
                ContentFrame.Navigate(typeof(BusinessIntelligencePage));
                break;

            case "Notifications":
                ContentFrame.Navigate(typeof(NotificationsPage));
                break;

            case "SystemAdministration":
                ContentFrame.Navigate(typeof(SystemAdministrationPage));
                break;

            case "SubscriptionPlans":
                ContentFrame.Navigate(typeof(SubscriptionPlansPage));
                break;
        }
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        AuthSession.Clear();
        RootNavigation.GoToLogin();
    }
}