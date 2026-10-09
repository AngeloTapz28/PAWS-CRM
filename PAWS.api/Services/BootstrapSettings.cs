namespace PAWS.api.Services;

// The secret used to prove someone is allowed to create the FIRST Administrator
// of a company. Comes ONLY from User Secrets ("Bootstrap:SetupKey") or, when
// hosted, an environment variable. Separate from the JWT signing key on purpose:
// leaking one must not compromise the other.
public class BootstrapSettings
{
    public const string SectionName = "Bootstrap";

    public string SetupKey { get; set; } = string.Empty;
}