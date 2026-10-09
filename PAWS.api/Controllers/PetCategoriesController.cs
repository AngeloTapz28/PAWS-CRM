using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.domain.Entities;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

// Reading categories: any signed-in user (Adopters need this to filter pets).
// Writing categories: Administrator and Staff only.
[Authorize]
[ApiController]
[Route("api/pet-categories")]
public class PetCategoriesController : ControllerBase
{
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;
    private readonly ILogger<PetCategoriesController> _logger;

    public PetCategoriesController(
        ICurrentCompanyService currentCompany,
        ITenantDbContextFactory tenantFactory,
        ILogger<PetCategoriesController> logger)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
        _logger = logger;
    }

    // GET /api/pet-categories
    // GET /api/pet-categories?includeInactive=true  (Administrator/Staff only, checked below)
    [HttpGet]
    public async Task<IActionResult> GetCategories(bool includeInactive, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        if (includeInactive && !IsAdministratorOrStaff())
        {
            // Only management roles may see deactivated categories.
            return Forbid();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var query = db.PetCategories.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        var categories = await query
            .OrderBy(c => c.CategoryName)
            .Select(c => new PetCategoryDto
            {
                PetCategoryId = c.PetCategoryId,
                CategoryName = c.CategoryName,
                Description = c.Description,
                IsActive = c.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    // GET /api/pet-categories/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCategory(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var category = await db.PetCategories
            .Where(c => c.PetCategoryId == id)
            .Select(c => new PetCategoryDto
            {
                PetCategoryId = c.PetCategoryId,
                CategoryName = c.CategoryName,
                Description = c.Description,
                IsActive = c.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        return Ok(category);
    }

    // POST /api/pet-categories
    [HttpPost]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> CreateCategory(CreatePetCategoryRequestDto request, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var name = request.CategoryName.Trim();
        var nameTaken = await db.PetCategories.AnyAsync(c => c.CategoryName == name, cancellationToken);
        if (nameTaken)
        {
            return BadRequest(new { message = "A category with that name already exists." });
        }

        var category = new PetCategory
        {
            CategoryName = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = true
        };

        db.PetCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Pet category created: {CategoryId} in CompanyId {CompanyId}.", category.PetCategoryId, companyId.Value);

        return Ok(new PetCategoryDto
        {
            PetCategoryId = category.PetCategoryId,
            CategoryName = category.CategoryName,
            Description = category.Description,
            IsActive = category.IsActive
        });
    }

    // PUT /api/pet-categories/{id}
    [HttpPut("{id:int}")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> UpdateCategory(int id, UpdatePetCategoryRequestDto request, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var category = await db.PetCategories.FirstOrDefaultAsync(c => c.PetCategoryId == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        var name = request.CategoryName.Trim();
        var nameTakenByAnother = await db.PetCategories
            .AnyAsync(c => c.CategoryName == name && c.PetCategoryId != id, cancellationToken);
        if (nameTakenByAnother)
        {
            return BadRequest(new { message = "A category with that name already exists." });
        }

        category.CategoryName = name;
        category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        category.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Category updated." });
    }

    // POST /api/pet-categories/{id}/deactivate
    [HttpPost("{id:int}/deactivate")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        return await SetActiveStatus(id, isActive: false, cancellationToken);
    }

    // POST /api/pet-categories/{id}/activate
    [HttpPost("{id:int}/activate")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
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

        var category = await db.PetCategories.FirstOrDefaultAsync(c => c.PetCategoryId == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        category.IsActive = isActive;
        category.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = isActive ? "Category activated." : "Category deactivated." });
    }

    private bool IsAdministratorOrStaff()
    {
        return User.IsInRole(RoleNames.Administrator) || User.IsInRole(RoleNames.Staff);
    }
}