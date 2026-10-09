using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.domain.Entities;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

// Reading breeds: any signed-in user. Writing breeds: Administrator and Staff only.
[Authorize]
[ApiController]
[Route("api/breeds")]
public class BreedsController : ControllerBase
{
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;
    private readonly ILogger<BreedsController> _logger;

    public BreedsController(
        ICurrentCompanyService currentCompany,
        ITenantDbContextFactory tenantFactory,
        ILogger<BreedsController> logger)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
        _logger = logger;
    }

    // GET /api/breeds
    // GET /api/breeds?categoryId=1
    // GET /api/breeds?includeInactive=true  (Administrator/Staff only)
    [HttpGet]
    public async Task<IActionResult> GetBreeds(int? categoryId, bool includeInactive, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        if (includeInactive && !IsAdministratorOrStaff())
        {
            return Forbid();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var query = db.Breeds.Include(b => b.PetCategory).AsQueryable();

        if (categoryId.HasValue)
        {
            query = query.Where(b => b.PetCategoryId == categoryId.Value);
        }

        if (!includeInactive)
        {
            query = query.Where(b => b.IsActive);
        }

        var breeds = await query
            .OrderBy(b => b.PetCategory!.CategoryName)
            .ThenBy(b => b.BreedName)
            .Select(b => new BreedDto
            {
                BreedId = b.BreedId,
                PetCategoryId = b.PetCategoryId,
                CategoryName = b.PetCategory!.CategoryName,
                BreedName = b.BreedName,
                Description = b.Description,
                IsActive = b.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(breeds);
    }

    // GET /api/breeds/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBreed(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var breed = await db.Breeds
            .Include(b => b.PetCategory)
            .Where(b => b.BreedId == id)
            .Select(b => new BreedDto
            {
                BreedId = b.BreedId,
                PetCategoryId = b.PetCategoryId,
                CategoryName = b.PetCategory!.CategoryName,
                BreedName = b.BreedName,
                Description = b.Description,
                IsActive = b.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (breed is null)
        {
            return NotFound();
        }

        return Ok(breed);
    }

    // POST /api/breeds
    [HttpPost]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> CreateBreed(CreateBreedRequestDto request, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var category = await db.PetCategories.FirstOrDefaultAsync(c => c.PetCategoryId == request.PetCategoryId, cancellationToken);
        if (category is null)
        {
            return BadRequest(new { message = "That pet category does not exist." });
        }

        var name = request.BreedName.Trim();
        var nameTaken = await db.Breeds.AnyAsync(
            b => b.PetCategoryId == request.PetCategoryId && b.BreedName == name, cancellationToken);
        if (nameTaken)
        {
            return BadRequest(new { message = "A breed with that name already exists in this category." });
        }

        var breed = new Breed
        {
            PetCategoryId = request.PetCategoryId,
            BreedName = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = true
        };

        db.Breeds.Add(breed);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Breed created: {BreedId} under CategoryId {CategoryId}, CompanyId {CompanyId}.",
            breed.BreedId, request.PetCategoryId, companyId.Value);

        return Ok(new BreedDto
        {
            BreedId = breed.BreedId,
            PetCategoryId = breed.PetCategoryId,
            CategoryName = category.CategoryName,
            BreedName = breed.BreedName,
            Description = breed.Description,
            IsActive = breed.IsActive
        });
    }

    // PUT /api/breeds/{id}
    [HttpPut("{id:int}")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> UpdateBreed(int id, UpdateBreedRequestDto request, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var breed = await db.Breeds.FirstOrDefaultAsync(b => b.BreedId == id, cancellationToken);
        if (breed is null)
        {
            return NotFound();
        }

        var category = await db.PetCategories.FirstOrDefaultAsync(c => c.PetCategoryId == request.PetCategoryId, cancellationToken);
        if (category is null)
        {
            return BadRequest(new { message = "That pet category does not exist." });
        }

        var name = request.BreedName.Trim();
        var nameTakenByAnother = await db.Breeds.AnyAsync(
            b => b.PetCategoryId == request.PetCategoryId && b.BreedName == name && b.BreedId != id, cancellationToken);
        if (nameTakenByAnother)
        {
            return BadRequest(new { message = "A breed with that name already exists in this category." });
        }

        breed.PetCategoryId = request.PetCategoryId;
        breed.BreedName = name;
        breed.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        breed.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Breed updated." });
    }

    // POST /api/breeds/{id}/deactivate
    [HttpPost("{id:int}/deactivate")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        return await SetActiveStatus(id, isActive: false, cancellationToken);
    }

    // POST /api/breeds/{id}/activate
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

        var breed = await db.Breeds.FirstOrDefaultAsync(b => b.BreedId == id, cancellationToken);
        if (breed is null)
        {
            return NotFound();
        }

        breed.IsActive = isActive;
        breed.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = isActive ? "Breed activated." : "Breed deactivated." });
    }

    private bool IsAdministratorOrStaff()
    {
        return User.IsInRole(RoleNames.Administrator) || User.IsInRole(RoleNames.Staff);
    }
}