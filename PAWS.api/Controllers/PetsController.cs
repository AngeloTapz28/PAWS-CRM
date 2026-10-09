using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.domain.Entities;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

[Authorize]
[ApiController]
[Route("api/pets")]
public class PetsController : ControllerBase
{
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;
    private readonly ILogger<PetsController> _logger;

    public PetsController(
        ICurrentCompanyService currentCompany,
        ITenantDbContextFactory tenantFactory,
        ILogger<PetsController> logger)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
        _logger = logger;
    }

    // GET /api/pets
    // GET /api/pets?status=Pending
    // GET /api/pets?categoryId=1
    // GET /api/pets?branchId=2
    // Adopters ALWAYS see only Available pets, regardless of any status filter they pass.
    // Administrator/Staff see everything, optionally filtered.
    // Veterinarian defaults to Pending (their review queue), unless a status is given.
    [HttpGet]
    public async Task<IActionResult> GetPets(string? status, int? categoryId, int? branchId, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var query = db.Pets
            .Include(p => p.PetCategory)
            .Include(p => p.Breed)
            .Include(p => p.Branch)
            .Include(p => p.AddedByUser)
            .Include(p => p.VetApprovedByUser)
            .Include(p => p.AdminApprovedByUser)
            .Where(p => p.IsActive)
            .AsQueryable();

        if (IsAdopter() && !IsAdministratorOrStaff())
        {
            // Adopters can NEVER see anything but Available pets, no matter what
            // they pass in "status". This is enforced here, not just skipped.
            query = query.Where(p => p.Status == PetStatuses.Available);
        }
        else if (IsVeterinarian() && !IsAdministratorOrStaff() && string.IsNullOrEmpty(status))
        {
            // A Veterinarian with no explicit filter sees their review queue by default.
            query = query.Where(p => p.Status == PetStatuses.Pending);
        }
        else if (!string.IsNullOrEmpty(status))
        {
            if (!PetStatuses.All.Contains(status))
            {
                return BadRequest(new { message = $"Status must be one of: {string.Join(", ", PetStatuses.All)}." });
            }
            query = query.Where(p => p.Status == status);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.PetCategoryId == categoryId.Value);
        }

        if (branchId.HasValue)
        {
            query = query.Where(p => p.BranchId == branchId.Value);
        }

        var pets = await query
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => ToDto(p))
            .ToListAsync(cancellationToken);

        return Ok(pets);
    }

    // GET /api/pets/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetPet(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var pet = await db.Pets
            .Include(p => p.PetCategory)
            .Include(p => p.Breed)
            .Include(p => p.Branch)
            .Include(p => p.AddedByUser)
            .Include(p => p.VetApprovedByUser)
            .Include(p => p.AdminApprovedByUser)
            .FirstOrDefaultAsync(p => p.PetId == id, cancellationToken);

        if (pet is null || !pet.IsActive)
        {
            return NotFound();
        }

        if (IsAdopter() && !IsAdministratorOrStaff() && pet.Status != PetStatuses.Available)
        {
            // An Adopter cannot view a specific pet by ID either, if it is not Available.
            return NotFound();
        }

        return Ok(ToDto(pet));
    }

    // POST /api/pets
    [HttpPost]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> CreatePet(CreatePetRequestDto request, CancellationToken cancellationToken)
    {
        var imageError = ValidateImagePath(request.ImagePath);
        if (imageError is not null)
        {
            return BadRequest(new { message = imageError });
        }

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

        var breed = await db.Breeds.FirstOrDefaultAsync(
            b => b.BreedId == request.BreedId && b.PetCategoryId == request.PetCategoryId, cancellationToken);
        if (breed is null)
        {
            return BadRequest(new { message = "That breed does not exist under the selected category." });
        }

        var branch = await db.Branches.FirstOrDefaultAsync(b => b.BranchId == request.BranchId, cancellationToken);
        if (branch is null)
        {
            return BadRequest(new { message = "That branch does not exist." });
        }

        var pet = new Pet
        {
            PetCategoryId = request.PetCategoryId,
            BreedId = request.BreedId,
            BranchId = request.BranchId,
            PetName = request.PetName.Trim(),
            Gender = NullIfBlank(request.Gender),
            Age = request.Age,
            Size = NullIfBlank(request.Size),
            Color = NullIfBlank(request.Color),
            Description = NullIfBlank(request.Description),
            ImagePath = NullIfBlank(request.ImagePath),
            AdoptionFee = request.AdoptionFee,
            AdoptionFeeType = NullIfBlank(request.AdoptionFeeType),
            Status = PetStatuses.Pending,
            IsVetApproved = false,
            IsAdminApproved = false,
            AddedBy = GetCurrentUserId(),
            IsActive = true
        };

        db.Pets.Add(pet);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Pet created: {PetId} in CompanyId {CompanyId}.", pet.PetId, companyId.Value);

        return Ok(new SimpleMessageDto { Message = "Pet added. It is now Pending vet and admin approval." });
    }

    // PUT /api/pets/{id}
    [HttpPut("{id:int}")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> UpdatePet(int id, UpdatePetRequestDto request, CancellationToken cancellationToken)
    {
        var imageError = ValidateImagePath(request.ImagePath);
        if (imageError is not null)
        {
            return BadRequest(new { message = imageError });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.PetId == id, cancellationToken);
        if (pet is null)
        {
            return NotFound();
        }

        var breed = await db.Breeds.FirstOrDefaultAsync(
            b => b.BreedId == request.BreedId && b.PetCategoryId == request.PetCategoryId, cancellationToken);
        if (breed is null)
        {
            return BadRequest(new { message = "That breed does not exist under the selected category." });
        }

        var branch = await db.Branches.FirstOrDefaultAsync(b => b.BranchId == request.BranchId, cancellationToken);
        if (branch is null)
        {
            return BadRequest(new { message = "That branch does not exist." });
        }

        pet.PetCategoryId = request.PetCategoryId;
        pet.BreedId = request.BreedId;
        pet.BranchId = request.BranchId;
        pet.PetName = request.PetName.Trim();
        pet.Gender = NullIfBlank(request.Gender);
        pet.Age = request.Age;
        pet.Size = NullIfBlank(request.Size);
        pet.Color = NullIfBlank(request.Color);
        pet.Description = NullIfBlank(request.Description);
        pet.ImagePath = NullIfBlank(request.ImagePath);
        pet.AdoptionFee = request.AdoptionFee;
        pet.AdoptionFeeType = NullIfBlank(request.AdoptionFeeType);
        pet.UpdatedAt = DateTime.UtcNow;

        // Editing a pet's details does not change its approval status.
        // (Re-approval is a separate, explicit action.)

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Pet updated." });
    }

    // POST /api/pets/{id}/vet-approve
    [HttpPost("{id:int}/vet-approve")]
    [Authorize(Roles = RoleNames.Veterinarian)]
    public async Task<IActionResult> VetApprove(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.PetId == id, cancellationToken);
        if (pet is null)
        {
            return NotFound();
        }

        pet.IsVetApproved = true;
        pet.VetApprovedBy = GetCurrentUserId();
        pet.VetNotes = null;
        RecomputeStatus(pet);
        pet.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Pet {PetId} vet-approved by UserId {UserId}.", id, GetCurrentUserId());

        return Ok(new SimpleMessageDto { Message = $"Vet approval given. Pet status: {pet.Status}." });
    }

    // POST /api/pets/{id}/vet-reject
    [HttpPost("{id:int}/vet-reject")]
    [Authorize(Roles = RoleNames.Veterinarian)]
    public async Task<IActionResult> VetReject(int id, RejectPetRequestDto request, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.PetId == id, cancellationToken);
        if (pet is null)
        {
            return NotFound();
        }

        pet.IsVetApproved = false;
        pet.VetApprovedBy = null;
        pet.VetNotes = request.VetNotes.Trim();
        RecomputeStatus(pet);
        pet.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Vet approval withheld. The pet remains Pending." });
    }

    // POST /api/pets/{id}/admin-approve
    // Requires vet approval to already be in place. Enforces the confirmed order:
    // vet approves first, then admin/staff approves.
    [HttpPost("{id:int}/admin-approve")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> AdminApprove(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.PetId == id, cancellationToken);
        if (pet is null)
        {
            return NotFound();
        }

        if (!pet.IsVetApproved)
        {
            return BadRequest(new { message = "This pet needs veterinary approval before it can be admin-approved." });
        }

        pet.IsAdminApproved = true;
        pet.AdminApprovedBy = GetCurrentUserId();
        RecomputeStatus(pet);
        pet.UpdatedAt = DateTime.UtcNow;

        if (pet.Status == PetStatuses.Available && pet.ListedAt is null)
        {
            pet.ListedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Pet {PetId} admin-approved by UserId {UserId}.", id, GetCurrentUserId());

        return Ok(new SimpleMessageDto { Message = $"Listing approval given. Pet status: {pet.Status}." });
    }

    // POST /api/pets/{id}/set-status
    // For "Under Treatment" or "Unavailable" only. "Available" and "Adopted" are
    // never set through this endpoint (Available is computed; Adopted is Phase 17).
    [HttpPost("{id:int}/set-status")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff},{RoleNames.Veterinarian}")]
    public async Task<IActionResult> SetStatus(int id, SetPetStatusRequestDto request, CancellationToken cancellationToken)
    {
        if (request.Status != PetStatuses.UnderTreatment && request.Status != PetStatuses.Unavailable)
        {
            return BadRequest(new
            {
                message = $"This endpoint only accepts '{PetStatuses.UnderTreatment}' or '{PetStatuses.Unavailable}'."
            });
        }

        // Only Administrator/Staff may mark a pet Unavailable; a Veterinarian may
        // only move a pet into Under Treatment.
        if (request.Status == PetStatuses.Unavailable && !IsAdministratorOrStaff())
        {
            return Forbid();
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.PetId == id, cancellationToken);
        if (pet is null)
        {
            return NotFound();
        }

        if (pet.Status == PetStatuses.Adopted)
        {
            return BadRequest(new { message = "An adopted pet's status cannot be changed here." });
        }

        pet.Status = request.Status;

        if (request.Status == PetStatuses.UnderTreatment)
        {
            // Medical fact is now in question; the business decision (IsAdminApproved)
            // is untouched, per the confirmed design.
            pet.IsVetApproved = false;
            pet.VetApprovedBy = null;
        }

        pet.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = $"Pet status set to {pet.Status}." });
    }

    // POST /api/pets/{id}/deactivate
    [HttpPost("{id:int}/deactivate")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.PetId == id, cancellationToken);
        if (pet is null)
        {
            return NotFound();
        }

        pet.IsActive = false;
        pet.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Pet deactivated." });
    }

    // ----- Helpers -----

    // The ONE place that turns the two approval flags into the Status text.
    // Never called when the pet is already Under Treatment, Unavailable, or
    // Adopted — those are separate, explicit states outside the approval flow.
    // The ONE place that turns the two approval flags into the Status text.
    // "Unavailable" and "Adopted" are deliberate end-states that an approval
    // action must never silently override. "Under Treatment" is NOT protected
    // here: it is meant to clear itself automatically once vet approval is
    // restored (that is the whole point of the reset in SetStatus).
    private static void RecomputeStatus(Pet pet)
    {
        if (pet.Status == PetStatuses.Unavailable ||
            pet.Status == PetStatuses.Adopted)
        {
            return;
        }

        pet.Status = (pet.IsVetApproved && pet.IsAdminApproved)
            ? PetStatuses.Available
            : PetStatuses.Pending;
    }

    private static string? ValidateImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        if (!imagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !imagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "Image path must start with http:// or https://.";
        }

        return null;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PetDto ToDto(Pet p) => new()
    {
        PetId = p.PetId,
        PetCategoryId = p.PetCategoryId,
        CategoryName = p.PetCategory?.CategoryName ?? string.Empty,
        BreedId = p.BreedId,
        BreedName = p.Breed?.BreedName ?? string.Empty,
        BranchId = p.BranchId,
        BranchName = p.Branch?.BranchName ?? string.Empty,
        PetName = p.PetName,
        Gender = p.Gender,
        Age = p.Age,
        Size = p.Size,
        Color = p.Color,
        Description = p.Description,
        ImagePath = p.ImagePath,
        AdoptionFee = p.AdoptionFee,
        AdoptionFeeType = p.AdoptionFeeType,
        Status = p.Status,
        IsVetApproved = p.IsVetApproved,
        IsAdminApproved = p.IsAdminApproved,
        VetNotes = p.VetNotes,
        AddedByName = p.AddedByUser?.FullName,
        VetApprovedByName = p.VetApprovedByUser?.FullName,
        AdminApprovedByName = p.AdminApprovedByUser?.FullName,
        ListedAt = p.ListedAt,
        IsActive = p.IsActive,
        CreatedAt = p.CreatedAt
    };

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(PawsClaims.UserId)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }

    private bool IsAdministratorOrStaff() =>
        User.IsInRole(RoleNames.Administrator) || User.IsInRole(RoleNames.Staff);

    private bool IsVeterinarian() => User.IsInRole(RoleNames.Veterinarian);

    private bool IsAdopter() => User.IsInRole(RoleNames.Adopter);
}