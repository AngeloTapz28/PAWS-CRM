namespace PAWS.winui.Models;

// Matches the API's PlatformLoginRequestDto. No CompanyCode: Super Admin belongs to no company.
public class PlatformLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}