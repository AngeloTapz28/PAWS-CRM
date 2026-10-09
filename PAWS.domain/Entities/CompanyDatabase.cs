using System;
using System.Collections.Generic;
using System.Text;

namespace PAWS.domain.Entities;

/// <summary>
/// Tells the system which tenant database belongs to which Company.
/// This entity lives in the MASTER database.
/// It never stores a password. CredentialKey is only a label such as
/// "TenantA" that points to a login kept in secure configuration.
/// </summary>
public class CompanyDatabase
{
    public int CompanyDatabaseId { get; set; }

    // Which company this database belongs to.
    public int CompanyId { get; set; }

    public string ServerName { get; set; } = string.Empty;

    public string DatabaseName { get; set; } = string.Empty;

    // A label such as "TenantA". The real UserId and Password are read from
    // secure configuration (User Secrets or environment variables).
    public string CredentialKey { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Navigation property: lets C# reach the Company object directly.
    public Company? Company { get; set; }
}
