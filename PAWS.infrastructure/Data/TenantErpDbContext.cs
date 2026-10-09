using Microsoft.EntityFrameworkCore;
using PAWS.domain.Entities;

namespace PAWS.infrastructure.Data;

/// <summary>
/// The context for ONE tenant database. The same class is used for every
/// tenant (A, B and C). The factory (Phase 6) points it at a different
/// database each time, so the schema is identical but the data is separate.
/// It holds the 15 PAWS entities only. Company and CompanyDatabase belong
/// to the master database and must NOT be added here.
/// </summary>
public class TenantErpDbContext : DbContext
{
    public TenantErpDbContext(DbContextOptions<TenantErpDbContext> options)
        : base(options)
    {
    }

    // Access control
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RoleUser> RoleUsers => Set<RoleUser>();

    // Pet catalog
    public DbSet<PetCategory> PetCategories => Set<PetCategory>();
    public DbSet<Breed> Breeds => Set<Breed>();
    public DbSet<Pet> Pets => Set<Pet>();
    public DbSet<Branch> Branches => Set<Branch>();

    // Adoption and medical
    public DbSet<AdoptionApplication> AdoptionApplications => Set<AdoptionApplication>();
    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
    public DbSet<VaccinationRecord> VaccinationRecords => Set<VaccinationRecord>();

    // Payments and customer service
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<CustomerServiceRequest> CustomerServiceRequests => Set<CustomerServiceRequest>();

