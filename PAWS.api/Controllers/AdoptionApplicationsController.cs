using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PAWS.api.Dtos;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.domain.Entities;
using PAWS.infrastructure.Data;
using PAWS.infrastructure.Services;

namespace PAWS.api.Controllers;

[Authorize]
[ApiController]
[Route("api/adoption-applications")]
public class AdoptionApplicationsController : ControllerBase
{
    private readonly ICurrentCompanyService _currentCompany;
    private readonly ITenantDbContextFactory _tenantFactory;
    private readonly ILogger<AdoptionApplicationsController> _logger;

    public AdoptionApplicationsController(
        ICurrentCompanyService currentCompany,
        ITenantDbContextFactory tenantFactory,
        ILogger<AdoptionApplicationsController> logger)
    {
        _currentCompany = currentCompany;
        _tenantFactory = tenantFactory;
        _logger = logger;
    }

    // GET /api/adoption-applications
    // GET /api/adoption-applications?status=Submitted
    [HttpGet]
    public async Task<IActionResult> GetApplications(string? status, CancellationToken cancellationToken)
    {
        if (IsVeterinarian() && !IsAdministratorOrStaff())
        {
            return Forbid();
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var query = db.AdoptionApplications
            .Include(a => a.Pet)
            .Include(a => a.User)
            .Include(a => a.Reviewer)
            .Where(a => a.DeletedAt == null)
            .AsQueryable();

        if (IsAdopter() && !IsAdministratorOrStaff())
        {
            query = query.Where(a => a.UserId == GetCurrentUserId());
        }

        if (!string.IsNullOrEmpty(status))
        {
            if (!ApplicationStatuses.All.Contains(status))
            {
                return BadRequest(new { message = $"Status must be one of: {string.Join(", ", ApplicationStatuses.All)}." });
            }
            query = query.Where(a => a.ApplicationStatus == status);
        }

        var applications = await query
            .OrderByDescending(a => a.AppliedAt)
            .Select(a => ToDto(a))
            .ToListAsync(cancellationToken);

        return Ok(applications);
    }

    // GET /api/adoption-applications/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetApplication(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var application = await db.AdoptionApplications
            .Include(a => a.Pet)
            .Include(a => a.User)
            .Include(a => a.Reviewer)
            .FirstOrDefaultAsync(a => a.AdoptionApplicationId == id && a.DeletedAt == null, cancellationToken);

        if (application is null)
        {
            return NotFound();
        }

        var isOwnApplication = IsAdopter() && application.UserId == GetCurrentUserId();

        if (!isOwnApplication && !IsAdministratorOrStaff())
        {
            return Forbid();
        }

        return Ok(ToDto(application));
    }

    // POST /api/adoption-applications
    [HttpPost]
    [Authorize(Roles = RoleNames.Adopter)]
    public async Task<IActionResult> SubmitApplication(SubmitAdoptionApplicationRequestDto request, CancellationToken cancellationToken)
    {
        if (request.DonationAmount.HasValue && request.DonationAmount.Value <= 0)
        {
            return BadRequest(new { message = "Donation amount must be greater than zero." });
        }

        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        var currentUserId = GetCurrentUserId();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.PetId == request.PetId && p.IsActive, cancellationToken);
        if (pet is null)
        {
            return BadRequest(new { message = "That pet does not exist." });
        }

        if (pet.Status != PetStatuses.Available)
        {
            return BadRequest(new { message = "This pet is not currently available for adoption." });
        }

        var hasOpenApplication = await db.AdoptionApplications.AnyAsync(
            a => a.PetId == request.PetId
              && a.UserId == currentUserId
              && a.DeletedAt == null
              && (a.ApplicationStatus == ApplicationStatuses.Submitted
                  || a.ApplicationStatus == ApplicationStatuses.UnderReview),
            cancellationToken);

        if (hasOpenApplication)
        {
            return BadRequest(new { message = "You already have an open application for this pet." });
        }

        var applicationNumber = await GenerateApplicationNumberAsync(db, cancellationToken);

