using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.infrastructure.Services;

/// <summary>
/// Describes WHERE one tenant's database is. It is filled in by
/// TenantDatabaseResolver from the master database and read by
/// TenantDbContextFactory when it builds a connection.
/// It deliberately has NO password. The login is looked up in secure
/// configuration using CredentialKey.
/// </summary>
public class TenantDatabaseInfo
{
    // The address of the database server.
    public string ServerName { get; set; } = string.Empty;

    // The name of this tenant's database.
    public string DatabaseName { get; set; } = string.Empty;

    // A label such as "TenantA". The factory uses it to find the matching
    // UserId and Password in secure configuration.
    public string CredentialKey { get; set; } = string.Empty;
}