    // Notifications, audit and CRM
    public DbSet<SystemNotification> SystemNotifications => Set<SystemNotification>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<CustomerRetention> CustomerRetentions => Set<CustomerRetention>();
    public DbSet<Promo> Promos => Set<Promo>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Each step of Phase 5B adds one group below.
        ConfigureAccessControl(builder);
        ConfigurePetCatalog(builder);
        ConfigureAdoptionAndMedical(builder);
        ConfigurePaymentsAndService(builder);
        ConfigureNotificationsAuditAndCrm(builder);
        SeedTenantRoles(builder);
    }

    // ------------------------------------------------------------------
    // STEP 2: User, Role, RoleUser
    // ------------------------------------------------------------------
    private static void ConfigureAccessControl(ModelBuilder builder)
    {
        builder.Entity<User>(entity =>
        {
            entity.HasKey(x => x.UserId);

            entity.Property(x => x.FullName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Email)
                .HasMaxLength(255)
                .IsRequired();

            // Two accounts cannot share the same email.
            entity.HasIndex(x => x.Email)
                .IsUnique();

            // Will hold a HASHED password (Phase 15), never plain text.
            entity.Property(x => x.Password)
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(x => x.Phone).HasMaxLength(20);
            entity.Property(x => x.Address).HasMaxLength(500);
            entity.Property(x => x.Gender).HasMaxLength(20);
            entity.Property(x => x.Avatar).HasMaxLength(500);

            // Store only the date, not a time of day.
            entity.Property(x => x.DateOfBirth).HasColumnType("date");

            // Active, Inactive or Suspended.
            entity.Property(x => x.Status)
                .HasMaxLength(20)
                .IsRequired();
        });

        builder.Entity<Role>(entity =>
        {
            entity.HasKey(x => x.RoleId);

            entity.Property(x => x.Name)
                .HasMaxLength(50)
                .IsRequired();

            // Two roles cannot share the same name.
            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        builder.Entity<RoleUser>(entity =>
        {
            // The pair (UserId, RoleId) is the primary key, so the same
            // role cannot be given to the same user twice.
            entity.HasKey(x => new { x.UserId, x.RoleId });

            // The user who HAS the role.
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // The role itself.
            entity.HasOne(x => x.Role)
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            // The Administrator who GAVE the role (optional).
            entity.HasOne(x => x.AssignedByUser)
                .WithMany()
                .HasForeignKey(x => x.AssignedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });


    }


    // ------------------------------------------------------------------
    // STEP 3: PetCategory, Breed, Pet, Branch
    // ------------------------------------------------------------------
    private static void ConfigurePetCatalog(ModelBuilder builder)
    {
        builder.Entity<PetCategory>(entity =>
        {
            entity.HasKey(x => x.PetCategoryId);

            entity.Property(x => x.CategoryName)
                .HasMaxLength(100)
                .IsRequired();

            // Two categories cannot share the same name.
            entity.HasIndex(x => x.CategoryName)
                .IsUnique();

            entity.Property(x => x.Description).HasMaxLength(500);
        });

        builder.Entity<Breed>(entity =>
        {
            entity.HasKey(x => x.BreedId);

            entity.Property(x => x.BreedName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Description).HasMaxLength(500);

            // A breed name cannot repeat inside the same category,
            // but two categories may each have a breed with the same name.
            entity.HasIndex(x => new { x.PetCategoryId, x.BreedName })
                .IsUnique();

            entity.HasOne(x => x.PetCategory)
                .WithMany()
                .HasForeignKey(x => x.PetCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Branch>(entity =>
        {
            entity.HasKey(x => x.BranchId);

            entity.Property(x => x.BranchName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Address).HasMaxLength(500);

            // Two branches in the same tenant cannot share a name.
            entity.HasIndex(x => x.BranchName)
                .IsUnique();
        });

        builder.Entity<Pet>(entity =>
        {
            entity.HasKey(x => x.PetId);

            entity.Property(x => x.PetName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Gender).HasMaxLength(20);
            entity.Property(x => x.Size).HasMaxLength(20);
            entity.Property(x => x.Color).HasMaxLength(50);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.ImagePath).HasMaxLength(500);

            // Money: up to 18 digits, 2 of them after the decimal point.
            entity.Property(x => x.AdoptionFee)
                .HasPrecision(18, 2);

            entity.Property(x => x.AdoptionFeeType).HasMaxLength(20);

            entity.Property(x => x.Status)
                .HasMaxLength(20)
                .IsRequired();

            // Written by a Veterinarian when rejecting/deferring approval.
            entity.Property(x => x.VetNotes).HasMaxLength(1000);

            // Adopters browse by status, so make that search fast.
            entity.HasIndex(x => x.Status);

            entity.HasOne(x => x.PetCategory)
                .WithMany()
                .HasForeignKey(x => x.PetCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Breed)
                .WithMany()
                .HasForeignKey(x => x.BreedId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Branch)
                .WithMany()
                .HasForeignKey(x => x.BranchId)
                .OnDelete(DeleteBehavior.Restrict);

            // The user who added the pet (optional).
            entity.HasOne(x => x.AddedByUser)
                .WithMany()
                .HasForeignKey(x => x.AddedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // The veterinarian who gave MEDICAL approval (optional).
            entity.HasOne(x => x.VetApprovedByUser)
                .WithMany()
                .HasForeignKey(x => x.VetApprovedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // The Administrator/Staff who gave LISTING approval (optional).
            entity.HasOne(x => x.AdminApprovedByUser)
                .WithMany()
                .HasForeignKey(x => x.AdminApprovedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }


    // ------------------------------------------------------------------
    // STEP 4: AdoptionApplication, MedicalRecord, VaccinationRecord
    // ------------------------------------------------------------------
    private static void ConfigureAdoptionAndMedical(ModelBuilder builder)
    {
        builder.Entity<AdoptionApplication>(entity =>
        {
            entity.HasKey(x => x.AdoptionApplicationId);

            entity.Property(x => x.ApplicationNumber)
                .HasMaxLength(50)
                .IsRequired();

            // Every application number must be unique.
            entity.HasIndex(x => x.ApplicationNumber)
                .IsUnique();

            // Applicant details.
            entity.Property(x => x.FullName).HasMaxLength(200);
            entity.Property(x => x.Email).HasMaxLength(255);
            entity.Property(x => x.Phone).HasMaxLength(20);
            entity.Property(x => x.Address).HasMaxLength(500);
            entity.Property(x => x.Occupation).HasMaxLength(100);

            // Home and household details.
            entity.Property(x => x.HousingType).HasMaxLength(50);
            entity.Property(x => x.HomeDescription).HasMaxLength(1000);
            entity.Property(x => x.OtherPetsDetails).HasMaxLength(1000);
            entity.Property(x => x.ChildrenDetails).HasMaxLength(1000);
            entity.Property(x => x.WorkingHours).HasMaxLength(100);
            entity.Property(x => x.ReasonForAdopting).HasMaxLength(2000);
            entity.Property(x => x.PetExperience).HasMaxLength(2000);

            // Emergency contact.
            entity.Property(x => x.EmergencyContactName).HasMaxLength(200);
            entity.Property(x => x.EmergencyContactPhone).HasMaxLength(20);
            entity.Property(x => x.EmergencyContactRelationship).HasMaxLength(50);

            entity.Property(x => x.ApplicationStatus)
                .HasMaxLength(30)
                .IsRequired();

            // Staff filter applications by status, so make that fast.
            entity.HasIndex(x => x.ApplicationStatus);

            entity.Property(x => x.Remarks).HasMaxLength(1000);
            entity.Property(x => x.RejectionReason).HasMaxLength(1000);
            entity.Property(x => x.InterviewNotes).HasMaxLength(2000);

            // The pet being applied for.
            entity.HasOne(x => x.Pet)
                .WithMany()
                .HasForeignKey(x => x.PetId)
                .OnDelete(DeleteBehavior.Restrict);

            // The adopter who applied.
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // The Staff/Admin who reviewed it (optional).
            entity.HasOne(x => x.Reviewer)
                .WithMany()
                .HasForeignKey(x => x.ReviewedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MedicalRecord>(entity =>
        {
            entity.HasKey(x => x.MedicalRecordId);

            // Store only the dates, not a time of day.
            entity.Property(x => x.ExaminationDate).HasColumnType("date");
            entity.Property(x => x.NextCheckupDate).HasColumnType("date");

            entity.Property(x => x.Diagnosis).HasMaxLength(500);
            entity.Property(x => x.Symptoms).HasMaxLength(2000);
            entity.Property(x => x.Treatment).HasMaxLength(2000);
            entity.Property(x => x.Medications).HasMaxLength(2000);

            // Weight in kilograms: up to 5 digits, 2 after the decimal point.
            entity.Property(x => x.WeightAtExam)
                .HasPrecision(5, 2);

            entity.Property(x => x.HealthStatus)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(x => x.Notes).HasMaxLength(2000);

            // Deleting a pet also deletes its health records (per the
            // original documentation). Pets are soft-deleted in practice,
            // so this rarely fires.
            entity.HasOne(x => x.Pet)
                .WithMany()
                .HasForeignKey(x => x.PetId)
                .OnDelete(DeleteBehavior.Cascade);

            // The veterinarian (a User with the Veterinarian role).
            entity.HasOne(x => x.Vet)
                .WithMany()
                .HasForeignKey(x => x.VetId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<VaccinationRecord>(entity =>
        {
            entity.HasKey(x => x.VaccinationRecordId);

            entity.Property(x => x.VaccineName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Manufacturer).HasMaxLength(200);
            entity.Property(x => x.BatchNumber).HasMaxLength(100);
            entity.Property(x => x.Notes).HasMaxLength(1000);

            // Store only the dates, not a time of day.
            entity.Property(x => x.DateAdministered).HasColumnType("date");
            entity.Property(x => x.NextDueDate).HasColumnType("date");

            // The "vaccination due" dashboard searches by this date.
            entity.HasIndex(x => x.NextDueDate);

            // Deleting a pet also deletes its vaccination records.
            entity.HasOne(x => x.Pet)
                .WithMany()
                .HasForeignKey(x => x.PetId)
                .OnDelete(DeleteBehavior.Cascade);

            // The person who gave the vaccine (a User).
            entity.HasOne(x => x.AdministeredByUser)
                .WithMany()
                .HasForeignKey(x => x.AdministeredBy)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }


    // ------------------------------------------------------------------
    // STEP 5: Payment, Transaction, CustomerServiceRequest
    // ------------------------------------------------------------------
    private static void ConfigurePaymentsAndService(ModelBuilder builder)
    {
        builder.Entity<Payment>(entity =>
        {
            entity.HasKey(x => x.PaymentId);

            entity.Property(x => x.ReferenceNumber)
                .HasMaxLength(50)
                .IsRequired();

            // Every payment reference number must be unique.
            entity.HasIndex(x => x.ReferenceNumber)
                .IsUnique();

            entity.Property(x => x.Type)
                .HasMaxLength(30)
                .IsRequired();

            // Money: up to 18 digits, 2 of them after the decimal point.
            entity.Property(x => x.Amount)
                .HasPrecision(18, 2);

            // Currency code such as "PHP".
            entity.Property(x => x.Currency)
                .HasMaxLength(3)
                .IsRequired();

            entity.Property(x => x.Method)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(x => x.Status)
                .HasMaxLength(20)
                .IsRequired();

            // Staff filter payments by status.
            entity.HasIndex(x => x.Status);

            entity.Property(x => x.ProofOfPayment).HasMaxLength(500);
            entity.Property(x => x.Notes).HasMaxLength(1000);

            // REQUIRED link: every payment belongs to an adoption application.
            entity.HasOne(x => x.AdoptionApplication)
                .WithMany()
                .HasForeignKey(x => x.AdoptionApplicationId)
                .OnDelete(DeleteBehavior.Restrict);

            // The person paying (optional).
            entity.HasOne(x => x.Payer)
                .WithMany()
                .HasForeignKey(x => x.PayerId)
                .OnDelete(DeleteBehavior.Restrict);

            // The Staff/Admin who recorded the payment (optional).
            entity.HasOne(x => x.RecordedByUser)
                .WithMany()
                .HasForeignKey(x => x.RecordedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Transaction>(entity =>
        {
            entity.HasKey(x => x.TransactionId);

            // The gateway's own reference. It is optional and may repeat,
            // so it gets an ordinary (not unique) index for searching.
            entity.Property(x => x.TransactionReference).HasMaxLength(100);
            entity.HasIndex(x => x.TransactionReference);

            entity.Property(x => x.Gateway).HasMaxLength(100);

            // Money: same precision as Payment.Amount.
            entity.Property(x => x.Amount)
                .HasPrecision(18, 2);

            entity.Property(x => x.Type)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(x => x.Status)
                .HasMaxLength(20)
                .IsRequired();

            // GatewayResponse is deliberately left without a maximum length,
            // because a gateway reply can be long.

            entity.Property(x => x.Notes).HasMaxLength(1000);

            // Restrict: financial records must never disappear because a
            // payment row was deleted.
            entity.HasOne(x => x.Payment)
                .WithMany()
                .HasForeignKey(x => x.PaymentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CustomerServiceRequest>(entity =>
        {
            entity.HasKey(x => x.CustomerServiceRequestId);

            // Inquiry, Complaint or Feedback.
            entity.Property(x => x.Type)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(x => x.Subject)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Message)
                .HasMaxLength(4000)
                .IsRequired();

            // New, In Progress, Resolved or Closed.
            entity.Property(x => x.Status)
                .HasMaxLength(20)
                .IsRequired();

            // Written by Staff/Admin. Empty until someone responds.
            entity.Property(x => x.Response).HasMaxLength(4000);

            // Staff filter requests by status and by type.
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.Type);

            // The customer must be an existing User.
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }


    // ------------------------------------------------------------------
    // STEP 6: SystemNotification, ActivityLog, CustomerRetention, Promo
    // ------------------------------------------------------------------
    private static void ConfigureNotificationsAuditAndCrm(ModelBuilder builder)
    {
        builder.Entity<SystemNotification>(entity =>
        {
            entity.HasKey(x => x.SystemNotificationId);

            entity.Property(x => x.Title)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Message)
                .HasMaxLength(2000)
                .IsRequired();

            entity.Property(x => x.Type)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(x => x.Link).HasMaxLength(500);
            entity.Property(x => x.Icon).HasMaxLength(100);

            // The unread badge counts rows where ReadAt is empty for one
            // user, so an index on (UserId, ReadAt) keeps that fast.
            entity.HasIndex(x => new { x.UserId, x.ReadAt });

            // When a user is deleted, their notifications go with them.
            // No other cascade reaches this table, so there is only one
            // cascade path.
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ActivityLog>(entity =>
        {
            entity.HasKey(x => x.ActivityLogId);

            entity.Property(x => x.Action)
                .HasMaxLength(200)
                .IsRequired();

            // ModelType and ModelId are plain values, not foreign keys.
            entity.Property(x => x.ModelType).HasMaxLength(100);
            entity.HasIndex(x => new { x.ModelType, x.ModelId });

            entity.Property(x => x.Description).HasMaxLength(2000);

            // OldValues and NewValues hold JSON text and are deliberately
            // left without a maximum length.

            entity.Property(x => x.IpAddress).HasMaxLength(45);
            entity.Property(x => x.UserAgent).HasMaxLength(500);

            // Administrators review the log newest first.
            entity.HasIndex(x => x.CreatedAt);

            // If the user is deleted, the log entry stays and UserId
            // becomes empty.
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CustomerRetention>(entity =>
        {
            entity.HasKey(x => x.CustomerRetentionId);

            entity.Property(x => x.Notes).HasMaxLength(2000);

            // Each customer has at most ONE retention row.
            entity.HasIndex(x => x.UserId)
                .IsUnique();

            // Staff look up who is due for follow-up by date.
            entity.HasIndex(x => x.NextFollowUpDate);

            // The customer this row is about.
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // The staff member who last contacted the customer (optional).
            entity.HasOne(x => x.LastContactedByUser)
                .WithMany()
                .HasForeignKey(x => x.LastContactedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Promo>(entity =>
        {
            entity.HasKey(x => x.PromoId);

            entity.Property(x => x.Title)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Description).HasMaxLength(1000);

            entity.Property(x => x.DiscountType)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(x => x.DiscountValue)
                .HasPrecision(18, 2);

            // Staff look up a customer's promos by UserId.
            entity.HasIndex(x => x.UserId);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.RedeemedByUser)
                .WithMany()
                .HasForeignKey(x => x.RedeemedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }


    // ------------------------------------------------------------------
    // STEP 7: Seed data - the four tenant-level roles
    // ------------------------------------------------------------------
    private static void SeedTenantRoles(ModelBuilder builder)
    {
        // A FIXED date is required. If this used DateTime.UtcNow, EF Core
        // would see different seed data on every build and reject it.
        var seedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Every tenant database receives these same four rows.
        // Super Admin is NOT here: it lives in the master database.
        builder.Entity<Role>().HasData(
            new Role { RoleId = 1, Name = "Administrator", CreatedAt = seedDate },
            new Role { RoleId = 2, Name = "Staff", CreatedAt = seedDate },
            new Role { RoleId = 3, Name = "Veterinarian", CreatedAt = seedDate },
            new Role { RoleId = 4, Name = "Adopter", CreatedAt = seedDate }
        );
    }
}