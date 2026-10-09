using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.domain.Entities;
using PAWS.infrastructure.Data;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    // Shown for EVERY login failure caused by a credential problem, so a caller
    // cannot tell a wrong company code from a wrong email from a wrong password.
    private const string InvalidCredentialsMessage = "Invalid company code, email or password.";

    // Shown when the company itself exists but has been deactivated. This is
    // NOT a credential problem — every legitimate employee is affected at once,
    // so a distinct, actionable message is appropriate here. It reveals only
    // that the company is inactive, never anything about a specific account.
    private const string CompanySuspendedMessage =
        "This company's access has been suspended. Please contact your administrator.";

    // Shown for EVERY registration failure that must not reveal information
    // (unknown company code, or an email that is already registered).
    private const string CouldNotRegisterMessage =
        "We couldn't create the account with those details. Check them, or sign in if you already have an account.";

    // Shown for EVERY bootstrap failure. Deliberately vague: it must not confirm
    // whether a company code exists, whether it already has an administrator,
    // or whether the setup key was close to correct.
    private const string CouldNotBootstrapMessage =
        "Could not create the administrator account with those details.";

    private readonly MasterErpDbContext _masterDb;
    private readonly ITenantDatabaseResolver _tenantResolver;
    private readonly ITenantDbContextFactory _tenantFactory;
    private readonly IPasswordService _passwords;
    private readonly ITokenService _tokens;
    private readonly BootstrapSettings _bootstrapSettings;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        MasterErpDbContext masterDb,
        ITenantDatabaseResolver tenantResolver,
        ITenantDbContextFactory tenantFactory,
        IPasswordService passwords,
        ITokenService tokens,
        IOptions<BootstrapSettings> bootstrapSettings,
        ILogger<AuthController> logger)
    {
        _masterDb = masterDb;
        _tenantResolver = tenantResolver;
        _tenantFactory = tenantFactory;
        _passwords = passwords;
        _tokens = tokens;
        _bootstrapSettings = bootstrapSettings.Value;
        _logger = logger;
    }

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequestDto request, CancellationToken cancellationToken)
    {
        var companyCode = request.CompanyCode.Trim();
        var email = request.Email.Trim();

        var company = await _masterDb.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyCode.ToUpper() == companyCode.ToUpper(), cancellationToken);

        if (company is null)
        {
            _passwords.PretendToVerify(request.Password);
            _logger.LogWarning("Login failed: unknown company code '{CompanyCode}'.", companyCode);
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        if (!company.IsActive)
        {
            _passwords.PretendToVerify(request.Password);
            _logger.LogWarning("Login blocked: CompanyId {CompanyId} is deactivated.", company.CompanyId);
            return StatusCode(StatusCodes.Status403Forbidden, new { message = CompanySuspendedMessage });
        }

        try
        {
            await _tenantResolver.GetDatabaseInfoAsync(company.CompanyId);
        }
        catch (InvalidOperationException ex)
        {
            _passwords.PretendToVerify(request.Password);
            _logger.LogWarning(ex, "Login failed: no active tenant database for CompanyId {CompanyId}.", company.CompanyId);
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        await using TenantErpDbContext db = await _tenantFactory.CreateAsync(company.CompanyId);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            _passwords.PretendToVerify(request.Password);
            _logger.LogWarning("Login failed: unknown email in CompanyId {CompanyId}.", company.CompanyId);
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var passwordOk = _passwords.Verify(user.Password, request.Password);
        var accountUsable = user.Status == UserStatuses.Active && user.IsActive;

        if (!passwordOk || !accountUsable)
        {
            _logger.LogWarning(
                "Login failed for UserId {UserId}: passwordOk={PasswordOk}, status={Status}, isActive={IsActive}.",
                user.UserId, passwordOk, user.Status, user.IsActive);
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var roles = await db.RoleUsers
            .Where(ru => ru.UserId == user.UserId && ru.Role != null)
            .Select(ru => ru.Role!.Name)
            .ToListAsync(cancellationToken);

        var accessToken = _tokens.CreateAccessToken(
            userId: user.UserId,
            fullName: user.FullName,
            email: user.Email,
            roles: roles,
            companyId: company.CompanyId);

        return Ok(new LoginResponseDto
        {
            Token = accessToken.Token,
            ExpiresAtUtc = accessToken.ExpiresAtUtc,
            UserId = user.UserId,
            FullName = user.FullName,
            Email = user.Email,
            Roles = roles,
            CompanyId = company.CompanyId,
            CompanyCode = company.CompanyCode
        });
    }

    // POST /api/auth/register
    // Adopter self-registration ONLY. There is no role field in the request:
    // every account created here gets exactly one role, Adopter, assigned below.
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequestDto request, CancellationToken cancellationToken)
    {
        var companyCode = request.CompanyCode.Trim();
        var email = request.Email.Trim();
        var fullName = request.FullName.Trim();

        var company = await _masterDb.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyCode.ToUpper() == companyCode.ToUpper(), cancellationToken);

        if (company is null)
        {
            _logger.LogWarning("Registration failed: unknown company code '{CompanyCode}'.", companyCode);
            return BadRequest(new { message = CouldNotRegisterMessage });
        }

        if (!company.IsActive)
        {
            _logger.LogWarning("Registration blocked: CompanyId {CompanyId} is deactivated.", company.CompanyId);
            return StatusCode(StatusCodes.Status403Forbidden, new { message = CompanySuspendedMessage });
        }

        try
        {
            await _tenantResolver.GetDatabaseInfoAsync(company.CompanyId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Registration failed: no active tenant database for CompanyId {CompanyId}.", company.CompanyId);
            return BadRequest(new { message = CouldNotRegisterMessage });
        }

        await using TenantErpDbContext db = await _tenantFactory.CreateAsync(company.CompanyId);

        var emailTaken = await db.Users.AnyAsync(u => u.Email == email, cancellationToken);
        if (emailTaken)
        {
            _logger.LogWarning("Registration failed: email already used in CompanyId {CompanyId}.", company.CompanyId);
            return BadRequest(new { message = CouldNotRegisterMessage });
        }

        var adopterRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Adopter, cancellationToken);
        if (adopterRole is null)
        {
            throw new InvalidOperationException(
                $"The '{RoleNames.Adopter}' role was not found in CompanyId {company.CompanyId}.");
        }

        var user = new User
        {
            FullName = fullName,
            Email = email,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Password = _passwords.Hash(request.Password),
            Status = UserStatuses.Active,
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        db.RoleUsers.Add(new RoleUser { UserId = user.UserId, RoleId = adopterRole.RoleId });
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "New Adopter registered: UserId {UserId} in CompanyId {CompanyId}.",
            user.UserId, company.CompanyId);

        return Ok(new RegisterResponseDto
        {
            Message = "Account created. Please sign in."
        });
    }

    // POST /api/auth/bootstrap-admin
    // Creates the FIRST Administrator of a company. Works only when:
    //   1. The SetupKey matches the one in User Secrets/environment, AND
    //   2. The company currently has ZERO users.
    // Once an Administrator exists, use Manage Users (Step 15.7) instead.
    [HttpPost("bootstrap-admin")]
    public async Task<IActionResult> BootstrapAdmin(BootstrapAdminRequestDto request, CancellationToken cancellationToken)
    {
        // 1. Check the setup key FIRST, before touching any database. A wrong key
        //    should not reveal anything about whether the company exists.
        if (string.IsNullOrEmpty(_bootstrapSettings.SetupKey) ||
            request.SetupKey != _bootstrapSettings.SetupKey)
        {
            _logger.LogWarning("Bootstrap-admin failed: incorrect setup key.");
            return Unauthorized(new { message = CouldNotBootstrapMessage });
        }

        var companyCode = request.CompanyCode.Trim();
        var email = request.Email.Trim();
        var fullName = request.FullName.Trim();

        var company = await _masterDb.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyCode.ToUpper() == companyCode.ToUpper(), cancellationToken);

        if (company is null || !company.IsActive)
        {
            // Deliberately kept as a single generic branch here (unlike Login/
            // Register): bootstrap is a rare, sensitive setup operation, and
            // this endpoint's message is already vague-by-design so a caller
            // learns nothing about the company either way.
            _logger.LogWarning("Bootstrap-admin failed: unknown or inactive company code '{CompanyCode}'.", companyCode);
            return BadRequest(new { message = CouldNotBootstrapMessage });
        }

        try
        {
            await _tenantResolver.GetDatabaseInfoAsync(company.CompanyId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Bootstrap-admin failed: no active tenant database for CompanyId {CompanyId}.", company.CompanyId);
            return BadRequest(new { message = CouldNotBootstrapMessage });
        }

        await using TenantErpDbContext db = await _tenantFactory.CreateAsync(company.CompanyId);

        // 2. This company must have NO users at all yet. This is what stops the
        //    endpoint being used to add a second administrator later.
        var alreadyHasUsers = await db.Users.AnyAsync(cancellationToken);
        if (alreadyHasUsers)
        {
            _logger.LogWarning("Bootstrap-admin refused: CompanyId {CompanyId} already has users.", company.CompanyId);
            return BadRequest(new { message = CouldNotBootstrapMessage });
        }

        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Administrator, cancellationToken);
        if (adminRole is null)
        {
            throw new InvalidOperationException(
                $"The '{RoleNames.Administrator}' role was not found in CompanyId {company.CompanyId}.");
        }

        var user = new User
        {
            FullName = fullName,
            Email = email,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Password = _passwords.Hash(request.Password),
            Status = UserStatuses.Active,
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        db.RoleUsers.Add(new RoleUser { UserId = user.UserId, RoleId = adminRole.RoleId });
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Bootstrap administrator created: UserId {UserId} in CompanyId {CompanyId}.",
            user.UserId, company.CompanyId);

        return Ok(new BootstrapAdminResponseDto
        {
            Message = "Administrator account created. Please sign in."
        });
    }
}