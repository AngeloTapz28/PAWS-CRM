using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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

public sealed partial class PetsPage : Page
{
    // ----- Animation tuning -----
    private const int MaxAnimatedCards = 12;     // only the first cards animate; the rest are off-screen
    private const double CardStaggerMs = 55;
    private const double CountUpMs = 700;

    public class PetRow : INotifyPropertyChanged
    {
        public PetItem Source { get; }
        public PetRow(PetItem source) => Source = source;

        private static bool IsAdminOrStaff =>
            AuthSession.Roles.Contains("Administrator") || AuthSession.Roles.Contains("Staff");
        private static bool IsVet => AuthSession.Roles.Contains("Veterinarian");
        private static bool IsAdopterRow => AuthSession.Roles.Contains("Adopter");

        // Looks up a PAWS brush from PawsTheme.xaml
        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        // ----- Text shown on the card -----
        public string NameText => Source.PetName;
        public string BreedLine => $"{Source.BreedName} \u00b7 {Source.CategoryName}";
        public string MetaText =>
            $"{Source.Gender ?? "Unknown gender"} \u2022 " +
            $"{(Source.Age.HasValue ? $"{Source.Age} yr" : "Age unknown")} \u2022 " +
            $"{Source.Size ?? "Size unknown"}";
        public string BranchText => Source.BranchName;

        public string FeeText
        {
            get
            {
                if (!Source.AdoptionFee.HasValue)
                {
                    return "Fee not set";
                }

                var fee = Source.AdoptionFee.Value;
                return fee > 0
                    ? "\u20b1" + fee.ToString("N2", CultureInfo.InvariantCulture)
                    : "Free";
            }
        }

        public string VetNotesText => $"Vet note: {Source.VetNotes}";
        public Visibility VetNotesVisibility =>
            !string.IsNullOrWhiteSpace(Source.VetNotes) ? Visibility.Visible : Visibility.Collapsed;

