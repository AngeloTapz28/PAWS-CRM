using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PAWS.winui.Models;
using PAWS.winui.Services;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace PAWS.winui.Views;

public sealed partial class LoginPage : Page
{
    public LoginPage()
    {
        InitializeComponent();

        // Start the logo animation when the page appears; stop it when the page goes away,
        // so nothing keeps running in the background after sign-in.
        Loaded += (s, e) => StartLogoAnimation();
        Unloaded += (s, e) => LogoAnimation.Stop();
    }

    private void StartLogoAnimation()
    {
        // Respect the Windows "Show animations" setting.
        bool animationsEnabled = true;
        try
        {
            animationsEnabled = new UISettings().AnimationsEnabled;
        }
        catch
        {
            // If the setting cannot be read, animate anyway.
        }

        if (animationsEnabled)
        {
            LogoAnimation.Begin();
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is (string companyCode, string email))
        {
            CompanyCodeBox.Text = companyCode;
            EmailBox.Text = email;

            InfoMessageBar.Title = "Account created";
            InfoMessageBar.Message = "Please sign in with your new account.";
            InfoMessageBar.IsOpen = true;
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        var companyCode = CompanyCodeBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        var password = PasswordInput.Password;

        if (string.IsNullOrWhiteSpace(companyCode) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            ShowError("Please fill in the Company Code, Email and Password.");
            return;
        }

        LoginButton.IsEnabled = false;
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;
        InfoMessageBar.IsOpen = false;

        try
        {
            var request = new LoginRequest
            {
                CompanyCode = companyCode,
                Email = email,
                Password = password
            };

            using var response = await ApiClient.Http.PostAsJsonAsync("api/auth/login", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                if (result is null)
                {
                    ShowError("The server sent an unexpected reply. Please try again.");
                    return;
                }

                AuthSession.SetTenantSession(result);
                RootNavigation.GoToShell();
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var message = ReadField(json, "message");
            ShowError(message == "(not provided)"
                ? "Sign-in failed. Please check your details and try again."
                : message);
        }
        catch (TaskCanceledException)
        {
            ShowError("The server did not answer in time. Check your connection and try again.");
        }
        catch (HttpRequestException ex)
        {
            ShowError($"Could not reach the server. {ex.Message}");
        }
        finally
        {
            Busy.IsActive = false;
            LoginButton.IsEnabled = true;
        }
    }

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        RootNavigation.GoToRegister();
    }

    private void PlatformLogin_Click(object sender, RoutedEventArgs e)
    {
        RootNavigation.GoToPlatformLogin();
    }

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

    private void ShowError(string message)
    {
        ErrorBar.Title = "Could not sign in";
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}