        var application = new AdoptionApplication
        {
            ApplicationNumber = applicationNumber,
            PetId = request.PetId,
            UserId = currentUserId,
            FullName = NullIfBlank(request.FullName),
            Email = NullIfBlank(request.Email),
            Phone = NullIfBlank(request.Phone),
            Address = NullIfBlank(request.Address),
            Occupation = NullIfBlank(request.Occupation),
            HousingType = NullIfBlank(request.HousingType),
            HomeDescription = NullIfBlank(request.HomeDescription),
            HasYard = request.HasYard,
            HasOtherPets = request.HasOtherPets,
            OtherPetsDetails = NullIfBlank(request.OtherPetsDetails),
            HasChildren = request.HasChildren,
            ChildrenDetails = NullIfBlank(request.ChildrenDetails),
            WorkingHours = NullIfBlank(request.WorkingHours),
            ReasonForAdopting = NullIfBlank(request.ReasonForAdopting),
            PetExperience = NullIfBlank(request.PetExperience),
            EmergencyContactName = NullIfBlank(request.EmergencyContactName),
            EmergencyContactPhone = NullIfBlank(request.EmergencyContactPhone),
            EmergencyContactRelationship = NullIfBlank(request.EmergencyContactRelationship),
            ApplicationStatus = ApplicationStatuses.Submitted
        };

        db.AdoptionApplications.Add(application);
        await db.SaveChangesAsync(cancellationToken);

        // Optional donation offered alongside the application. Created as a
        // Pending payment; Staff/Admin confirm it the same way as any other
        // payment, via the existing Payments page. No special donation
        // workflow beyond this — it reuses Payment's normal lifecycle.
        string? donationMessage = null;
        if (request.DonationAmount.HasValue)
        {
            var paymentCount = await db.Payments.CountAsync(cancellationToken);
            var payment = new Payment
            {
                ReferenceNumber = $"PAY-{DateTime.UtcNow.Year}-{paymentCount + 1:D6}",
                AdoptionApplicationId = application.AdoptionApplicationId,
                Type = "Donation",
                Amount = request.DonationAmount.Value,
                Currency = "PHP",
                Method = "Other",
                Status = PaymentStatuses.Pending,
                Notes = "Donation offered during adoption application submission.",
                PayerId = currentUserId
            };

            db.Payments.Add(payment);
            await db.SaveChangesAsync(cancellationToken);

            donationMessage = $" A donation of \u20b1{request.DonationAmount.Value:N2} ({payment.ReferenceNumber}) has also been recorded, pending confirmation.";
        }

        _logger.LogInformation("Application {ApplicationNumber} submitted by UserId {UserId} for PetId {PetId}.",
            applicationNumber, currentUserId, request.PetId);