        // ----- Pet photo (only valid http/https URLs; otherwise the card shows the paw placeholder) -----
        private BitmapImage? _image;
        private bool _imageResolved;
        public ImageSource? PetImage
        {
            get
            {
                if (!_imageResolved)
                {
                    _imageResolved = true;
                    if (Uri.TryCreate(Source.ImagePath, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        // Decode at a small size: keeps memory low when there are many cards.
                        _image = new BitmapImage { DecodePixelWidth = 360, UriSource = uri };
                    }
                }
                return _image;
            }
        }

        // ----- Status badge (colors follow the PAWS design system) -----
        public string StatusLabel => (Source.Status ?? string.Empty).ToUpperInvariant();

        public Brush StatusBadgeBackground => Source.Status switch
        {
            "Available" => Res("PawsSuccessSoftBrush"),
            "Pending" => Res("PawsWarningSoftBrush"),
            "Under Treatment" => Res("PawsWarningSoftBrush"),
            "Adopted" => Res("PawsInfoSoftBrush"),
            _ => Res("PawsHoverSurfaceBrush")
        };

        public Brush StatusBadgeForeground => Source.Status switch
        {
            "Available" => Res("PawsSuccessBrush"),
            "Pending" => Res("PawsWarningBrush"),
            "Under Treatment" => Res("PawsWarningBrush"),
            "Adopted" => Res("PawsInfoBrush"),
            _ => Res("PawsTextSecondaryBrush")
        };

        // ----- Approval badge (Administrator / Staff / Veterinarian only, as before) -----
        public Visibility ApprovalVisibility =>
            IsAdminOrStaff || IsVet ? Visibility.Visible : Visibility.Collapsed;

        public string ApprovalText =>
            Source.IsAdminApproved ? "\u2713 APPROVED"
            : Source.IsVetApproved ? "VET APPROVED"
            : "AWAITING VET";

        public Brush ApprovalBadgeBackground =>
            Source.IsAdminApproved ? Res("PawsSuccessSoftBrush")
            : Source.IsVetApproved ? Res("PawsInfoSoftBrush")
            : Res("PawsWarningSoftBrush");

        public Brush ApprovalBadgeForeground =>
            Source.IsAdminApproved ? Res("PawsSuccessBrush")
            : Source.IsVetApproved ? Res("PawsInfoBrush")
            : Res("PawsWarningBrush");

        // ----- Button visibility: SAME rules as the old page -----
        public Visibility ApplyVisibility =>
            IsAdopterRow && Source.Status == "Available" ? Visibility.Visible : Visibility.Collapsed;

        public Visibility EditVisibility => IsAdminOrStaff ? Visibility.Visible : Visibility.Collapsed;

        public Visibility VetApproveVisibility =>
            IsVet && !Source.IsVetApproved && Source.Status != "Adopted" && Source.Status != "Unavailable"
                ? Visibility.Visible : Visibility.Collapsed;

        public Visibility VetRejectVisibility =>
            IsVet && Source.Status == "Pending" ? Visibility.Visible : Visibility.Collapsed;

        public Visibility AdminApproveVisibility =>
            IsAdminOrStaff && Source.IsVetApproved && !Source.IsAdminApproved ? Visibility.Visible : Visibility.Collapsed;

        public Visibility TreatmentVisibility =>
            (IsAdminOrStaff || IsVet) && Source.Status != "Under Treatment" &&
            Source.Status != "Adopted" && Source.Status != "Unavailable"
                ? Visibility.Visible : Visibility.Collapsed;

        public Visibility UnavailableVisibility =>
            IsAdminOrStaff && Source.Status != "Unavailable" && Source.Status != "Adopted"
                ? Visibility.Visible : Visibility.Collapsed;

        public Visibility DeactivateVisibility => IsAdminOrStaff ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private const string AllStatuses = "All Statuses";

    private readonly ObservableCollection<PetRow> _rows = new();
    private List<PetRow> _allRows = new();          // everything the API returned
    private List<PetCategoryItem> _categories = new();
    private List<BranchItem> _branches = new();
    private int? _editingPetId;
    private PetRow? _rejectingRow;
    private PetRow? _applyingRow;
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

    public PetsPage()
    {
        InitializeComponent();
        PetsList.ItemsSource = _rows;

        // The popup background must follow whichever form panel is showing.
        WatchPanel(FormPanel);
        WatchPanel(ApplyFormPanel);
        WatchPanel(RejectFormPanel);

        _countTimer.Tick += CountTimer_Tick;

        // After the cards have been created, stop animating them (so later filtering is instant).
        _cardsWindowTimer.Tick += (s, e) =>
        {
            _cardsWindowTimer.Stop();
            _animateCards = false;
        };

        PageTitle.Text = IsAdopter ? "Browse Pets" : "Manage Pets";
        AddPetButton.Visibility = IsAdminOrStaff ? Visibility.Visible : Visibility.Collapsed;

        // Cards need different heights depending on how many action buttons the role sees.
        PetsLayout.MinItemHeight = IsAdminOrStaff ? 470
            : AuthSession.Roles.Contains("Veterinarian") ? 430
            : 380;

        StatusFilterBox.ItemsSource = new List<string> { AllStatuses, "Pending", "Available", "Under Treatment", "Adopted", "Unavailable" };
        StatusFilterBox.SelectedIndex = 0;

        if (IsAdopter)
        {
            // Adopters cannot filter by status at all; the API enforces Available-only regardless.
            StatusFilterBox.Visibility = Visibility.Collapsed;
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _animate = ReadAnimationSetting();

        if (_animate)
        {
            // The header and the toolbar fade in one after another (they do not depend on data).
            PrepareFade(HeaderPanel);
            PrepareFade(ToolbarCard);
            PlayFade(HeaderPanel, 0, 420);
            PlayFade(ToolbarCard, 110, 420);
        }

        await LoadCategoriesAsync();
        await LoadBranchesAsync();
        await LoadPetsAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Never leave a timer running after the page is gone.
        _countTimer.Stop();
        _cardsWindowTimer.Stop();
        _animateCards = false;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadPetsAsync();

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilter)
        {
            return;
        }

        await LoadPetsAsync();
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

    // ----- Pet cards: the first few rise and fade in after a load -----

    private void PetsList_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (!_animateCards || args.Element is not FrameworkElement card)
        {
            return;
        }

        if (args.Index < 0 || args.Index >= MaxAnimatedCards)
        {
            return;
        }

        PlaySlideFadeIn(card, 18, args.Index * CardStaggerMs, 420);
    }

    // ----- Card hover: lift 4 px and turn the border coral -----

    private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            AnimateLift(card, -4f);
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

    // ----- Count-up for "Showing N pets" -----

    private static string CountLabel(int n) => n == 1 ? "Showing 1 pet" : $"Showing {n} pets";

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

    // The popup background (FormOverlay) must be visible whenever one of the three
    // form panels is visible. Instead of editing every place that shows or hides a
    // panel, we listen for any change to a panel's Visibility and update the overlay.
    private void WatchPanel(Border panel)
    {
        panel.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (s, dp) => UpdateFormOverlay());
    }

