namespace PAWS.winui.Models;

// Matches the API's LoginRequestDto.
public class LoginRequest
{
    public string CompanyCode { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}