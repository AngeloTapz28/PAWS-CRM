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

public sealed partial class NotificationsPage : Page
{
    // ----- Animation tuning -----
    private const int MaxAnimatedCards = 10;     // only the first cards animate; the rest are off-screen
    private const double CardStaggerMs = 55;
    private const double CountUpMs = 700;

    // Looks up a PAWS brush from PawsTheme.xaml
    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public class NotificationRow : INotifyPropertyChanged
    {
        public NotificationItem Source { get; }
        public NotificationRow(NotificationItem source) => Source = source;

        public bool IsUnread => !Source.ReadAt.HasValue;

        // Unread cards have a soft coral background; read ones are plain white.
        public Brush CardBackground => IsUnread ? Res("PawsPrimarySoftBrush") : Res("PawsSurfaceBrush");

        // ----- Type icon (success = green check, warning = amber, anything else = blue info) -----
        public string IconGlyph => Source.Type switch
        {
            "success" => "\uE73E",
            "warning" => "\uE7BA",
            _ => "\uE946"
        };

        public Brush IconTileBackground => Source.Type switch
        {
            "success" => Res("PawsSuccessSoftBrush"),
            "warning" => Res("PawsWarningSoftBrush"),
            _ => Res("PawsInfoSoftBrush")
        };

        public Brush IconForeground => Source.Type switch
        {
            "success" => Res("PawsSuccessBrush"),
            "warning" => Res("PawsWarningBrush"),
            _ => Res("PawsInfoBrush")
        };

        public string TimeText => Source.CreatedAt.ToString("MMM d, yyyy \\a\\t h:mm tt");

        public Visibility MarkReadVisibility => IsUnread ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<NotificationRow> _rows = new();
    private List<NotificationItem> _allItems = new();
    private bool _hasLoaded;

    // ----- Animation state -----
    private bool _animate = true;
    private bool _animateCards;                  // true only right after the list is (re)loaded

    private readonly DispatcherTimer _cardsWindowTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    private readonly DispatcherTimer _countTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private DateTime _countStart;
    private int _countTotal;
    private int _countUnread;

    public NotificationsPage()
    {
        InitializeComponent();
        NotificationsList.ItemsSource = _rows;
        FilterBox.SelectedIndex = 0;

        _countTimer.Tick += CountTimer_Tick;

        // After the cards have been created, stop animating them (so later updates are instant).
        _cardsWindowTimer.Tick += (s, e) =>
        {
            _cardsWindowTimer.Stop();
            _animateCards = false;
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _animate = ReadAnimationSetting();

        if (_animate)
        {
            // The header fades in, then the filter card (they do not depend on data).
            PrepareFade(HeaderPanel);
            PrepareFade(ToolbarCard);
            PlayFade(HeaderPanel, 0, 420);
            PlayFade(ToolbarCard, 110, 420);
        }

        await LoadAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        // Never leave a timer running after the page is gone.
        _countTimer.Stop();
        _cardsWindowTimer.Stop();
        _animateCards = false;
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

    // ----- Cards: slide up and fade in right after a (re)load -----

    private void Card_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_animateCards || sender is not Border card || card.DataContext is not NotificationRow data)
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
            card.BorderBrush = Res("PawsPrimaryBrush");
        }
    }

    private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            AnimateLift(card, 0f);
            card.BorderBrush = Res("PawsBorderBrush");
        }
    }

    // ----- Count-up for "N notifications • M unread" -----

    private static string SummaryLabel(int total, int unread)
    {
        var totalText = total == 1 ? "1 notification" : $"{total} notifications";
        return $"{totalText} \u2022 {unread} unread";
    }

    private void StartCountUp(int total, int unread)
    {
        _countTotal = total;
        _countUnread = unread;
        _countStart = DateTime.UtcNow;
        SummaryText.Text = SummaryLabel(0, 0);
        _countTimer.Start();
    }

    private void CountTimer_Tick(object? sender, object e)
    {
        double t = Math.Min(1.0, (DateTime.UtcNow - _countStart).TotalMilliseconds / CountUpMs);

        // Ease-out: fast at first, slowing down at the end.
        double eased = 1.0 - Math.Pow(1.0 - t, 3);

        // The last tick always writes the exact values.
        int total = t >= 1.0 ? _countTotal : (int)Math.Round(_countTotal * eased);
        int unread = t >= 1.0 ? _countUnread : (int)Math.Round(_countUnread * eased);
        SummaryText.Text = SummaryLabel(total, unread);

        if (t >= 1.0)
        {
            _countTimer.Stop();
        }
    }

    // ----- Filter (applied to the notifications already loaded; always instant) -----

    private bool UnreadOnly => FilterBox.SelectedIndex == 1;

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    // 'afterLoad' is true only when the list has just been loaded from the API and the
    // caller asked for animation.
    private void ApplyFilter(bool afterLoad = false)
    {
        var items = UnreadOnly
            ? _allItems.Where(i => !i.ReadAt.HasValue).ToList()
            : _allItems;

        // Cards animate only right after a load; give them a short window to be created.
        if (afterLoad && _animate)
        {
            _animateCards = true;
            _cardsWindowTimer.Stop();
            _cardsWindowTimer.Start();
        }

        _rows.Clear();
        foreach (var i in items) _rows.Add(new NotificationRow(i));

        var unread = _allItems.Count(i => !i.ReadAt.HasValue);

        if (afterLoad && _animate && _allItems.Count > 0)
        {
            StartCountUp(_allItems.Count, unread);
        }
        else
        {
            _countTimer.Stop();
            SummaryText.Text = SummaryLabel(_allItems.Count, unread);
        }

        // Nothing to mark when everything is already read.
        MarkAllReadButton.IsEnabled = unread > 0;

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        bool isEmpty = _hasLoaded && _rows.Count == 0 && !Busy.IsActive;

        if (isEmpty)
        {
            if (UnreadOnly && _allItems.Count > 0)
            {
                EmptyTitleText.Text = "You're all caught up";
                EmptyHintText.Text = "There are no unread notifications.";
            }
            else
            {
                EmptyTitleText.Text = "No notifications yet";
                EmptyHintText.Text = "Updates about your applications, pets and requests will appear here.";
            }
        }

        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    // ----- Load -----

    // 'animate' is true when the page opens or Refresh is pressed;
    // false after Mark as read / Mark All as Read, so the list updates in place.
    private async Task LoadAsync(bool animate = true)
    {
        Busy.IsActive = true;
        ListHost.Opacity = 0.5;
        ErrorBar.IsOpen = false;

        _animate = ReadAnimationSetting();

        try
        {
            using var response = await ApiClient.Http.GetAsync("api/notifications");

            if (response.IsSuccessStatusCode)
            {
                _allItems = await response.Content.ReadFromJsonAsync<List<NotificationItem>>() ?? new();
                _hasLoaded = true;
                ApplyFilter(afterLoad: animate);
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

    // ----- Mark as read -----

    private async void MarkRead_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not NotificationRow row) return;

        Busy.IsActive = true;
        try
        {
            using var response = await ApiClient.Http.PostAsync($"api/notifications/{row.Source.SystemNotificationId}/read", null);
            if (response.IsSuccessStatusCode)
            {
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

    private async void MarkAllRead_Click(object sender, RoutedEventArgs e)
    {
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;

        try
        {
            using var response = await ApiClient.Http.PostAsync("api/notifications/read-all", null);
            if (response.IsSuccessStatusCode)
            {
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
    }
}