    private void UpdateFormOverlay()
    {
        bool anyOpen =
            FormPanel.Visibility == Visibility.Visible ||
            ApplyFormPanel.Visibility == Visibility.Visible ||
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
                FormPanel.Visibility == Visibility.Visible ? FormPanel
                : ApplyFormPanel.Visibility == Visibility.Visible ? ApplyFormPanel
                : RejectFormPanel;

            PlaySlideFadeIn(openPanel, 18, 40, 260);
        }

        _overlayWasOpen = anyOpen;
    }

    // ----- Search (client-side, over the pets already loaded) -----

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();

    private static bool ContainsText(string? value, string term) =>
        value?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true;

    // 'afterLoad' is true only when the list has just been loaded from the API,
    // which is the only time the count and the cards animate.
    private void ApplySearch(bool afterLoad = false)
    {
        string term = SearchBox.Text.Trim();

        IEnumerable<PetRow> matches = _allRows;
        if (term.Length > 0)
        {
            matches = _allRows.Where(r =>
                ContainsText(r.Source.PetName, term) ||
                ContainsText(r.Source.BreedName, term) ||
                ContainsText(r.Source.CategoryName, term) ||
                ContainsText(r.Source.Color, term));
        }

        var list = matches.ToList();

        // Cards animate only right after a load; give them a short window to be created.
        if (afterLoad && _animate)
        {
            _animateCards = true;
            _cardsWindowTimer.Stop();
            _cardsWindowTimer.Start();
        }

        _rows.Clear();
        foreach (var row in list)
        {
            _rows.Add(row);
        }

        if (afterLoad && _animate && list.Count > 0)
        {
            StartCountUp(list.Count);
        }
        else
        {
            _countTimer.Stop();
            ResultCountText.Text = CountLabel(list.Count);
        }

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = _hasLoaded && _rows.Count == 0 && !Busy.IsActive
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        // Changing three boxes would otherwise trigger three separate reloads.
        _suppressFilter = true;
        try
        {
            SearchBox.Text = string.Empty;
            if (CategoryFilterBox.Items.Count > 0) CategoryFilterBox.SelectedIndex = 0;
            if (StatusFilterBox.Items.Count > 0) StatusFilterBox.SelectedIndex = 0;
            if (BranchFilterBox.Items.Count > 0) BranchFilterBox.SelectedIndex = 0;
        }
        finally
        {
            _suppressFilter = false;
        }

        await LoadPetsAsync();
    }

    // ----- Loading -----

