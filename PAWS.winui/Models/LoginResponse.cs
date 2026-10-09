using System;
using System.Collections.Generic;

namespace PAWS.winui.Models;

// Matches the API's LoginResponseDto.
public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }

    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();

    public int CompanyId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
}