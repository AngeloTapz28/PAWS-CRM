using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PAWS.winui.Models;
using PAWS.winui.Services;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PAWS.winui.Views;

public sealed partial class PlatformLoginPage : Page
{
    public PlatformLoginPage()
    {
        InitializeComponent();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        var password = PasswordInput.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ShowError("Please fill in Email and Password.");
            return;
        }

        LoginButton.IsEnabled = false;
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;

        try
        {
            var request = new PlatformLoginRequest { Email = email, Password = password };

            // Sends: POST https://localhost:7129/api/auth/platform-login
            using var response = await ApiClient.Http.PostAsJsonAsync("api/auth/platform-login", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<PlatformLoginResponse>();
                if (result is null)
                {
                    ShowError("The server sent an unexpected reply. Please try again.");
                    return;
                }

                AuthSession.SetPlatformSession(result);
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

    private void BackToLogin_Click(object sender, RoutedEventArgs e)
    {
        RootNavigation.GoToLogin();
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