    private async Task LoadCategoriesAsync()
    {
        try
        {
            using var response = await ApiClient.Http.GetAsync("api/pet-categories");
            if (response.IsSuccessStatusCode)
            {
                _categories = await response.Content.ReadFromJsonAsync<List<PetCategoryItem>>() ?? new();
                var withAny = new List<PetCategoryItem> { new PetCategoryItem { PetCategoryId = 0, CategoryName = "All Categories" } };
                withAny.AddRange(_categories.OrderBy(c => c.CategoryName));
                CategoryFilterBox.ItemsSource = withAny;
                CategoryFilterBox.SelectedIndex = 0;

                FormCategoryBox.ItemsSource = _categories.OrderBy(c => c.CategoryName).ToList();
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            ShowError("Could not load categories.");
        }
    }

    private async Task LoadBranchesAsync()
    {
        try
        {
            using var response = await ApiClient.Http.GetAsync("api/branches");
            if (response.IsSuccessStatusCode)
            {
                _branches = await response.Content.ReadFromJsonAsync<List<BranchItem>>() ?? new();

                var withAny = new List<BranchItem> { new BranchItem { BranchId = 0, BranchName = "All Branches" } };
                withAny.AddRange(_branches.Where(b => b.IsActive).OrderBy(b => b.BranchName));
                BranchFilterBox.ItemsSource = withAny;
                BranchFilterBox.SelectedIndex = 0;

                FormBranchBox.ItemsSource = _branches.Where(b => b.IsActive).OrderBy(b => b.BranchName).ToList();
            }
            else
            {
                ShowError("Could not load branches.");
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            ShowError("Could not reach the API to load branches.");
        }
    }

    private async Task LoadPetsAsync()
    {
        Busy.IsActive = true;
        ListHost.Opacity = 0.5;
        ErrorBar.IsOpen = false;

        _animate = ReadAnimationSetting();

        try
        {
            var query = new List<string>();

            if (!IsAdopter && StatusFilterBox.SelectedItem is string status && status != AllStatuses)
            {
                query.Add($"status={Uri.EscapeDataString(status)}");
            }

            if (CategoryFilterBox.SelectedItem is PetCategoryItem cat && cat.PetCategoryId > 0)
            {
                query.Add($"categoryId={cat.PetCategoryId}");
            }

            if (BranchFilterBox.SelectedItem is BranchItem branchFilter && branchFilter.BranchId > 0)
            {
                query.Add($"branchId={branchFilter.BranchId}");
            }

            var url = "api/pets" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

            using var response = await ApiClient.Http.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var pets = await response.Content.ReadFromJsonAsync<List<PetItem>>() ?? new();
                _allRows = pets.Select(p => new PetRow(p)).ToList();
                _hasLoaded = true;
                ApplySearch(afterLoad: true);
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

    private void AddPet_Click(object sender, RoutedEventArgs e)
    {
        _editingPetId = null;
        FormTitle.Text = "Add Pet";
        FormCategoryBox.SelectedIndex = -1;
        FormBreedBox.ItemsSource = null;
        FormBranchBox.SelectedIndex = _branches.Any(b => b.BranchName == "Main Branch")
            ? FormBranchBox.Items.IndexOf(_branches.First(b => b.BranchName == "Main Branch"))
            : -1;
        PetNameBox.Text = string.Empty;
        GenderBox.Text = string.Empty;
        AgeBox.Text = string.Empty;
        SizeBox.Text = string.Empty;
        ColorBox.Text = string.Empty;
        DescriptionBox.Text = string.Empty;
        ImagePathBox.Text = string.Empty;
        AdoptionFeeBox.Text = string.Empty;
        AdoptionFeeTypeBox.Text = string.Empty;
        FormPanel.Visibility = Visibility.Visible;
    }

    private async void EditPet_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PetRow row)
        {
            return;
        }

        _editingPetId = row.Source.PetId;
        FormTitle.Text = $"Edit {row.Source.PetName}";
        FormPanel.Visibility = Visibility.Visible;   // show the popup immediately; the breed list fills in a moment later

        var category = _categories.FirstOrDefault(c => c.PetCategoryId == row.Source.PetCategoryId);
        FormCategoryBox.SelectedItem = category;
        await LoadBreedsForFormAsync(row.Source.PetCategoryId);
        FormBreedBox.SelectedItem = (FormBreedBox.ItemsSource as List<BreedItem>)?
            .FirstOrDefault(b => b.BreedId == row.Source.BreedId);

        FormBranchBox.SelectedItem = (FormBranchBox.ItemsSource as List<BranchItem>)?
            .FirstOrDefault(b => b.BranchId == row.Source.BranchId);

        PetNameBox.Text = row.Source.PetName;
        GenderBox.Text = row.Source.Gender ?? string.Empty;
        AgeBox.Text = row.Source.Age?.ToString() ?? string.Empty;
        SizeBox.Text = row.Source.Size ?? string.Empty;
        ColorBox.Text = row.Source.Color ?? string.Empty;
        DescriptionBox.Text = row.Source.Description ?? string.Empty;
        ImagePathBox.Text = row.Source.ImagePath ?? string.Empty;
        AdoptionFeeBox.Text = row.Source.AdoptionFee?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        AdoptionFeeTypeBox.Text = row.Source.AdoptionFeeType ?? string.Empty;
    }

    private async void FormCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FormCategoryBox.SelectedItem is PetCategoryItem cat)
        {
            await LoadBreedsForFormAsync(cat.PetCategoryId);
        }
    }

