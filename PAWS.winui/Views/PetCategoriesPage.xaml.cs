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

public sealed partial class PetCategoriesPage : Page
{
    // ----- Animation tuning -----
    private const int MaxAnimatedRows = 12;
    private const double RowStaggerMs = 45;
    private const double CountUpMs = 700;

    // Looks up a PAWS brush from PawsTheme.xaml
    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public class CategoryRow : INotifyPropertyChanged
    {
        public PetCategoryItem Source { get; }
        public CategoryRow(PetCategoryItem source) => Source = source;

        public int PetCategoryId => Source.PetCategoryId;
        public string CategoryName => Source.CategoryName;

        // ----- Status badge -----
        public string StatusLabel => Source.IsActive ? "ACTIVE" : "INACTIVE";
        public Brush StatusBadgeBackground => Source.IsActive ? Res("PawsSuccessSoftBrush") : Res("PawsErrorSoftBrush");
        public Brush StatusBadgeForeground => Source.IsActive ? Res("PawsSuccessBrush") : Res("PawsErrorBrush");

        public Visibility DeactivateVisibility => Source.IsActive ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ActivateVisibility => !Source.IsActive ? Visibility.Visible : Visibility.Collapsed;

        // ----- Selection highlight: the clicked category shows in soft coral -----
        private bool _isSelected;

        public void SetSelected(bool selected)
        {
            if (_isSelected == selected) return;
            _isSelected = selected;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowBackground)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameBrush)));
        }

        public Brush RowBackground => _isSelected
            ? Res("PawsPrimarySoftBrush")
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        public Brush NameBrush => _isSelected ? Res("PawsPrimaryBrush") : Res("PawsTextPrimaryBrush");

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class BreedRow : INotifyPropertyChanged
    {
        public BreedItem Source { get; }
        public BreedRow(BreedItem source) => Source = source;

        public string BreedName => Source.BreedName;

        public string StatusLabel => Source.IsActive ? "ACTIVE" : "INACTIVE";
        public Brush StatusBadgeBackground => Source.IsActive ? Res("PawsSuccessSoftBrush") : Res("PawsErrorSoftBrush");
        public Brush StatusBadgeForeground => Source.IsActive ? Res("PawsSuccessBrush") : Res("PawsErrorBrush");

        public Visibility DeactivateVisibility => Source.IsActive ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ActivateVisibility => !Source.IsActive ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<CategoryRow> _categoryRows = new();
    private readonly ObservableCollection<BreedRow> _breedRows = new();
    private List<PetCategoryItem> _allCategories = new();

    private int? _selectedCategoryId;
    private int? _editingCategoryId;
    private int? _editingBreedId;
    private bool _categoriesLoaded;

    // ----- Animation state -----
    private bool _animate = true;
    private bool _animateCategoryRows;           // true only right after the categories are (re)loaded
    private bool _animateBreedRows;              // true only right after a category was clicked
    private bool _overlayWasOpen;

    private readonly DispatcherTimer _categoryWindowTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly DispatcherTimer _breedWindowTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    private sealed class Counter
    {
        public TextBlock Target = null!;
        public int Value;
        public Func<int, string> Label = null!;
        public DateTime Start;
    }

    private readonly List<Counter> _counters = new();
    private readonly DispatcherTimer _countTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    public PetCategoriesPage()
    {
        InitializeComponent();
        CategoriesList.ItemsSource = _categoryRows;
        BreedsList.ItemsSource = _breedRows;

        // The popup background must follow whichever form panel is showing.
        WatchPanel(CategoryFormPanel);
        WatchPanel(BreedFormPanel);

        _countTimer.Tick += CountTimer_Tick;

        // After the rows have been created, stop animating them (so later updates are instant).
        _categoryWindowTimer.Tick += (s, e) =>
        {
            _categoryWindowTimer.Stop();
            _animateCategoryRows = false;
        };
        _breedWindowTimer.Tick += (s, e) =>
        {
            _breedWindowTimer.Stop();
            _animateBreedRows = false;
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _animate = ReadAnimationSetting();

        if (_animate)
        {
            // The header fades in, then the two cards rise into place (they do not depend on data).
            PrepareFade(HeaderPanel);
            PlayFade(HeaderPanel, 0, 420);
            PlaySlideFadeIn(CategoriesCard, 16, 110, 450);
            PlaySlideFadeIn(BreedsCard, 16, 220, 450);
        }

        await LoadCategoriesAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Never leave a timer running after the page is gone.
        _countTimer.Stop();
        _counters.Clear();
        _categoryWindowTimer.Stop();
        _breedWindowTimer.Stop();
        _animateCategoryRows = false;
        _animateBreedRows = false;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadCategoriesAsync();
        if (_selectedCategoryId is not null)
        {
            await LoadBreedsAsync();
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

    private void CategoryRowItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_animateCategoryRows || sender is not Border row || row.DataContext is not CategoryRow data)
        {
            return;
        }

        int index = _categoryRows.IndexOf(data);
        if (index < 0 || index >= MaxAnimatedRows)
        {
            return;
        }

        PlaySlideFadeIn(row, 14, index * RowStaggerMs, 380);
    }

    private void BreedRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_animateBreedRows || sender is not Border row || row.DataContext is not BreedRow data)
        {
            return;
        }

        int index = _breedRows.IndexOf(data);
        if (index < 0 || index >= MaxAnimatedRows)
        {
            return;
        }

        PlaySlideFadeIn(row, 14, index * RowStaggerMs, 380);
    }

    // ----- Count-up for "N categories" and "N breeds" -----

    private static string CategoryCountLabel(int n) => n == 1 ? "1 category" : $"{n} categories";
    private static string BreedCountLabel(int n) => n == 1 ? "1 breed" : $"{n} breeds";

    private void ShowCount(TextBlock target, int value, Func<int, string> label, bool animate)
    {
        _counters.RemoveAll(c => c.Target == target);

        if (!animate || !_animate || value == 0)
        {
            target.Text = label(value);
            return;
        }

        target.Text = label(0);

        _counters.Add(new Counter
        {
            Target = target,
            Value = value,
            Label = label,
            Start = DateTime.UtcNow
        });

        if (!_countTimer.IsEnabled)
        {
            _countTimer.Start();
        }
    }

    private void CountTimer_Tick(object? sender, object e)
    {
        var now = DateTime.UtcNow;

        for (int i = _counters.Count - 1; i >= 0; i--)
        {
            var c = _counters[i];
            double t = Math.Min(1.0, (now - c.Start).TotalMilliseconds / CountUpMs);

            // Ease-out: fast at first, slowing down at the end.
            double eased = 1.0 - Math.Pow(1.0 - t, 3);

            // The last tick always writes the exact value.
            int shown = t >= 1.0 ? c.Value : (int)Math.Round(c.Value * eased);
            c.Target.Text = c.Label(shown);

            if (t >= 1.0)
            {
                _counters.RemoveAt(i);
            }
        }

        if (_counters.Count == 0)
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
        bool anyOpen =
            CategoryFormPanel.Visibility == Visibility.Visible ||
            BreedFormPanel.Visibility == Visibility.Visible;

        FormOverlay.Visibility = anyOpen ? Visibility.Visible : Visibility.Collapsed;

        // Start every popup with a clean slate.
        PopupErrorBar.IsOpen = false;

        // Animate only when the popup goes from closed to open.
        if (anyOpen && !_overlayWasOpen && _animate)
        {
            PrepareFade(FormOverlay);
            PlayFade(FormOverlay, 0, 180);

            Border openPanel = CategoryFormPanel.Visibility == Visibility.Visible
                ? CategoryFormPanel
                : BreedFormPanel;

            PlaySlideFadeIn(openPanel, 18, 40, 260);
        }

        _overlayWasOpen = anyOpen;
    }

    // ----- Breed row hover highlight -----

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

    // ----- Categories -----

    private void ApplySelection()
    {
        foreach (var row in _categoryRows)
        {
            row.SetSelected(_selectedCategoryId == row.PetCategoryId);
        }
    }

    // 'animate' is true when the page opens or Refresh is pressed; false after a save or activate.
    private async Task LoadCategoriesAsync(bool animate = true)
    {
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;

        _animate = ReadAnimationSetting();

        try
        {
            using var response = await ApiClient.Http.GetAsync("api/pet-categories?includeInactive=true");

            if (response.IsSuccessStatusCode)
            {
                _allCategories = await response.Content.ReadFromJsonAsync<List<PetCategoryItem>>() ?? new();

                // Rows animate only right after a load; give them a short window to be created.
                if (animate && _animate)
                {
                    _animateCategoryRows = true;
                    _categoryWindowTimer.Stop();
                    _categoryWindowTimer.Start();
                }

                _categoryRows.Clear();
                foreach (var c in _allCategories.OrderBy(c => c.CategoryName))
                {
                    _categoryRows.Add(new CategoryRow(c));
                }

                ApplySelection();
                _categoriesLoaded = true;

                ShowCount(CategoryCountText, _categoryRows.Count, CategoryCountLabel, animate);
                CategoriesEmpty.Visibility = _categoryRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // Keep the breed form's category dropdown in sync.
                BreedCategoryBox.ItemsSource = _allCategories.Where(c => c.IsActive).ToList();
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthSession.Clear();
                RootNavigation.GoToLogin();
                return;
            }
            else
            {
                ShowError($"Could not load categories (HTTP {(int)response.StatusCode}).");
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

    private async void CategoryRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CategoryRow row)
        {
            return;
        }

        _selectedCategoryId = row.PetCategoryId;
        BreedsTitle.Text = $"Breeds \u2014 {row.CategoryName}";
        ApplySelection();
        await LoadBreedsAsync();
    }

    // ----- Breeds -----

    // 'animate' is true when a category was clicked (or Refresh); false after a save or activate.
    private async Task LoadBreedsAsync(bool animate = true)
    {
        if (_selectedCategoryId is null)
        {
            return;
        }

        // Remember which category this request is for. If the person clicks another
        // category before the answer arrives, the late answer must be ignored.
        var requestedCategoryId = _selectedCategoryId;

        // Clear the old list right away so the previous category's breeds
        // do not stay on screen while the new ones load.
        _breedRows.Clear();
        _counters.RemoveAll(c => c.Target == BreedCountText);
        BreedCountText.Text = " ";
        BreedsPrompt.Visibility = Visibility.Collapsed;
        BreedsEmpty.Visibility = Visibility.Collapsed;
        BreedsLoading.Visibility = Visibility.Visible;

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;

        _animate = ReadAnimationSetting();

        try
        {
            using var response = await ApiClient.Http.GetAsync(
                $"api/breeds?categoryId={requestedCategoryId}&includeInactive=true");

            if (_selectedCategoryId != requestedCategoryId)
            {
                return;
            }

            if (response.IsSuccessStatusCode)
            {
                var breeds = await response.Content.ReadFromJsonAsync<List<BreedItem>>() ?? new();

                if (_selectedCategoryId != requestedCategoryId)
                {
                    return;
                }

                // Rows animate only right after a load; give them a short window to be created.
                if (animate && _animate)
                {
                    _animateBreedRows = true;
                    _breedWindowTimer.Stop();
                    _breedWindowTimer.Start();
                }

                _breedRows.Clear();
                foreach (var b in breeds.OrderBy(b => b.BreedName))
                {
                    _breedRows.Add(new BreedRow(b));
                }

                ShowCount(BreedCountText, _breedRows.Count, BreedCountLabel, animate);
                BreedsEmpty.Visibility = _breedRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthSession.Clear();
                RootNavigation.GoToLogin();
            }
            else
            {
                ShowError($"Could not load breeds (HTTP {(int)response.StatusCode}).");
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            ShowError("Could not reach the API.");
        }
        finally
        {
            if (_selectedCategoryId == requestedCategoryId)
            {
                BreedsLoading.Visibility = Visibility.Collapsed;
            }
            Busy.IsActive = false;
        }
    }

    // ----- Category form -----

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        _editingCategoryId = null;
        CategoryFormTitle.Text = "Add Category";
        CategoryNameBox.Text = string.Empty;
        CategoryDescriptionBox.Text = string.Empty;
        CategoryFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelCategoryForm_Click(object sender, RoutedEventArgs e)
    {
        CategoryFormPanel.Visibility = Visibility.Collapsed;
    }

    private async void SaveCategory_Click(object sender, RoutedEventArgs e)
    {
        var name = CategoryNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Category name is required.");
            return;
        }

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            HttpResponseMessage response;
            var description = string.IsNullOrWhiteSpace(CategoryDescriptionBox.Text) ? null : CategoryDescriptionBox.Text.Trim();

            if (_editingCategoryId is int id)
            {
                response = await ApiClient.Http.PutAsJsonAsync($"api/pet-categories/{id}",
                    new UpdatePetCategoryRequest { CategoryName = name, Description = description });
            }
            else
            {
                response = await ApiClient.Http.PostAsJsonAsync("api/pet-categories",
                    new CreatePetCategoryRequest { CategoryName = name, Description = description });
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    ShowSuccess("Category saved.");
                    CategoryFormPanel.Visibility = Visibility.Collapsed;
                    await LoadCategoriesAsync(animate: false);
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

    private async void DeactivateCategory_Click(object sender, RoutedEventArgs e) =>
        await SetCategoryActive(sender, false);

    private async void ActivateCategory_Click(object sender, RoutedEventArgs e) =>
        await SetCategoryActive(sender, true);

    private async Task SetCategoryActive(object sender, bool isActive)
    {
        if ((sender as Button)?.Tag is not CategoryRow row)
        {
            return;
        }

        Busy.IsActive = true;
        try
        {
            var action = isActive ? "activate" : "deactivate";
            using var response = await ApiClient.Http.PostAsync($"api/pet-categories/{row.PetCategoryId}/{action}", null);
            if (response.IsSuccessStatusCode)
            {
                ShowSuccess($"Category {action}d.");
                await LoadCategoriesAsync(animate: false);
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

    // ----- Breed form -----

    private void AddBreed_Click(object sender, RoutedEventArgs e)
    {
        _editingBreedId = null;
        BreedFormTitle.Text = "Add Breed";
        BreedNameBox.Text = string.Empty;
        BreedDescriptionBox.Text = string.Empty;

        if (_selectedCategoryId.HasValue)
        {
            BreedCategoryBox.SelectedItem = (BreedCategoryBox.ItemsSource as List<PetCategoryItem>)?
                .FirstOrDefault(c => c.PetCategoryId == _selectedCategoryId.Value);
        }
        else
        {
            BreedCategoryBox.SelectedIndex = -1;
        }

        BreedFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelBreedForm_Click(object sender, RoutedEventArgs e)
    {
        BreedFormPanel.Visibility = Visibility.Collapsed;
    }

    private async void SaveBreed_Click(object sender, RoutedEventArgs e)
    {
        var name = BreedNameBox.Text.Trim();
        var category = BreedCategoryBox.SelectedItem as PetCategoryItem;

        if (string.IsNullOrWhiteSpace(name) || category is null)
        {
            ShowError("Please choose a category and enter a breed name.");
            return;
        }

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            var description = string.IsNullOrWhiteSpace(BreedDescriptionBox.Text) ? null : BreedDescriptionBox.Text.Trim();
            HttpResponseMessage response;

            if (_editingBreedId is int id)
            {
                response = await ApiClient.Http.PutAsJsonAsync($"api/breeds/{id}",
                    new UpdateBreedRequest { PetCategoryId = category.PetCategoryId, BreedName = name, Description = description });
            }
            else
            {
                response = await ApiClient.Http.PostAsJsonAsync("api/breeds",
                    new CreateBreedRequest { PetCategoryId = category.PetCategoryId, BreedName = name, Description = description });
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    ShowSuccess("Breed saved.");
                    BreedFormPanel.Visibility = Visibility.Collapsed;
                    _selectedCategoryId = category.PetCategoryId;
                    BreedsTitle.Text = $"Breeds \u2014 {category.CategoryName}";
                    ApplySelection();
                    await LoadBreedsAsync(animate: false);
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

    private async void DeactivateBreed_Click(object sender, RoutedEventArgs e) =>
        await SetBreedActive(sender, false);

    private async void ActivateBreed_Click(object sender, RoutedEventArgs e) =>
        await SetBreedActive(sender, true);

    private async Task SetBreedActive(object sender, bool isActive)
    {
        if ((sender as Button)?.Tag is not BreedRow row)
        {
            return;
        }

        Busy.IsActive = true;
        try
        {
            var action = isActive ? "activate" : "deactivate";
            using var response = await ApiClient.Http.PostAsync($"api/breeds/{row.Source.BreedId}/{action}", null);
            if (response.IsSuccessStatusCode)
            {
                ShowSuccess($"Breed {action}d.");
                await LoadBreedsAsync(animate: false);
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