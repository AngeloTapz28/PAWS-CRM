using System;
using System.Collections.Generic;

namespace PAWS.winui.Models;

// Matches the API's PlatformLoginResponseDto. No CompanyId/CompanyCode fields.
public class PlatformLoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
}