    private async Task LoadBreedsForFormAsync(int categoryId)
    {
        try
        {
            using var response = await ApiClient.Http.GetAsync($"api/breeds?categoryId={categoryId}");
            if (response.IsSuccessStatusCode)
            {
                var breeds = await response.Content.ReadFromJsonAsync<List<BreedItem>>() ?? new();
                FormBreedBox.ItemsSource = breeds.OrderBy(b => b.BreedName).ToList();
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            ShowError("Could not load breeds for this category.");
        }
    }

    private void CancelForm_Click(object sender, RoutedEventArgs e)
    {
        FormPanel.Visibility = Visibility.Collapsed;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var category = FormCategoryBox.SelectedItem as PetCategoryItem;
        var breed = FormBreedBox.SelectedItem as BreedItem;
        var branch = FormBranchBox.SelectedItem as BranchItem;
        var name = PetNameBox.Text.Trim();

        if (category is null || breed is null || branch is null || string.IsNullOrWhiteSpace(name))
        {
            ShowError("Please choose a category, a breed, a branch, and enter a pet name.");
            return;
        }

        int? age = int.TryParse(AgeBox.Text, out var parsedAge) ? parsedAge : null;
        decimal? fee = decimal.TryParse(AdoptionFeeBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedFee)
            ? parsedFee : null;

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            HttpResponseMessage response;

            if (_editingPetId is int id)
            {
                var updateRequest = new UpdatePetRequest
                {
                    PetCategoryId = category.PetCategoryId,
                    BreedId = breed.BreedId,
                    BranchId = branch.BranchId,
                    PetName = name,
                    Gender = NullIfBlank(GenderBox.Text),
                    Age = age,
                    Size = NullIfBlank(SizeBox.Text),
                    Color = NullIfBlank(ColorBox.Text),
                    Description = NullIfBlank(DescriptionBox.Text),
                    ImagePath = NullIfBlank(ImagePathBox.Text),
                    AdoptionFee = fee,
                    AdoptionFeeType = NullIfBlank(AdoptionFeeTypeBox.Text)
                };
                response = await ApiClient.Http.PutAsJsonAsync($"api/pets/{id}", updateRequest);
            }
            else
            {
                var createRequest = new CreatePetRequest
                {
                    PetCategoryId = category.PetCategoryId,
                    BreedId = breed.BreedId,
                    BranchId = branch.BranchId,
                    PetName = name,
                    Gender = NullIfBlank(GenderBox.Text),
                    Age = age,
                    Size = NullIfBlank(SizeBox.Text),
                    Color = NullIfBlank(ColorBox.Text),
                    Description = NullIfBlank(DescriptionBox.Text),
                    ImagePath = NullIfBlank(ImagePathBox.Text),
                    AdoptionFee = fee,
                    AdoptionFeeType = NullIfBlank(AdoptionFeeTypeBox.Text)
                };
                response = await ApiClient.Http.PostAsJsonAsync("api/pets", createRequest);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    ShowSuccess(await ReadMessageAsync(response));
                    FormPanel.Visibility = Visibility.Collapsed;
                    await LoadPetsAsync();
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

    // ----- Apply (Adopter) -----

    private void ApplyForPet_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PetRow row)
        {
            return;
        }

        _applyingRow = row;
        ApplyFormTitle.Text = $"Apply to Adopt {row.Source.PetName}";
        ApplyFullNameBox.Text = AuthSession.FullName;
        ApplyEmailBox.Text = AuthSession.Email;
        ApplyPhoneBox.Text = string.Empty;
        ApplyAddressBox.Text = string.Empty;
        ApplyOccupationBox.Text = string.Empty;
        ApplyHousingTypeBox.Text = string.Empty;
        ApplyHomeDescriptionBox.Text = string.Empty;
        ApplyHasYardBox.IsChecked = false;
        ApplyHasOtherPetsBox.IsChecked = false;
        ApplyOtherPetsDetailsBox.Text = string.Empty;
        ApplyHasChildrenBox.IsChecked = false;
        ApplyChildrenDetailsBox.Text = string.Empty;
        ApplyWorkingHoursBox.Text = string.Empty;
        ApplyReasonBox.Text = string.Empty;
        ApplyPetExperienceBox.Text = string.Empty;
        ApplyEmergencyNameBox.Text = string.Empty;
        ApplyEmergencyPhoneBox.Text = string.Empty;
        ApplyEmergencyRelationshipBox.Text = string.Empty;
        ApplyDonationAmountBox.Text = string.Empty;

        ApplyFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelApplyForm_Click(object sender, RoutedEventArgs e)
    {
        ApplyFormPanel.Visibility = Visibility.Collapsed;
        _applyingRow = null;
    }

    private async void SubmitApplication_Click(object sender, RoutedEventArgs e)
    {
        if (_applyingRow is null)
        {
            return;
        }

        decimal? donationAmount = null;
        if (!string.IsNullOrWhiteSpace(ApplyDonationAmountBox.Text))
        {
            if (!decimal.TryParse(ApplyDonationAmountBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDonation)
                || parsedDonation <= 0)
            {
                ShowError("Please enter a valid donation amount, or leave it blank.");
                return;
            }
            donationAmount = parsedDonation;
        }

        var request = new SubmitAdoptionApplicationRequest
        {
            PetId = _applyingRow.Source.PetId,
            FullName = NullIfBlank(ApplyFullNameBox.Text),
            Email = NullIfBlank(ApplyEmailBox.Text),
            Phone = NullIfBlank(ApplyPhoneBox.Text),
            Address = NullIfBlank(ApplyAddressBox.Text),
            Occupation = NullIfBlank(ApplyOccupationBox.Text),
            HousingType = NullIfBlank(ApplyHousingTypeBox.Text),
            HomeDescription = NullIfBlank(ApplyHomeDescriptionBox.Text),
            HasYard = ApplyHasYardBox.IsChecked == true,
            HasOtherPets = ApplyHasOtherPetsBox.IsChecked == true,
            OtherPetsDetails = NullIfBlank(ApplyOtherPetsDetailsBox.Text),
            HasChildren = ApplyHasChildrenBox.IsChecked == true,
            ChildrenDetails = NullIfBlank(ApplyChildrenDetailsBox.Text),
            WorkingHours = NullIfBlank(ApplyWorkingHoursBox.Text),
            ReasonForAdopting = NullIfBlank(ApplyReasonBox.Text),
            PetExperience = NullIfBlank(ApplyPetExperienceBox.Text),
            EmergencyContactName = NullIfBlank(ApplyEmergencyNameBox.Text),
            EmergencyContactPhone = NullIfBlank(ApplyEmergencyPhoneBox.Text),
            EmergencyContactRelationship = NullIfBlank(ApplyEmergencyRelationshipBox.Text),
            DonationAmount = donationAmount
        };

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            using var response = await ApiClient.Http.PostAsJsonAsync("api/adoption-applications", request);

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                ApplyFormPanel.Visibility = Visibility.Collapsed;
                _applyingRow = null;
                await LoadPetsAsync();
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

    // ----- Approvals and status -----

    private async void VetApprove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PetRow row) return;
        await PostActionAsync($"api/pets/{row.Source.PetId}/vet-approve", null);
    }

    private void VetReject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PetRow row) return;
        _rejectingRow = row;
        RejectFormTitle.Text = $"Reject {row.Source.PetName}";
        RejectNotesBox.Text = string.Empty;
        RejectFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelRejectForm_Click(object sender, RoutedEventArgs e)
    {
        RejectFormPanel.Visibility = Visibility.Collapsed;
        _rejectingRow = null;
    }

