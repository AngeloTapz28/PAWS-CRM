using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PAWS.domain.Entities;

namespace PAWS.infrastructure.Data;

/// <summary>
/// The context for the MASTER database.
/// It holds the tenant registry (Companies and CompanyDatabases) and,
/// because it inherits IdentityDbContext, the ASP.NET Identity tables used
/// for platform-level accounts such as the Super Admin.
/// It also holds the SubscriptionPlans that companies can be assigned.
/// It holds NO PAWS adoption data. That lives in the tenant databases.
/// </summary>
public class MasterErpDbContext : IdentityDbContext
{
    public MasterErpDbContext(DbContextOptions<MasterErpDbContext> options)
        : base(options)
    {
    }

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<CompanyDatabase> CompanyDatabases => Set<CompanyDatabase>();

    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Must stay first: it sets up the Identity tables.
        base.OnModelCreating(builder);

        builder.Entity<Company>(entity =>
        {
            entity.HasKey(x => x.CompanyId);

            entity.Property(x => x.CompanyCode)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.CompanyName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.TermsAcceptedByName)
                .HasMaxLength(200);

            // No two companies can share the same code.
            entity.HasIndex(x => x.CompanyCode)
                .IsUnique();

            // Restrict: a plan cannot be deleted while a company uses it.
            entity.HasOne(x => x.SubscriptionPlan)
                .WithMany()
                .HasForeignKey(x => x.SubscriptionPlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CompanyDatabase>(entity =>
        {
            entity.HasKey(x => x.CompanyDatabaseId);

            entity.Property(x => x.ServerName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.DatabaseName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.CredentialKey)
                .HasMaxLength(50)
                .IsRequired();

            // Restrict: a Company cannot be deleted while it still has
            // database registrations.
            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasKey(x => x.SubscriptionPlanId);

            entity.Property(x => x.Name)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Price)
                .HasPrecision(18, 2);

            entity.Property(x => x.TermsAndConditions)
                .IsRequired();   // no max length: stored as nvarchar(max)

            // No two plans can share the same name.
            entity.HasIndex(x => x.Name)
                .IsUnique();

            // Seed data. Dates must be FIXED (not DateTime.UtcNow),
            // otherwise EF sees a different value on every build and complains.
            var seededAt = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);
            var termsUpdatedAt = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

            entity.HasData(
                new SubscriptionPlan
                {
                    SubscriptionPlanId = 1,
                    Name = "Free",
                    Price = 0m,
                    BranchLimit = 1,
                    TermsAndConditions =
                        "FREE PLAN - PLACEHOLDER TERMS. " +
                        "This plan is provided at no cost and allows 1 branch. " +
                        "Features and limits may change. The Super Admin can edit these terms.",
                    TermsUpdatedAt = termsUpdatedAt,
                    IsActive = true,
                    CreatedAt = seededAt
                },
                new SubscriptionPlan
                {
                    SubscriptionPlanId = 2,
                    Name = "Basic",
                    Price = 499m,
                    BranchLimit = 3,
                    TermsAndConditions =
                        "BASIC PLAN - PLACEHOLDER TERMS. " +
                        "PHP 499 per 30-day cycle. Allows up to 3 branches. " +
                        "The Super Admin can edit these terms.",
                    TermsUpdatedAt = termsUpdatedAt,
                    IsActive = true,
                    CreatedAt = seededAt
                },
                new SubscriptionPlan
                {
                    SubscriptionPlanId = 3,
                    Name = "Premium",
                    Price = 1499m,
                    BranchLimit = int.MaxValue,
                    TermsAndConditions =
                        "PREMIUM PLAN - PLACEHOLDER TERMS. " +
                        "PHP 1,499 per 30-day cycle. Unlimited branches. " +
                        "The Super Admin can edit these terms.",
                    TermsUpdatedAt = termsUpdatedAt,
                    IsActive = true,
                    CreatedAt = seededAt
                });
        });
    }
}