        return Ok(new SimpleMessageDto { Message = $"Application {applicationNumber} submitted.{donationMessage}" });
    }

    // POST /api/adoption-applications/{id}/withdraw
    [HttpPost("{id:int}/withdraw")]
    [Authorize(Roles = RoleNames.Adopter)]
    public async Task<IActionResult> Withdraw(int id, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var application = await db.AdoptionApplications
            .FirstOrDefaultAsync(a => a.AdoptionApplicationId == id && a.DeletedAt == null, cancellationToken);

        if (application is null)
        {
            return NotFound();
        }

        if (application.UserId != GetCurrentUserId())
        {
            return Forbid();
        }

        if (application.ApplicationStatus != ApplicationStatuses.Submitted &&
            application.ApplicationStatus != ApplicationStatuses.UnderReview)
        {
            return BadRequest(new { message = "Only an open application (Submitted or Under Review) can be withdrawn." });
        }

        application.ApplicationStatus = ApplicationStatuses.Withdrawn;
        application.ReviewedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Application withdrawn." });
    }

    // POST /api/adoption-applications/{id}/under-review
    [HttpPost("{id:int}/under-review")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> SetUnderReview(int id, SetUnderReviewRequestDto request, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var application = await db.AdoptionApplications
            .FirstOrDefaultAsync(a => a.AdoptionApplicationId == id && a.DeletedAt == null, cancellationToken);

        if (application is null)
        {
            return NotFound();
        }

        if (application.ApplicationStatus != ApplicationStatuses.Submitted)
        {
            return BadRequest(new { message = "Only a Submitted application can be moved to Under Review." });
        }

        application.ApplicationStatus = ApplicationStatuses.UnderReview;
        application.ReviewedBy = GetCurrentUserId();
        application.ReviewedAt = DateTime.UtcNow;
        application.Remarks = NullIfBlank(request.Remarks);

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Application moved to Under Review." });
    }

    // POST /api/adoption-applications/{id}/schedule-interview
    [HttpPost("{id:int}/schedule-interview")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> ScheduleInterview(int id, ScheduleInterviewRequestDto request, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var application = await db.AdoptionApplications
            .FirstOrDefaultAsync(a => a.AdoptionApplicationId == id && a.DeletedAt == null, cancellationToken);

        if (application is null)
        {
            return NotFound();
        }

        if (application.ApplicationStatus != ApplicationStatuses.UnderReview)
        {
            return BadRequest(new { message = "An interview can only be scheduled for an application that is Under Review." });
        }

        application.InterviewDate = request.InterviewDate;
        application.InterviewNotes = NullIfBlank(request.InterviewNotes);

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Interview scheduled." });
    }

    // POST /api/adoption-applications/{id}/approve
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> Approve(int id, ApproveApplicationRequestDto request, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        var reviewerId = GetCurrentUserId();

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var application = await db.AdoptionApplications
            .FirstOrDefaultAsync(a => a.AdoptionApplicationId == id && a.DeletedAt == null, cancellationToken);

        if (application is null)
        {
            return NotFound();
        }

        if (application.ApplicationStatus != ApplicationStatuses.Submitted &&
            application.ApplicationStatus != ApplicationStatuses.UnderReview)
        {
            return BadRequest(new { message = "Only a Submitted or Under Review application can be approved." });
        }

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.PetId == application.PetId, cancellationToken);
        if (pet is null)
        {
            throw new InvalidOperationException($"Pet {application.PetId} referenced by AdoptionApplication {id} was not found.");
        }

        var now = DateTime.UtcNow;

        // Approve the winning application.
        application.ApplicationStatus = ApplicationStatuses.Approved;
        application.ReviewedBy = reviewerId;
        application.ReviewedAt = now;
        application.Remarks = NullIfBlank(request.Remarks);

        // The pet is no longer available.
        pet.Status = PetStatuses.Adopted;
        pet.UpdatedAt = now;

        NotificationHelper.Create(db, application.UserId,
            "Application Approved",
            $"Your application {application.ApplicationNumber} for {pet.PetName} has been approved!",
            "success", $"applications/{application.AdoptionApplicationId}");

        // Auto-reject every other open application for this same pet.
        var otherOpenApplications = await db.AdoptionApplications
            .Where(a => a.PetId == application.PetId
                     && a.AdoptionApplicationId != id
                     && a.DeletedAt == null
                     && (a.ApplicationStatus == ApplicationStatuses.Submitted
                         || a.ApplicationStatus == ApplicationStatuses.UnderReview))
            .ToListAsync(cancellationToken);

        foreach (var other in otherOpenApplications)
        {
            other.ApplicationStatus = ApplicationStatuses.Rejected;
            other.RejectionReason = "Pet was adopted by another applicant.";
            other.ReviewedBy = reviewerId;
            other.ReviewedAt = now;

            NotificationHelper.Create(db, other.UserId,
                "Application Update",
                $"Your application {other.ApplicationNumber} for {pet.PetName} was not approved. Reason: {other.RejectionReason}",
                "warning", $"applications/{other.AdoptionApplicationId}");

            db.ActivityLogs.Add(new ActivityLog
            {
                UserId = reviewerId,
                Action = "Auto-Rejected Application",
                ModelType = "AdoptionApplication",
                ModelId = other.AdoptionApplicationId,
                Description = $"Application {other.ApplicationNumber} was automatically rejected because " +
                               $"application {application.ApplicationNumber} for the same pet (PetId {pet.PetId}) " +
                               $"was approved by UserId {reviewerId}. This was a system side effect, not a " +
                               "manual review of this specific application.",
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Application {ApplicationNumber} approved by UserId {ReviewerId}. Pet {PetId} set to Adopted. " +
            "{Count} other application(s) auto-rejected.",
            application.ApplicationNumber, reviewerId, pet.PetId, otherOpenApplications.Count);

        return Ok(new SimpleMessageDto
        {
            Message = $"Application approved. Pet marked as Adopted. " +
                      $"{otherOpenApplications.Count} other application(s) for this pet were automatically rejected."
        });
    }

    // POST /api/adoption-applications/{id}/reject
    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> Reject(int id, RejectApplicationRequestDto request, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var application = await db.AdoptionApplications
            .FirstOrDefaultAsync(a => a.AdoptionApplicationId == id && a.DeletedAt == null, cancellationToken);

        if (application is null)
        {
            return NotFound();
        }

        if (application.ApplicationStatus != ApplicationStatuses.Submitted &&
            application.ApplicationStatus != ApplicationStatuses.UnderReview)
        {
            return BadRequest(new { message = "Only a Submitted or Under Review application can be rejected." });
        }

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.PetId == application.PetId, cancellationToken);

        application.ApplicationStatus = ApplicationStatuses.Rejected;
        application.RejectionReason = request.RejectionReason.Trim();
        application.ReviewedBy = GetCurrentUserId();
        application.ReviewedAt = DateTime.UtcNow;

        NotificationHelper.Create(db, application.UserId,
            "Application Update",
            $"Your application {application.ApplicationNumber} for {pet?.PetName} was not approved. Reason: {application.RejectionReason}",
            "warning", $"applications/{application.AdoptionApplicationId}");

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Application rejected." });
    }

    // POST /api/adoption-applications/{id}/complete
    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = $"{RoleNames.Administrator},{RoleNames.Staff}")]
    public async Task<IActionResult> Complete(int id, CompleteApplicationRequestDto request, CancellationToken cancellationToken)
    {
        var companyId = _currentCompany.GetCompanyId();
        if (companyId is null)
        {
            return Unauthorized();
        }

        await using var db = await _tenantFactory.CreateAsync(companyId.Value);

        var application = await db.AdoptionApplications
            .FirstOrDefaultAsync(a => a.AdoptionApplicationId == id && a.DeletedAt == null, cancellationToken);

        if (application is null)
        {
            return NotFound();
        }

        if (application.ApplicationStatus != ApplicationStatuses.Approved)
        {
            return BadRequest(new { message = "Only an Approved application can be marked Completed." });
        }

        var pet = await db.Pets.FirstOrDefaultAsync(p => p.PetId == application.PetId, cancellationToken);

        application.ApplicationStatus = ApplicationStatuses.Completed;
        application.CompletedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.Remarks))
        {
            application.Remarks = request.Remarks.Trim();
        }

        NotificationHelper.Create(db, application.UserId,
            "Adoption Completed",
            $"Congratulations! Your adoption of {pet?.PetName} is now complete.",
            "success", $"applications/{application.AdoptionApplicationId}");

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new SimpleMessageDto { Message = "Application marked as Completed." });
    }

    // ----- Helpers -----

    private static async Task<string> GenerateApplicationNumberAsync(TenantErpDbContext db, CancellationToken cancellationToken)
    {
        var count = await db.AdoptionApplications.CountAsync(cancellationToken);
        var nextNumber = count + 1;
        return $"APP-{DateTime.UtcNow.Year}-{nextNumber:D6}";
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AdoptionApplicationDto ToDto(AdoptionApplication a) => new()
    {
        AdoptionApplicationId = a.AdoptionApplicationId,
        ApplicationNumber = a.ApplicationNumber,
        PetId = a.PetId,
        PetName = a.Pet?.PetName ?? string.Empty,
        PetStatus = a.Pet?.Status ?? string.Empty,
        UserId = a.UserId,
        ApplicantName = a.User?.FullName ?? string.Empty,
        ReviewedBy = a.ReviewedBy,
        ReviewerName = a.Reviewer?.FullName,
        FullName = a.FullName,
        Email = a.Email,
        Phone = a.Phone,
        Address = a.Address,
        Occupation = a.Occupation,
        HousingType = a.HousingType,
        HomeDescription = a.HomeDescription,
        HasYard = a.HasYard,
        HasOtherPets = a.HasOtherPets,
        OtherPetsDetails = a.OtherPetsDetails,
        HasChildren = a.HasChildren,
        ChildrenDetails = a.ChildrenDetails,
        WorkingHours = a.WorkingHours,
        ReasonForAdopting = a.ReasonForAdopting,
        PetExperience = a.PetExperience,
        EmergencyContactName = a.EmergencyContactName,
        EmergencyContactPhone = a.EmergencyContactPhone,
        EmergencyContactRelationship = a.EmergencyContactRelationship,
        ApplicationStatus = a.ApplicationStatus,
        Remarks = a.Remarks,
        RejectionReason = a.RejectionReason,
        AppliedAt = a.AppliedAt,
        ReviewedAt = a.ReviewedAt,
        InterviewDate = a.InterviewDate,
        InterviewNotes = a.InterviewNotes,
        CompletedAt = a.CompletedAt
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