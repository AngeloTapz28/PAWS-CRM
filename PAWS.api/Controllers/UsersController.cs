using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.domain.Entities;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

// Manage Users: Administrator only, and only within the Administrator's own
// company. Adopters are never created here (self-registration only, 15.3b).
[Authorize(Roles = RoleNames.Administrator)]
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;
    private readonly IPasswordService _passwords;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        ICurrentCompanyService currentCompany,
        ITenantDbContextFactory tenantFactory,
        IPasswordService passwords,
        ILogger<UsersController> logger)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
        _passwords = passwords;
        _logger = logger;
    }

    // GET /api/users
    [HttpGet]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        // One role per user is assumed throughout this controller (Step 15.7's
        // simplification). FirstOrDefault picks whichever role row exists.
        var users = await db.Users
            .OrderBy(u => u.FullName)
            .Select(u => new UserListItemDto
            {
                UserId = u.UserId,
                FullName = u.FullName,
                Email = u.Email,
                Phone = u.Phone,
                Role = db.RoleUsers
                    .Where(ru => ru.UserId == u.UserId && ru.Role != null)
                    .Select(ru => ru.Role!.Name)
                    .FirstOrDefault() ?? "(none)",
                Status = u.Status,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    // GET /api/users/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetUser(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        var role = await db.RoleUsers
            .Where(ru => ru.UserId == id && ru.Role != null)
            .Select(ru => ru.Role!.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "(none)";

        return Ok(new UserListItemDto
        {
            UserId = user.UserId,
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            Role = role,
            Status = user.Status,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt
        });
    }

    // POST /api/users
    // Creates a Staff, Veterinarian or Administrator account. Adopters cannot
    // be created here.
    [HttpPost]
    public async Task<IActionResult> CreateUser(CreateUserRequestDto request, CancellationToken cancellationToken)
    {
        if (!RoleNames.ManageableRoles.Contains(request.Role))
        {
            return BadRequest(new { message = $"Role must be one of: {string.Join(", ", RoleNames.ManageableRoles)}." });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var email = request.Email.Trim();
        var emailTaken = await db.Users.AnyAsync(u => u.Email == email, cancellationToken);
        if (emailTaken)
        {
            return BadRequest(new { message = "A user with that email already exists in this company." });
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == request.Role, cancellationToken);
        if (role is null)
        {
            // Seeding bug, not a user mistake: surface as a real 500.
            throw new InvalidOperationException($"The '{request.Role}' role was not found in CompanyId {companyId.Value}.");
        }

        var newUser = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Password = _passwords.Hash(request.Password),
            Status = UserStatuses.Active,
            IsActive = true
        };

        db.Users.Add(newUser);
        await db.SaveChangesAsync(cancellationToken);

        var currentUserId = GetCurrentUserId();
        db.RoleUsers.Add(new RoleUser
        {
            UserId = newUser.UserId,
            RoleId = role.RoleId,
            AssignedBy = currentUserId
        });
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User created: UserId {UserId}, Role {Role}, CompanyId {CompanyId}, by AdminUserId {AdminUserId}.",
            newUser.UserId, request.Role, companyId.Value, currentUserId);

        return Ok(new SimpleMessageDto { Message = $"{request.Role} account created." });
    }

    // PUT /api/users/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, UpdateUserRequestDto request, CancellationToken cancellationToken)
    {
        if (!RoleNames.ManageableRoles.Contains(request.Role))
        {
            return BadRequest(new { message = $"Role must be one of: {string.Join(", ", RoleNames.ManageableRoles)}." });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        var currentUserId = GetCurrentUserId();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        var currentRoleRow = await db.RoleUsers
            .Include(ru => ru.Role)
            .FirstOrDefaultAsync(ru => ru.UserId == id, cancellationToken);
        var currentRoleName = currentRoleRow?.Role?.Name ?? "(none)";

        // Safety rail: an Administrator may not change their OWN role, even to
        // another manageable role. This prevents a company locking itself out
        // of Administrator access by accident.
        if (id == currentUserId && request.Role != currentRoleName)
        {
            return BadRequest(new { message = "You cannot change your own role." });
        }

        user.FullName = request.FullName.Trim();
        user.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        if (request.Role != currentRoleName)
        {
            var newRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == request.Role, cancellationToken);
            if (newRole is null)
            {
                throw new InvalidOperationException($"The '{request.Role}' role was not found in CompanyId {companyId.Value}.");
            }

            if (currentRoleRow is not null)
            {
                db.RoleUsers.Remove(currentRoleRow);
            }

            db.RoleUsers.Add(new RoleUser
            {
                UserId = id,
                RoleId = newRole.RoleId,
                AssignedBy = currentUserId
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User updated: UserId {UserId}, Role {OldRole} -> {NewRole}, by AdminUserId {AdminUserId}.",
            id, currentRoleName, request.Role, currentUserId);

        return Ok(new SimpleMessageDto { Message = "User updated." });
    }

    // POST /api/users/{id}/deactivate
    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        if (id == currentUserId)
        {
            return BadRequest(new { message = "You cannot deactivate your own account." });
        }

        return await SetActiveStatus(id, isActive: false, cancellationToken);
    }

    // POST /api/users/{id}/activate
    [HttpPost("{id:int}/activate")]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
    {
        return await SetActiveStatus(id, isActive: true, cancellationToken);
    }

    private async Task<IActionResult> SetActiveStatus(int id, bool isActive, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        user.IsActive = isActive;
        user.Status = isActive ? UserStatuses.Active : UserStatuses.Inactive;
        user.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {Action}: UserId {UserId}, CompanyId {CompanyId}, by AdminUserId {AdminUserId}.",
            isActive ? "activated" : "deactivated", id, companyId.Value, GetCurrentUserId());

        return Ok(new SimpleMessageDto { Message = isActive ? "User activated." : "User deactivated." });
    }

    // Reads the signed-in user's own UserId from their token (the "sub" claim, Step 15.2).
    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(PawsClaims.UserId)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }
}