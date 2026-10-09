using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.infrastructure.Services;

/// <summary>
/// Finds out which tenant database belongs to a company by looking it up
/// in the MASTER database.
/// </summary>
public interface ITenantDatabaseResolver
{
    /// <summary>
    /// Returns the server, database name and credential key of the active
    /// tenant database for the given company. Throws if there is none.
    /// </summary>
    Task<TenantDatabaseInfo> GetDatabaseInfoAsync(int companyId);
}
