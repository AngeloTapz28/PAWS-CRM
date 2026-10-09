using System.ComponentModel.DataAnnotations;

namespace PAWS.api.Dtos;

public class LoginRequestDto
{
    [Required]
    public string CompanyCode { get; set; } = string.Empty;

    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponseDto
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

public class RegisterRequestDto
{
    [Required]
    public string CompanyCode { get; set; } = string.Empty;

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

    // Deliberately NO role field here. Self-registration always becomes Adopter.
}

public class RegisterResponseDto
{
    public string Message { get; set; } = string.Empty;
}

public class BootstrapAdminRequestDto
{
    [Required]
    public string CompanyCode { get; set; } = string.Empty;

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

    [Required]
    public string SetupKey { get; set; } = string.Empty;
}

public class BootstrapAdminResponseDto
{
    public string Message { get; set; } = string.Empty;
}

// ----- NEW: Super Admin (platform) auth -----

public class PlatformLoginRequestDto
{
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    // Deliberately NO CompanyCode. Super Admin belongs to no company.
}

public class PlatformLoginResponseDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();

    // Deliberately no CompanyId/CompanyCode fields: Super Admin has none.
}

public class BootstrapSuperAdminRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(Password), ErrorMessage = "Password and Confirm Password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required]
    public string SetupKey { get; set; } = string.Empty;
}

public class BootstrapSuperAdminResponseDto
{
    public string Message { get; set; } = string.Empty;
}