using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PAWS.infrastructure.Data;

namespace PAWS.infrastructure.Services;

/// <summary>
/// Builds a TenantErpDbContext for one company.
/// It asks the resolver WHERE the tenant database is, then reads the
/// database login from secure configuration (User Secrets locally,
/// environment variables when hosted). The master database never
/// stores a password.
/// </summary>
public class TenantDbContextFactory : ITenantDbContextFactory
{
    private readonly ITenantDatabaseResolver _resolver;
    private readonly IConfiguration _configuration;

    public TenantDbContextFactory(
        ITenantDatabaseResolver resolver,
        IConfiguration configuration)
    {
        _resolver = resolver;
        _configuration = configuration;
    }

    public async Task<TenantErpDbContext> CreateAsync(int companyId)
    {
        // 1. Ask the master database where this company's database is.
        var databaseInfo = await _resolver.GetDatabaseInfoAsync(companyId);

        // 2. Read the login for that database from secure configuration.
        //    The keys look like  TenantCredentials:TenantA:UserId
        //                        TenantCredentials:TenantA:Password
        var userId = _configuration[
            $"TenantCredentials:{databaseInfo.CredentialKey}:UserId"];
        var password = _configuration[
            $"TenantCredentials:{databaseInfo.CredentialKey}:Password"];

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(password))
        {
            // The message names the KEY only. It never prints a password.
            throw new InvalidOperationException(
                $"Credentials not found for key '{databaseInfo.CredentialKey}'.");
        }

        // 3. Build the connection string. SqlConnectionStringBuilder adds
        //    the values safely, so a special character in a password
        //    cannot break the string.
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = databaseInfo.ServerName,
            InitialCatalog = databaseInfo.DatabaseName,
            UserID = userId,
            Password = password,
            Encrypt = true,
            TrustServerCertificate = true,
            MultipleActiveResultSets = true
        }.ConnectionString;

        // 4. Create and return a context for that one database.
        var options = new DbContextOptionsBuilder<TenantErpDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new TenantErpDbContext(options);
    }
}
