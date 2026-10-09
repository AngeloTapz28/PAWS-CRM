namespace PAWS.api.Services;

// The ONE place that decides which company (tenant) the current request belongs to.
// Controllers call this, then pass the result to ITenantDbContextFactory.CreateAsync(...).
public interface ICurrentCompanyService
{
    // Returns the current company's ID, or null if this request has no company.
    int? GetCompanyId();
}
