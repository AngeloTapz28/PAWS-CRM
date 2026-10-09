using PAWS.infrastructure.Data;

namespace PAWS.infrastructure.Services;

/// <summary>
/// Creates a TenantErpDbContext that is connected to the correct
/// tenant database for a given company.
/// </summary>
public interface ITenantDbContextFactory
{
    /// <summary>
    /// Returns a new context for the company's tenant database.
    /// The caller must dispose it when finished, for example with
    /// "await using var db = await factory.CreateAsync(companyId);".
    /// </summary>
    Task<TenantErpDbContext> CreateAsync(int companyId);
}
