using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;

namespace PAWS.api.Controllers;

// Sign-in for the Super Admin ONLY. This is a master-database Identity account,
// completely separate from tenant Users. It has no CompanyCode, no tenant
// database involvement, and its token carries no companyId claim.
[ApiController]
[Route("api/auth")]
public class PlatformAuthController : ControllerBase
{
    // Shown for EVERY platform-login failure, whatever the real cause.
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    // Shown for EVERY bootstrap-superadmin failure that must not reveal information.
    private const string CouldNotBootstrapMessage =
        "Could not create the Super Admin account with those details.";

    private readonly UserManager<IdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ITokenService _tokens;
    private readonly SuperAdminBootstrapSettings _bootstrapSettings;
    private readonly ILogger<PlatformAuthController> _logger;

    public PlatformAuthController(
        UserManager<IdentityUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ITokenService tokens,
        IOptions<SuperAdminBootstrapSettings> bootstrapSettings,
        ILogger<PlatformAuthController> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _tokens = tokens;
        _bootstrapSettings = bootstrapSettings.Value;
        _logger = logger;
    }

    // POST /api/auth/platform-login
    [HttpPost("platform-login")]
    public async Task<IActionResult> PlatformLogin(PlatformLoginRequestDto request)
    {
        var email = request.Email.Trim();

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Run a real password check anyway, against a throwaway hash, so a
            // missing account is not obviously faster than a wrong password.
            _ = _userManager.PasswordHasher.VerifyHashedPassword(
                new IdentityUser(), _dummyHash, request.Password);

            _logger.LogWarning("Platform login failed: unknown email.");
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var passwordOk = await _userManager.CheckPasswordAsync(user, request.Password);
        var roles = await _userManager.GetRolesAsync(user);
        var isSuperAdmin = roles.Contains(RoleNames.SuperAdmin);

        if (!passwordOk || !isSuperAdmin || user.LockoutEnabled && await _userManager.IsLockedOutAsync(user))
        {
            _logger.LogWarning(
                "Platform login failed for UserId {UserId}: passwordOk={PasswordOk}, isSuperAdmin={IsSuperAdmin}.",
                user.Id, passwordOk, isSuperAdmin);
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        // Super Admin tokens carry NO companyId claim at all.
        var accessToken = _tokens.CreateAccessToken(
            userId: 0, // Identity's user id is a string (Guid-like), not an int; not used for Super Admin.
            fullName: user.UserName ?? user.Email ?? "Super Admin",
            email: user.Email ?? email,
            roles: roles,
            companyId: null);

        return Ok(new PlatformLoginResponseDto
        {
            Token = accessToken.Token,
            ExpiresAtUtc = accessToken.ExpiresAtUtc,
            UserId = user.Id,
            Email = user.Email ?? email,
            Roles = roles.ToList()
        });
    }

    // POST /api/auth/bootstrap-superadmin
    // Creates the FIRST Super Admin account. Works only when:
    //   1. The SetupKey matches SuperAdminBootstrap:SetupKey, AND
    //   2. NO user currently holds the "Super Admin" role.
    [HttpPost("bootstrap-superadmin")]
    public async Task<IActionResult> BootstrapSuperAdmin(BootstrapSuperAdminRequestDto request)
    {
        if (string.IsNullOrEmpty(_bootstrapSettings.SetupKey) ||
            request.SetupKey != _bootstrapSettings.SetupKey)
        {
            _logger.LogWarning("Bootstrap-superadmin failed: incorrect setup key.");
            return Unauthorized(new { message = CouldNotBootstrapMessage });
        }

        // Ensure the Super Admin ROLE exists in the master database.
        if (!await _roleManager.RoleExistsAsync(RoleNames.SuperAdmin))
        {
            var roleResult = await _roleManager.CreateAsync(new IdentityRole(RoleNames.SuperAdmin));
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create the '{RoleNames.SuperAdmin}' role: " +
                    string.Join("; ", roleResult.Errors.Select(e => e.Description)));
            }
        }

        // Refuse if ANYONE already holds the Super Admin role.
        var existingSuperAdmins = await _userManager.GetUsersInRoleAsync(RoleNames.SuperAdmin);
        if (existingSuperAdmins.Count > 0)
        {
            _logger.LogWarning("Bootstrap-superadmin refused: a Super Admin already exists.");
            return BadRequest(new { message = CouldNotBootstrapMessage });
        }

        var email = request.Email.Trim();

        var user = new IdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            // A weak password, a malformed email, etc. Identity's own validation
            // messages are safe to show here, since this is a one-time setup step,
            // not a public-facing login.
            _logger.LogWarning(
                "Bootstrap-superadmin failed to create user: {Errors}",
                string.Join("; ", createResult.Errors.Select(e => e.Description)));
            return BadRequest(new { message = CouldNotBootstrapMessage });
        }

        await _userManager.AddToRoleAsync(user, RoleNames.SuperAdmin);

        _logger.LogInformation("Super Admin bootstrapped: UserId {UserId}.", user.Id);

        return Ok(new BootstrapSuperAdminResponseDto
        {
            Message = "Super Admin account created. Please sign in."
        });
    }

    // A real hash of a random value nobody knows, computed once per request instance.
    // Used only so a missing-account check takes about as long as a real one.
    private static readonly string _dummyHash =
        new PasswordHasher<IdentityUser>().HashPassword(new IdentityUser(), Guid.NewGuid().ToString("N"));
}