namespace PAWS.api.Services;

// A separate setup key from Bootstrap:SetupKey (which creates tenant Administrators).
// This one guards creation of the very first Super Admin (a master-database
// Identity account, with no company). Comes ONLY from User Secrets.
public class SuperAdminBootstrapSettings
{
    public const string SectionName = "SuperAdminBootstrap";

    public string SetupKey { get; set; } = string.Empty;
}