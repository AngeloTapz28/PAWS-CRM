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

public sealed partial class RegisterPage : Page
{
    public RegisterPage()
    {
        InitializeComponent();
    }

    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        var companyCode = CompanyCodeBox.Text.Trim();
        var fullName = FullNameBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        var phone = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim();
        var password = PasswordInput.Password;
        var confirmPassword = ConfirmPasswordInput.Password;

        if (string.IsNullOrWhiteSpace(companyCode) ||
            string.IsNullOrWhiteSpace(fullName) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(confirmPassword))
        {
            ShowError("Please fill in all the required fields.");
            return;
        }

        // Checked here too, even though the API also checks it, so the person
        // does not have to wait for a round trip to the server to find a typo.
        if (password != confirmPassword)
        {
            ShowError("Password and Confirm Password do not match.");
            return;
        }

        RegisterButton.IsEnabled = false;
        Busy.IsActive = true;
        ErrorBar.IsOpen = false;

        try
        {
            var request = new RegisterRequest
            {
                CompanyCode = companyCode,
                FullName = fullName,
                Email = email,
                Phone = phone,
                Password = password,
                ConfirmPassword = confirmPassword
            };

            // Sends: POST https://localhost:7129/api/auth/register
            using var response = await ApiClient.Http.PostAsJsonAsync("api/auth/register", request);

            if (response.IsSuccessStatusCode)
            {
                // Per the chosen design: NOT signed in automatically.
                // Send the person back to the login page, with a confirmation message
                // and the Company Code / Email already filled in.
                RootNavigation.GoToLoginAfterRegistration(companyCode, email);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var message = ReadField(json, "message");

            if (message != "(not provided)")
            {
                // The API's generic message (duplicate email / unknown company code)
                // or its specific validation message (e.g. passwords do not match).
                ShowError(message);
            }
            else
            {
                // Field-validation errors come back in a different shape
                // ({ "errors": { "FieldName": ["message"] } }); show the first one.
                var firstValidationMessage = ReadFirstValidationMessage(json);
                ShowError(firstValidationMessage ?? "Registration failed. Please check your details and try again.");
            }
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
            RegisterButton.IsEnabled = true;
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

    // Reads the first message out of ASP.NET Core's standard validation error shape:
    // { "errors": { "SomeField": ["message one", "message two"], ... } }
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
        ErrorBar.Title = "Could not create the account";
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}