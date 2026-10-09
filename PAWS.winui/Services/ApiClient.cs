using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace PAWS.winui.Services;

// The ONE HttpClient that every page uses to talk to the PAWS API.
// It is created once, with the API address and a timeout already set.
public static class ApiClient
{
    // "static readonly" = created a single time and reused for the whole app run.
    public static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            // Every request path (like "api/health") is added to this address.
            BaseAddress = new Uri(ApiSettings.BaseUrl),

            // Longer than the API's own 10-second health check, so an API error
            // (like 503) reaches us instead of the app timing out first.
            Timeout = TimeSpan.FromSeconds(15)
        };

        // Tell the API we want JSON back.
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    // Attaches a sign-in token to every future request. Called by AuthSession
    // right after a successful login.
    public static void SetAuthToken(string? token)
    {
        Http.DefaultRequestHeaders.Authorization =
            string.IsNullOrEmpty(token) ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    // Removes the token. Called on sign-out.
    public static void ClearAuthToken()
    {
        Http.DefaultRequestHeaders.Authorization = null;
    }
}