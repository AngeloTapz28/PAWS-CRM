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

public sealed partial class MedicalRecordsPage : Page
{
    // ----- Animation tuning -----
    private const int MaxAnimatedRows = 10;
    private const double RowStaggerMs = 50;
    private const double CountUpMs = 700;

    // Looks up a PAWS brush from PawsTheme.xaml
    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public class MedicalRow : INotifyPropertyChanged
    {
        public MedicalRecordItem Source { get; }
        public MedicalRow(MedicalRecordItem source) => Source = source;

        public string DateText => Source.ExaminationDate.ToString("MMM d, yyyy");
        public string VetText => $"Vet: {Source.VetName}";

        // ----- Health badge -----
        public string HealthLabel => (Source.HealthStatus ?? string.Empty).ToUpperInvariant();

        public Brush HealthBadgeBackground => Source.HealthStatus switch
        {
            "Excellent" or "Good" => Res("PawsSuccessSoftBrush"),
            "Fair" => Res("PawsWarningSoftBrush"),
            "Poor" or "Critical" => Res("PawsErrorSoftBrush"),
            _ => Res("PawsHoverSurfaceBrush")
        };

        public Brush HealthBadgeForeground => Source.HealthStatus switch
        {
            "Excellent" or "Good" => Res("PawsSuccessBrush"),
            "Fair" => Res("PawsWarningBrush"),
            "Poor" or "Critical" => Res("PawsErrorBrush"),
            _ => Res("PawsTextSecondaryBrush")
        };

        public Visibility FitVisibility => Source.FitForAdoption ? Visibility.Visible : Visibility.Collapsed;

        // Same details the old page showed: diagnosis, weight, next checkup.
        public string DetailLine
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Source.Diagnosis)) parts.Add($"Diagnosis: {Source.Diagnosis}");
                if (Source.WeightAtExam.HasValue) parts.Add($"Weight: {Source.WeightAtExam} kg");
                if (Source.NextCheckupDate.HasValue) parts.Add($"Next checkup: {Source.NextCheckupDate:MMM d, yyyy}");
                return string.Join("  \u2022  ", parts);
            }
        }

        public Visibility DetailVisibility =>
            !string.IsNullOrWhiteSpace(DetailLine) ? Visibility.Visible : Visibility.Collapsed;

        public string NotesText => $"Notes: {Source.Notes}";
        public Visibility NotesVisibility =>
            !string.IsNullOrWhiteSpace(Source.Notes) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility EditVisibility => IsVeterinarian ? Visibility.Visible : Visibility.Collapsed;

        private static bool IsVeterinarian => AuthSession.Roles.Contains("Veterinarian");

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class VaccineRow : INotifyPropertyChanged
    {
        public VaccinationRecordItem Source { get; }
        public VaccineRow(VaccinationRecordItem source) => Source = source;

        public string NameText => Source.VaccineName;
        public Visibility BoosterVisibility => Source.IsBooster ? Visibility.Visible : Visibility.Collapsed;

        public string DetailLine =>
            $"Given: {Source.DateAdministered:MMM d, yyyy}  \u2022  By: {Source.AdministeredByName}";

        public string NextDueText => $"Next due: {Source.NextDueDate:MMM d, yyyy}";
        public Visibility NextDueVisibility =>
            Source.NextDueDate.HasValue ? Visibility.Visible : Visibility.Collapsed;

        // ----- Due badge -----
        public string DueLabel => (Source.DueStatus ?? string.Empty).ToUpperInvariant();
        public Visibility DueVisibility =>
            !string.IsNullOrWhiteSpace(Source.DueStatus) ? Visibility.Visible : Visibility.Collapsed;

        public Brush DueBadgeBackground => Source.DueStatus switch
        {
            "Overdue" => Res("PawsErrorSoftBrush"),
            "Upcoming" => Res("PawsSuccessSoftBrush"),
            _ => Res("PawsHoverSurfaceBrush")
        };

        public Brush DueBadgeForeground => Source.DueStatus switch
        {
            "Overdue" => Res("PawsErrorBrush"),
            "Upcoming" => Res("PawsSuccessBrush"),
            _ => Res("PawsTextSecondaryBrush")
        };

        public Visibility EditVisibility => IsVeterinarian ? Visibility.Visible : Visibility.Collapsed;

        private static bool IsVeterinarian => AuthSession.Roles.Contains("Veterinarian");

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<MedicalRow> _medicalRows = new();
    private readonly ObservableCollection<VaccineRow> _vaccineRows = new();
    private List<PetItem> _pets = new();
    private int? _editingMedicalId;
    private int? _editingVaccineId;

    // ----- Animation state -----
    private bool _animate = true;
    private bool _animateMedicalRows;            // true only right after the medical list was (re)loaded
    private bool _animateVaccineRows;            // true only right after the vaccination list was (re)loaded
    private bool _overlayWasOpen;

    private readonly DispatcherTimer _medicalWindowTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly DispatcherTimer _vaccineWindowTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    private sealed class Counter
    {
        public TextBlock Target = null!;
        public int Value;
        public Func<int, string> Label = null!;
        public DateTime Start;
    }

    private readonly List<Counter> _counters = new();
    private readonly DispatcherTimer _countTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    private static bool IsVeterinarian => AuthSession.Roles.Contains("Veterinarian");
    private static bool IsAdopter => AuthSession.Roles.Contains("Adopter");

    public MedicalRecordsPage()
    {
        InitializeComponent();
        MedicalList.ItemsSource = _medicalRows;
        VaccineList.ItemsSource = _vaccineRows;

        // The popup background must follow whichever form panel is showing.
        WatchPanel(MedicalFormPanel);
        WatchPanel(VaccineFormPanel);

        _countTimer.Tick += CountTimer_Tick;

        // After the rows have been created, stop animating them (so later updates are instant).
        _medicalWindowTimer.Tick += (s, e) =>
        {
            _medicalWindowTimer.Stop();
            _animateMedicalRows = false;
        };
        _vaccineWindowTimer.Tick += (s, e) =>
        {
            _vaccineWindowTimer.Stop();
            _animateVaccineRows = false;
        };

        AddMedicalButton.Visibility = IsVeterinarian ? Visibility.Visible : Visibility.Collapsed;
        AddVaccineButton.Visibility = IsVeterinarian ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _animate = ReadAnimationSetting();

        if (_animate)
        {
            // The header fades in, then the pet picker card (they do not depend on data).
            PrepareFade(HeaderPanel);
            PrepareFade(ToolbarCard);
            PlayFade(HeaderPanel, 0, 420);
            PlayFade(ToolbarCard, 110, 420);
        }

        await LoadPetsAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Never leave a timer running after the page is gone.
        _countTimer.Stop();
        _counters.Clear();
        _medicalWindowTimer.Stop();
        _vaccineWindowTimer.Stop();
        _animateMedicalRows = false;
        _animateVaccineRows = false;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadRecordsForSelectedPetAsync();

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

    private void MedicalRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_animateMedicalRows || sender is not Border row || row.DataContext is not MedicalRow data)
        {
            return;
        }

        int index = _medicalRows.IndexOf(data);
        if (index < 0 || index >= MaxAnimatedRows)
        {
            return;
        }

        PlaySlideFadeIn(row, 14, index * RowStaggerMs, 380);
    }

    private void VaccineRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_animateVaccineRows || sender is not Border row || row.DataContext is not VaccineRow data)
        {
            return;
        }

        int index = _vaccineRows.IndexOf(data);
        if (index < 0 || index >= MaxAnimatedRows)
        {
            return;
        }

        PlaySlideFadeIn(row, 14, index * RowStaggerMs, 380);
    }

    // ----- Count-up for "N records" -----

    private static string RecordCountLabel(int n) => n == 1 ? "1 record" : $"{n} records";

    private void ShowCount(TextBlock target, int value, bool animate)
    {
        _counters.RemoveAll(c => c.Target == target);

        if (!animate || !_animate || value == 0)
        {
            target.Text = RecordCountLabel(value);
            return;
        }

        target.Text = RecordCountLabel(0);

        _counters.Add(new Counter
        {
            Target = target,
            Value = value,
            Label = RecordCountLabel,
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
            MedicalFormPanel.Visibility == Visibility.Visible ||
            VaccineFormPanel.Visibility == Visibility.Visible;

        FormOverlay.Visibility = anyOpen ? Visibility.Visible : Visibility.Collapsed;

        // Start every popup with a clean slate.
        PopupErrorBar.IsOpen = false;

        // Animate only when the popup goes from closed to open.
        if (anyOpen && !_overlayWasOpen && _animate)
        {
            PrepareFade(FormOverlay);
            PlayFade(FormOverlay, 0, 180);

            Border openPanel = MedicalFormPanel.Visibility == Visibility.Visible
                ? MedicalFormPanel
                : VaccineFormPanel;

            PlaySlideFadeIn(openPanel, 18, 40, 260);
        }

        _overlayWasOpen = anyOpen;
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

    // ----- Pet list (same three role-specific ways as before) -----

    private async Task LoadPetsAsync()
    {
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;

        try
        {
            // Adopters can only ever browse Available pets via this endpoint,
            // but they need to see THEIR OWN adopted pet here. Since Pets'
            // normal list hides non-Available pets from Adopters, we instead
            // pull the pet list from their own medical/vaccination records,
            // which the API already scopes correctly for them.
            if (IsAdopter)
            {
                using var response = await ApiClient.Http.GetAsync("api/medical-records");
                if (response.IsSuccessStatusCode)
                {
                    var records = await response.Content.ReadFromJsonAsync<List<MedicalRecordItem>>() ?? new();
                    _pets = records
                        .Select(r => new PetItem { PetId = r.PetId, PetName = r.PetName })
                        .GroupBy(p => p.PetId)
                        .Select(g => g.First())
                        .ToList();

                    if (_pets.Count == 0)
                    {
                        using var vaccineResponse = await ApiClient.Http.GetAsync("api/vaccination-records");
                        if (vaccineResponse.IsSuccessStatusCode)
                        {
                            var vaccines = await vaccineResponse.Content.ReadFromJsonAsync<List<VaccinationRecordItem>>() ?? new();
                            _pets = vaccines
                                .Select(v => new PetItem { PetId = v.PetId, PetName = v.PetName })
                                .GroupBy(p => p.PetId)
                                .Select(g => g.First())
                                .ToList();
                        }
                    }
                }
            }
            else
            {
                // Administrator/Staff get everything with no filter needed.
                // A Veterinarian, however, defaults to their Pending review queue
                // when no status is given — so for this page (which needs to see
                // a pet regardless of its current status), we explicitly fetch
                // every status and combine the results.
                if (IsVeterinarian)
                {
                    var allPets = new List<PetItem>();
                    foreach (var petStatus in new[] { "Pending", "Available", "Under Treatment", "Adopted", "Unavailable" })
                    {
                        using var statusResponse = await ApiClient.Http.GetAsync($"api/pets?status={Uri.EscapeDataString(petStatus)}");
                        if (statusResponse.IsSuccessStatusCode)
                        {
                            var petsForStatus = await statusResponse.Content.ReadFromJsonAsync<List<PetItem>>() ?? new();
                            allPets.AddRange(petsForStatus);
                        }
                    }
                    _pets = allPets;
                }
                else
                {
                    using var response = await ApiClient.Http.GetAsync("api/pets");
                    if (response.IsSuccessStatusCode)
                    {
                        _pets = await response.Content.ReadFromJsonAsync<List<PetItem>>() ?? new();
                    }
                }
            }

            PetSelectorBox.ItemsSource = _pets.OrderBy(p => p.PetName).ToList();

            if (_pets.Count == 0)
            {
                PetPromptTitle.Text = "No pets to show";
                PetPromptHint.Text = IsAdopter
                    ? "Medical records appear here once you have adopted a pet."
                    : "There are no pets in the system yet.";

                ShowError(IsAdopter
                    ? "You have no adopted pets with medical records yet."
                    : "No pets found.");
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

    private async void PetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        await LoadRecordsForSelectedPetAsync();

    // ----- Records for the selected pet -----

    // 'animate' is true when a pet is chosen or Refresh is pressed; false after a save.
    private async Task LoadRecordsForSelectedPetAsync(bool animate = true)
    {
        if (PetSelectorBox.SelectedItem is not PetItem pet)
        {
            PetPrompt.Visibility = _pets.Count == 0 || PetContent.Visibility == Visibility.Collapsed
                ? Visibility.Visible
                : Visibility.Collapsed;
            return;
        }

        // Remember which pet this request is for. If the person picks another pet
        // before the answer arrives, the late answer must be ignored.
        var requestedPetId = pet.PetId;

        _animate = ReadAnimationSetting();
        bool run = animate && _animate;

        // Show the selected pet and clear the old lists right away, so the
        // previous pet's records do not stay on screen while the new ones load.
        SelectedPetName.Text = pet.PetName;
        SelectedPetSub.Text = BuildPetSubtitle(pet);
        PetPrompt.Visibility = Visibility.Collapsed;
        PetContent.Visibility = Visibility.Visible;

        // Dim the content while it updates in place; when the cards animate in, no dimming.
        PetContent.Opacity = run ? 1 : 0.5;

        if (run)
        {
            // The pet card and the two record cards rise into place one after another.
            PlaySlideFadeIn(PetInfoCard, 16, 0, 400);
            PlaySlideFadeIn(MedicalCard, 16, 90, 420);
            PlaySlideFadeIn(VaccineCard, 16, 180, 420);
        }

        _medicalRows.Clear();
        _vaccineRows.Clear();
        _counters.RemoveAll(c => c.Target == MedicalCountText || c.Target == VaccineCountText);
        MedicalCountText.Text = " ";
        VaccineCountText.Text = " ";
        MedicalEmpty.Visibility = Visibility.Collapsed;
        VaccineEmpty.Visibility = Visibility.Collapsed;

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;

        try
        {
            using var medicalResponse = await ApiClient.Http.GetAsync($"api/medical-records?petId={requestedPetId}");

            if (!StillSelected(requestedPetId))
            {
                return;
            }

            if (medicalResponse.IsSuccessStatusCode)
            {
                var records = await medicalResponse.Content.ReadFromJsonAsync<List<MedicalRecordItem>>() ?? new();

                if (!StillSelected(requestedPetId))
                {
                    return;
                }

                // Rows animate only right after a load; give them a short window to be created.
                if (run)
                {
                    _animateMedicalRows = true;
                    _medicalWindowTimer.Stop();
                    _medicalWindowTimer.Start();
                }

                _medicalRows.Clear();
                foreach (var r in records) _medicalRows.Add(new MedicalRow(r));

                ShowCount(MedicalCountText, _medicalRows.Count, animate);
                MedicalEmpty.Visibility = _medicalRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (medicalResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthSession.Clear();
                RootNavigation.GoToLogin();
                return;
            }

            using var vaccineResponse = await ApiClient.Http.GetAsync($"api/vaccination-records?petId={requestedPetId}");

            if (!StillSelected(requestedPetId))
            {
                return;
            }

            if (vaccineResponse.IsSuccessStatusCode)
            {
                var records = await vaccineResponse.Content.ReadFromJsonAsync<List<VaccinationRecordItem>>() ?? new();

                if (!StillSelected(requestedPetId))
                {
                    return;
                }

                if (run)
                {
                    _animateVaccineRows = true;
                    _vaccineWindowTimer.Stop();
                    _vaccineWindowTimer.Start();
                }

                _vaccineRows.Clear();
                foreach (var r in records) _vaccineRows.Add(new VaccineRow(r));

                ShowCount(VaccineCountText, _vaccineRows.Count, animate);
                VaccineEmpty.Visibility = _vaccineRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            ShowError("Could not reach the API.");
        }
        finally
        {
            Busy.IsActive = false;
            PetContent.Opacity = 1;
        }
    }

    private bool StillSelected(int petId) =>
        PetSelectorBox.SelectedItem is PetItem current && current.PetId == petId;

    // Breed and category are only known when the pet came from the pet list,
    // not when an Adopter's list was rebuilt from their records.
    private static string BuildPetSubtitle(PetItem pet)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(pet.BreedName)) parts.Add(pet.BreedName);
        if (!string.IsNullOrWhiteSpace(pet.CategoryName)) parts.Add(pet.CategoryName);
        return parts.Count > 0 ? string.Join("  \u00b7  ", parts) : "Health history";
    }

    // ----- Medical record form -----

    private void AddMedical_Click(object sender, RoutedEventArgs e)
    {
        if (PetSelectorBox.SelectedItem is not PetItem)
        {
            ShowError("Please select a pet first.");
            return;
        }

        _editingMedicalId = null;
        MedicalFormTitle.Text = "Add Medical Record";
        ExamDatePicker.Date = DateTimeOffset.Now;
        HealthStatusBox.SelectedIndex = -1;
        DiagnosisBox.Text = string.Empty;
        SymptomsBox.Text = string.Empty;
        TreatmentBox.Text = string.Empty;
        MedicationsBox.Text = string.Empty;
        WeightBox.Text = string.Empty;
        FitForAdoptionBox.IsChecked = false;
        MedicalNotesBox.Text = string.Empty;
        NoNextCheckupBox.IsChecked = true;
        NextCheckupDatePicker.IsEnabled = false;
        MedicalFormPanel.Visibility = Visibility.Visible;
    }

    private void EditMedical_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not MedicalRow row) return;

        _editingMedicalId = row.Source.MedicalRecordId;
        MedicalFormTitle.Text = "Edit Medical Record";
        ExamDatePicker.Date = new DateTimeOffset(row.Source.ExaminationDate);
        HealthStatusBox.SelectedItem = HealthStatusBox.Items
            .Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Content == row.Source.HealthStatus);
        DiagnosisBox.Text = row.Source.Diagnosis ?? string.Empty;
        SymptomsBox.Text = row.Source.Symptoms ?? string.Empty;
        TreatmentBox.Text = row.Source.Treatment ?? string.Empty;
        MedicationsBox.Text = row.Source.Medications ?? string.Empty;
        WeightBox.Text = row.Source.WeightAtExam?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        FitForAdoptionBox.IsChecked = row.Source.FitForAdoption;
        MedicalNotesBox.Text = row.Source.Notes ?? string.Empty;

        if (row.Source.NextCheckupDate.HasValue)
        {
            NoNextCheckupBox.IsChecked = false;
            NextCheckupDatePicker.IsEnabled = true;
            NextCheckupDatePicker.Date = new DateTimeOffset(row.Source.NextCheckupDate.Value);
        }
        else
        {
            NoNextCheckupBox.IsChecked = true;
            NextCheckupDatePicker.IsEnabled = false;
        }

        MedicalFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelMedicalForm_Click(object sender, RoutedEventArgs e)
    {
        MedicalFormPanel.Visibility = Visibility.Collapsed;
        _editingMedicalId = null;
    }

    private void NoNextCheckup_Changed(object sender, RoutedEventArgs e)
    {
        NextCheckupDatePicker.IsEnabled = NoNextCheckupBox.IsChecked != true;
    }

    private async void SaveMedical_Click(object sender, RoutedEventArgs e)
    {
        if (PetSelectorBox.SelectedItem is not PetItem pet)
        {
            ShowError("Please select a pet first.");
            return;
        }

        var healthStatus = (HealthStatusBox.SelectedItem as ComboBoxItem)?.Content as string;
        if (healthStatus is null)
        {
            ShowError("Please choose a health status.");
            return;
        }

        decimal? weight = decimal.TryParse(WeightBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedWeight)
            ? parsedWeight : null;
        DateTime? nextCheckup = NoNextCheckupBox.IsChecked == true ? null : NextCheckupDatePicker.Date.DateTime;

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            HttpResponseMessage response;

            if (_editingMedicalId is int id)
            {
                var updateRequest = new UpdateMedicalRecordRequest
                {
                    ExaminationDate = ExamDatePicker.Date.DateTime,
                    Diagnosis = NullIfBlank(DiagnosisBox.Text),
                    Symptoms = NullIfBlank(SymptomsBox.Text),
                    Treatment = NullIfBlank(TreatmentBox.Text),
                    Medications = NullIfBlank(MedicationsBox.Text),
                    WeightAtExam = weight,
                    HealthStatus = healthStatus,
                    FitForAdoption = FitForAdoptionBox.IsChecked == true,
                    Notes = NullIfBlank(MedicalNotesBox.Text),
                    NextCheckupDate = nextCheckup
                };
                response = await ApiClient.Http.PutAsJsonAsync($"api/medical-records/{id}", updateRequest);
            }
            else
            {
                var createRequest = new CreateMedicalRecordRequest
                {
                    PetId = pet.PetId,
                    ExaminationDate = ExamDatePicker.Date.DateTime,
                    Diagnosis = NullIfBlank(DiagnosisBox.Text),
                    Symptoms = NullIfBlank(SymptomsBox.Text),
                    Treatment = NullIfBlank(TreatmentBox.Text),
                    Medications = NullIfBlank(MedicationsBox.Text),
                    WeightAtExam = weight,
                    HealthStatus = healthStatus,
                    FitForAdoption = FitForAdoptionBox.IsChecked == true,
                    Notes = NullIfBlank(MedicalNotesBox.Text),
                    NextCheckupDate = nextCheckup
                };
                response = await ApiClient.Http.PostAsJsonAsync("api/medical-records", createRequest);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    ShowSuccess(await ReadMessageAsync(response));
                    MedicalFormPanel.Visibility = Visibility.Collapsed;
                    await LoadRecordsForSelectedPetAsync(animate: false);
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

    // ----- Vaccination record form -----

    private void AddVaccine_Click(object sender, RoutedEventArgs e)
    {
        if (PetSelectorBox.SelectedItem is not PetItem)
        {
            ShowError("Please select a pet first.");
            return;
        }

        _editingVaccineId = null;
        VaccineFormTitle.Text = "Add Vaccination Record";
        VaccineNameBox.Text = string.Empty;
        ManufacturerBox.Text = string.Empty;
        BatchNumberBox.Text = string.Empty;
        DateAdministeredPicker.Date = DateTimeOffset.Now;
        NoNextDueBox.IsChecked = true;
        NextDueDatePicker.IsEnabled = false;
        IsBoosterBox.IsChecked = false;
        VaccineNotesBox.Text = string.Empty;
        VaccineFormPanel.Visibility = Visibility.Visible;
    }

    private void EditVaccine_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not VaccineRow row) return;

        _editingVaccineId = row.Source.VaccinationRecordId;
        VaccineFormTitle.Text = "Edit Vaccination Record";
        VaccineNameBox.Text = row.Source.VaccineName;
        ManufacturerBox.Text = row.Source.Manufacturer ?? string.Empty;
        BatchNumberBox.Text = row.Source.BatchNumber ?? string.Empty;
        DateAdministeredPicker.Date = new DateTimeOffset(row.Source.DateAdministered);
        IsBoosterBox.IsChecked = row.Source.IsBooster;
        VaccineNotesBox.Text = row.Source.Notes ?? string.Empty;

        if (row.Source.NextDueDate.HasValue)
        {
            NoNextDueBox.IsChecked = false;
            NextDueDatePicker.IsEnabled = true;
            NextDueDatePicker.Date = new DateTimeOffset(row.Source.NextDueDate.Value);
        }
        else
        {
            NoNextDueBox.IsChecked = true;
            NextDueDatePicker.IsEnabled = false;
        }

        VaccineFormPanel.Visibility = Visibility.Visible;
    }

    private void CancelVaccineForm_Click(object sender, RoutedEventArgs e)
    {
        VaccineFormPanel.Visibility = Visibility.Collapsed;
        _editingVaccineId = null;
    }

    private void NoNextDue_Changed(object sender, RoutedEventArgs e)
    {
        NextDueDatePicker.IsEnabled = NoNextDueBox.IsChecked != true;
    }

    private async void SaveVaccine_Click(object sender, RoutedEventArgs e)
    {
        if (PetSelectorBox.SelectedItem is not PetItem pet)
        {
            ShowError("Please select a pet first.");
            return;
        }

        var vaccineName = VaccineNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(vaccineName))
        {
            ShowError("Please enter a vaccine name.");
            return;
        }

        DateTime? nextDue = NoNextDueBox.IsChecked == true ? null : NextDueDatePicker.Date.DateTime;

        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;

        try
        {
            HttpResponseMessage response;

            if (_editingVaccineId is int id)
            {
                var updateRequest = new UpdateVaccinationRecordRequest
                {
                    VaccineName = vaccineName,
                    Manufacturer = NullIfBlank(ManufacturerBox.Text),
                    BatchNumber = NullIfBlank(BatchNumberBox.Text),
                    DateAdministered = DateAdministeredPicker.Date.DateTime,
                    NextDueDate = nextDue,
                    IsBooster = IsBoosterBox.IsChecked == true,
                    Notes = NullIfBlank(VaccineNotesBox.Text)
                };
                response = await ApiClient.Http.PutAsJsonAsync($"api/vaccination-records/{id}", updateRequest);
            }
            else
            {
                var createRequest = new CreateVaccinationRecordRequest
                {
                    PetId = pet.PetId,
                    VaccineName = vaccineName,
                    Manufacturer = NullIfBlank(ManufacturerBox.Text),
                    BatchNumber = NullIfBlank(BatchNumberBox.Text),
                    DateAdministered = DateAdministeredPicker.Date.DateTime,
                    NextDueDate = nextDue,
                    IsBooster = IsBoosterBox.IsChecked == true,
                    Notes = NullIfBlank(VaccineNotesBox.Text)
                };
                response = await ApiClient.Http.PostAsJsonAsync("api/vaccination-records", createRequest);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    ShowSuccess(await ReadMessageAsync(response));
                    VaccineFormPanel.Visibility = Visibility.Collapsed;
                    await LoadRecordsForSelectedPetAsync(animate: false);
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