    private async void ConfirmReject_Click(object sender, RoutedEventArgs e)
    {
        if (_rejectingRow is null)
        {
            return;
        }

        var notes = RejectNotesBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(notes))
        {
            ShowError("Please enter a reason for rejection.");
            return;
        }

        Busy.IsActive = true;
        try
        {
            using var response = await ApiClient.Http.PostAsJsonAsync(
                $"api/pets/{_rejectingRow.Source.PetId}/vet-reject", new RejectPetRequest { VetNotes = notes });

            if (response.IsSuccessStatusCode)
            {
                ShowSuccess(await ReadMessageAsync(response));
                RejectFormPanel.Visibility = Visibility.Collapsed;
                await LoadPetsAsync();
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

    private async void AdminApprove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PetRow row) return;
        await PostActionAsync($"api/pets/{row.Source.PetId}/admin-approve", null);
    }

    private async void SetUnderTreatment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PetRow row) return;
        await PostActionAsync($"api/pets/{row.Source.PetId}/set-status", new SetPetStatusRequest { Status = "Under Treatment" });
    }

    private async void SetUnavailable_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PetRow row) return;
        await PostActionAsync($"api/pets/{row.Source.PetId}/set-status", new SetPetStatusRequest { Status = "Unavailable" });
    }

    private async void DeactivatePet_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PetRow row) return;
        await PostActionAsync($"api/pets/{row.Source.PetId}/deactivate", null);
    }

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
                await LoadPetsAsync();
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