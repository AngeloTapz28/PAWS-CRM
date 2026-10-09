namespace PAWS.winui.Models;

// Matches the API's UpdateUserRequestDto.
public class UpdateUserRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Role { get; set; } = string.Empty;
}