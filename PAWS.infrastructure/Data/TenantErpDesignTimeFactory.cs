using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PAWS.infrastructure.Data;

/// <summary>
/// USED ONLY BY THE EF CORE TOOLS (Add-Migration and Update-Database).
/// It is never used while the API is running. At runtime, every
/// TenantErpDbContext comes from TenantDbContextFactory instead.
///
/// The connection string below is a DUMMY. It contains no real server and
/// no credentials. Add-Migration only needs to know the database PROVIDER
/// (SQL Server), not a real database. When we apply the migration to a
/// tenant database in Phase 9, we pass that tenant's real connection with
/// the -Connection parameter.
///
/// NEVER put a real server name, login or password in this file.
/// </summary>
public class TenantErpDesignTimeFactory : IDesignTimeDbContextFactory<TenantErpDbContext>
{
    public TenantErpDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenantErpDbContext>()
            .UseSqlServer("Server=(local);Database=PAWS_DesignTimeOnly;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        return new TenantErpDbContext(options);
    }
}
