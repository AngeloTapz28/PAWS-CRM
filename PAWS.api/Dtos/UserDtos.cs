using System.ComponentModel.DataAnnotations;

namespace PAWS.api.Dtos;

// One row in the Manage Users list.
public class UserListItemDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateUserRequestDto
{
    [Required]
    [StringLength(200, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string? Phone { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(Password), ErrorMessage = "Password and Confirm Password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    // Must be one of RoleNames.ManageableRoles (Administrator, Staff, Veterinarian).
    // Checked in the controller, not by a fixed [AllowedValues] list, so the
    // rule stays in one place (RoleNames.ManageableRoles).
    [Required]
    public string Role { get; set; } = string.Empty;
}

public class UpdateUserRequestDto
{
    [Required]
    [StringLength(200, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    public string? Phone { get; set; }

    // Must be one of RoleNames.ManageableRoles. An Administrator editing their
    // OWN account must send their current role unchanged (checked in the controller).
    [Required]
    public string Role { get; set; } = string.Empty;
}

public class SimpleMessageDto
{
    public string Message { get; set; } = string.Empty;
}