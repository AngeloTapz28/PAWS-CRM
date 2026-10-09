namespace PAWS.winui.Models;

// Matches the API's RegisterRequestDto.
public class RegisterRequest
{
    public string CompanyCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}