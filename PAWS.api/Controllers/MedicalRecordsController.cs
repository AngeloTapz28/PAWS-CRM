using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.domain.Entities;
using PAWS.infrastructure.Data;
using PAWS.infrastructure.Services;
using static PAWS.domain.Constants.RequestStatuses;

namespace PAWS.api.Controllers;

[Authorize]
[ApiController]
[Route("api/medical-records")]
public class MedicalRecordsController : ControllerBase
{
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;

    public MedicalRecordsController(ICurrentCompanyService currentCompany, ITenantDbContextFactory tenantFactory)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
    }

    // GET /api/medical-records?petId=5&fitForAdoption=true
    [HttpGet]
    public async Task<IActionResult> GetRecords(int? petId, bool? fitForAdoption, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var query = db.MedicalRecords
            .Include(r => r.Pet)
            .Include(r => r.Vet)
            .Where(r => r.DeletedAt == null)
            .AsQueryable();

        if (IsAdopterOnly())
        {
            var ownedPetIds = await GetAdoptedPetIdsAsync(db, GetCurrentUserId(), cancellationToken);
            query = query.Where(r => ownedPetIds.Contains(r.PetId));
        }

        if (petId.HasValue)
        {
            query = query.Where(r => r.PetId == petId.Value);
        }

        if (fitForAdoption == true)
        {
            query = query.Where(r => r.FitForAdoption);
        }

        var records = await query
            .OrderByDescending(r => r.ExaminationDate)
            .Select(r => ToDto(r))
            .ToListAsync(cancellationToken);

        return Ok(records);
    }

    // GET /api/medical-records/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var record = await db.MedicalRecords
            .Include(r => r.Pet)
            .Include(r => r.Vet)
            .FirstOrDefaultAsync(r => r.MedicalRecordId == id && r.DeletedAt == null, cancellationToken);

        if (record is null) return NotFound();

        if (IsAdopterOnly())
        {
            var ownedPetIds = await GetAdoptedPetIdsAsync(db, GetCurrentUserId(), cancellationToken);
            if (!ownedPetIds.Contains(record.PetId))
            {
                return Forbid();
            }
        }

        return Ok(ToDto(record));
    }

    // POST /api/medical-records
    [HttpPost]
    [Authorize(Roles = RoleNames.Veterinarian)]
    public async Task<IActionResult> Create(CreateMedicalRecordRequestDto dto, CancellationToken cancellationToken)
    {
        if (!HealthStatuses.All.Contains(dto.HealthStatus))
        {
            return BadRequest(new { message = $"HealthStatus must be one of: {string.Join(", ", HealthStatuses.All)}." });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.PetId == dto.PetId, cancellationToken);
        if (pet is null)
        {
            return BadRequest(new { message = "That pet does not exist." });
        }

        var record = new MedicalRecord
        {
            PetId = dto.PetId,
            VetId = GetCurrentUserId(),
            ExaminationDate = dto.ExaminationDate,
            Diagnosis = NullIfBlank(dto.Diagnosis),
            Symptoms = NullIfBlank(dto.Symptoms),
            Treatment = NullIfBlank(dto.Treatment),
            Medications = NullIfBlank(dto.Medications),
            WeightAtExam = dto.WeightAtExam,
            HealthStatus = dto.HealthStatus,
            FitForAdoption = dto.FitForAdoption,
            Notes = NullIfBlank(dto.Notes),
            NextCheckupDate = dto.NextCheckupDate
        };

        db.MedicalRecords.Add(record);

        if (record.FitForAdoption)
        {
            await NotificationHelper.NotifyAdminsAndStaffAsync(db,
                "Pet Cleared for Adoption",
                $"{pet.PetName} has been medically cleared and is fit for adoption.",
                "success", $"pets/{pet.PetId}", cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Medical record added." });
    }

    // PUT /api/medical-records/{id}
    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleNames.Veterinarian)]
    public async Task<IActionResult> Update(int id, UpdateMedicalRecordRequestDto dto, CancellationToken cancellationToken)
    {
        if (!HealthStatuses.All.Contains(dto.HealthStatus))
        {
            return BadRequest(new { message = $"HealthStatus must be one of: {string.Join(", ", HealthStatuses.All)}." });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null) return Unauthorized();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var record = await db.MedicalRecords.FirstOrDefaultAsync(
            r => r.MedicalRecordId == id && r.DeletedAt == null, cancellationToken);
        if (record is null) return NotFound();

        var wasFitForAdoption = record.FitForAdoption;

        record.ExaminationDate = dto.ExaminationDate;
        record.Diagnosis = NullIfBlank(dto.Diagnosis);
        record.Symptoms = NullIfBlank(dto.Symptoms);
        record.Treatment = NullIfBlank(dto.Treatment);
        record.Medications = NullIfBlank(dto.Medications);
        record.WeightAtExam = dto.WeightAtExam;
        record.HealthStatus = dto.HealthStatus;
        record.FitForAdoption = dto.FitForAdoption;
        record.Notes = NullIfBlank(dto.Notes);
        record.NextCheckupDate = dto.NextCheckupDate;
        record.UpdatedAt = DateTime.UtcNow;

        if (!wasFitForAdoption && record.FitForAdoption)
        {
            var pet = await db.Pets.FirstOrDefaultAsync(p => p.PetId == record.PetId, cancellationToken);
            if (pet is not null)
            {
                await NotificationHelper.NotifyAdminsAndStaffAsync(db,
                    "Pet Cleared for Adoption",
                    $"{pet.PetName} has been medically cleared and is fit for adoption.",
                    "success", $"pets/{pet.PetId}", cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Medical record updated." });
    }

    // ----- Helpers -----

    internal static async Task<List<int>> GetAdoptedPetIdsAsync(TenantErpDbContext db, int userId, CancellationToken cancellationToken)
    {
        return await db.AdoptionApplications
            .Where(a => a.UserId == userId &&
                        (a.ApplicationStatus == ApplicationStatuses.Approved || a.ApplicationStatus == ApplicationStatuses.Completed))
            .Select(a => a.PetId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private bool IsAdopterOnly() =>
        User.IsInRole(RoleNames.Adopter) &&
        !User.IsInRole(RoleNames.Administrator) &&
        !User.IsInRole(RoleNames.Staff) &&
        !User.IsInRole(RoleNames.Veterinarian);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static MedicalRecordDto ToDto(MedicalRecord r) => new()
    {
        MedicalRecordId = r.MedicalRecordId,
        PetId = r.PetId,
        PetName = r.Pet?.PetName ?? string.Empty,
        VetId = r.VetId,
        VetName = r.Vet?.FullName ?? string.Empty,
        ExaminationDate = r.ExaminationDate,
        Diagnosis = r.Diagnosis,
        Symptoms = r.Symptoms,
        Treatment = r.Treatment,
        Medications = r.Medications,
        WeightAtExam = r.WeightAtExam,
        HealthStatus = r.HealthStatus,
        FitForAdoption = r.FitForAdoption,
        Notes = r.Notes,
        NextCheckupDate = r.NextCheckupDate,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(PawsClaims.UserId)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }
}