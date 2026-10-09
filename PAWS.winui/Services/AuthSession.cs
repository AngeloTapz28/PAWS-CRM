using PAWS.winui.Models;
using System;
using System.Collections.Generic;

namespace PAWS.winui.Services;

// Holds the CURRENTLY signed-in user's info, in memory only. Nothing here is
// saved to disk: closing the app forgets it, and the person must sign in again
// next time. This is the ONE place the rest of the app checks to know who is
// signed in, which company (if any) they belong to, and what they may see.
public static class AuthSession
{
    public static string? Token { get; private set; }
    public static DateTime? ExpiresAtUtc { get; private set; }

    // Tenant-user fields. Empty/zero/null for a Super Admin session.
    public static int UserId { get; private set; }
    public static int? CompanyId { get; private set; }
    public static string? CompanyCode { get; private set; }

    // Common to both kinds of session.
    public static string FullName { get; private set; } = string.Empty;
    public static string Email { get; private set; } = string.Empty;
    public static List<string> Roles { get; private set; } = new();

    public static bool IsSignedIn => !string.IsNullOrEmpty(Token);

    // True for a Super Admin session: no company, platform-level access only.
    public static bool IsSuperAdmin => Roles.Contains("Super Admin");

    // Called right after a successful tenant login (Company Code + Email + Password).
    public static void SetTenantSession(LoginResponse response)
    {
        Token = response.Token;
        ExpiresAtUtc = response.ExpiresAtUtc;
        UserId = response.UserId;
        FullName = response.FullName;
        Email = response.Email;
        Roles = response.Roles ?? new List<string>();
        CompanyId = response.CompanyId;
        CompanyCode = response.CompanyCode;

        ApiClient.SetAuthToken(Token);
    }

    // Called right after a successful Super Admin (platform) login.
    // Deliberately sets NO CompanyId/CompanyCode: the Super Admin belongs to no company.
    public static void SetPlatformSession(PlatformLoginResponse response)
    {
        Token = response.Token;
        ExpiresAtUtc = response.ExpiresAtUtc;
        UserId = 0;
        FullName = response.Email;
        Email = response.Email;
        Roles = response.Roles ?? new List<string>();
        CompanyId = null;
        CompanyCode = null;

        ApiClient.SetAuthToken(Token);
    }

    // Called on sign-out, or when the API rejects the token (e.g. it expired).
    public static void Clear()
    {
        Token = null;
        ExpiresAtUtc = null;
        UserId = 0;
        FullName = string.Empty;
        Email = string.Empty;
        Roles = new List<string>();
        CompanyId = null;
        CompanyCode = null;

        ApiClient.ClearAuthToken();
    }
}