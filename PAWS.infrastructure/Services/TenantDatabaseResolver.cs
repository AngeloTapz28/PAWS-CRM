using Microsoft.EntityFrameworkCore;
using PAWS.infrastructure.Data;

namespace PAWS.infrastructure.Services;

/// <summary>
/// Looks in the MASTER database for the active tenant database that
/// belongs to a company. It never returns a password. The password is
/// looked up later, by the factory, from secure configuration.
/// </summary>
public class TenantDatabaseResolver : ITenantDatabaseResolver
{
    private readonly MasterErpDbContext _masterDb;

    public TenantDatabaseResolver(MasterErpDbContext masterDb)
    {
        _masterDb = masterDb;
    }

    public async Task<TenantDatabaseInfo> GetDatabaseInfoAsync(int companyId)
    {
        // Find the active database row for this company. The company
        // itself must also be active. If a company has been switched off,
        // its database must not be reachable.
        var tenantDatabase = await _masterDb.CompanyDatabases
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId
                     && x.IsActive
                     && x.Company != null
                     && x.Company.IsActive)
            .OrderBy(x => x.CompanyDatabaseId)
            .FirstOrDefaultAsync();

        if (tenantDatabase == null)
        {
            throw new InvalidOperationException(
                $"No active tenant database found for CompanyId {companyId}.");
        }

        return new TenantDatabaseInfo
        {
            ServerName = tenantDatabase.ServerName,
            DatabaseName = tenantDatabase.DatabaseName,
            CredentialKey = tenantDatabase.CredentialKey
        };
    }
}
