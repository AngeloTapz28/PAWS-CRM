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
[Route("api/vaccination-records")]
public class VaccinationRecordsController : ControllerBase
{
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;

    public VaccinationRecordsController(ICurrentCompanyService currentCompany, ITenantDbContextFactory tenantFactory)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
    }

    // GET /api/vaccination-records?petId=5
    [HttpGet]
    public async Task<IActionResult> GetRecords(int? petId, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var query = db.VaccinationRecords
            .Include(r => r.Pet)
            .Include(r => r.AdministeredByUser)
            .AsQueryable();

        if (IsAdopterOnly())
        {
            var ownedPetIds = await MedicalRecordsController.GetAdoptedPetIdsAsync(db, GetCurrentUserId(), cancellationToken);
            query = query.Where(r => ownedPetIds.Contains(r.PetId));
        }

        if (petId.HasValue)
        {
            query = query.Where(r => r.PetId == petId.Value);
        }

        var records = await query
            .OrderByDescending(r => r.DateAdministered)
            .Select(r => ToDto(r))
            .ToListAsync(cancellationToken);

        return Ok(records);
    }

    // GET /api/vaccination-records/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var record = await db.VaccinationRecords
            .Include(r => r.Pet)
            .Include(r => r.AdministeredByUser)
            .FirstOrDefaultAsync(r => r.VaccinationRecordId == id, cancellationToken);

        if (record is null) return NotFound();

        if (IsAdopterOnly())
        {
            var ownedPetIds = await MedicalRecordsController.GetAdoptedPetIdsAsync(db, GetCurrentUserId(), cancellationToken);
            if (!ownedPetIds.Contains(record.PetId))
            {
                return Forbid();
            }
        }

        return Ok(ToDto(record));
    }

    // POST /api/vaccination-records
    [HttpPost]
    [Authorize(Roles = RoleNames.Veterinarian)]
    public async Task<IActionResult> Create(CreateVaccinationRecordRequestDto dto, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.PetId == dto.PetId, cancellationToken);
        if (pet is null)
        {
            return BadRequest(new { message = "That pet does not exist." });
        }

        var record = new VaccinationRecord
        {
            PetId = dto.PetId,
            AdministeredBy = GetCurrentUserId(),
            VaccineName = dto.VaccineName.Trim(),
            Manufacturer = NullIfBlank(dto.Manufacturer),
            BatchNumber = NullIfBlank(dto.BatchNumber),
            DateAdministered = dto.DateAdministered,
            NextDueDate = dto.NextDueDate,
            IsBooster = dto.IsBooster,
            Notes = NullIfBlank(dto.Notes)
        };

        db.VaccinationRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Vaccination record added." });
    }

    // PUT /api/vaccination-records/{id}
    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleNames.Veterinarian)]
    public async Task<IActionResult> Update(int id, UpdateVaccinationRecordRequestDto dto, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var record = await db.VaccinationRecords.FirstOrDefaultAsync(r => r.VaccinationRecordId == id, cancellationToken);
        if (record is null) return NotFound();

        record.VaccineName = dto.VaccineName.Trim();
        record.Manufacturer = NullIfBlank(dto.Manufacturer);
        record.BatchNumber = NullIfBlank(dto.BatchNumber);
        record.DateAdministered = dto.DateAdministered;
        record.NextDueDate = dto.NextDueDate;
        record.IsBooster = dto.IsBooster;
        record.Notes = NullIfBlank(dto.Notes);
        record.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Vaccination record updated." });
    }

    // ----- Helpers -----

    private bool IsAdopterOnly() =>
        User.IsInRole(RoleNames.Adopter) &&
        !User.IsInRole(RoleNames.Administrator) &&
        !User.IsInRole(RoleNames.Staff) &&
        !User.IsInRole(RoleNames.Veterinarian);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CalculateDueStatus(DateTime? nextDueDate)
    {
        if (nextDueDate is null) return "None Scheduled";
        var daysUntil = (nextDueDate.Value.Date - DateTime.UtcNow.Date).TotalDays;
        return daysUntil < 0 ? "Overdue" : "Upcoming";
    }

    private static VaccinationRecordDto ToDto(VaccinationRecord r) => new()
    {
        VaccinationRecordId = r.VaccinationRecordId,
        PetId = r.PetId,
        PetName = r.Pet?.PetName ?? string.Empty,
        AdministeredBy = r.AdministeredBy,
        AdministeredByName = r.AdministeredByUser?.FullName ?? string.Empty,
        VaccineName = r.VaccineName,
        Manufacturer = r.Manufacturer,
        BatchNumber = r.BatchNumber,
        DateAdministered = r.DateAdministered,
        NextDueDate = r.NextDueDate,
        IsBooster = r.IsBooster,
        Notes = r.Notes,
        DueStatus = CalculateDueStatus(r.NextDueDate),
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(PawsClaims.UserId)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }
}