namespace PAWS.api.Services;

public class CurrentCompanyService : ICurrentCompanyService
{
    // The name of the value inside the sign-in token, set by TokenService (Step 15.2).
    private const string CompanyIdClaimType = "companyId";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentCompanyService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int? GetCompanyId()
    {
        // The signed-in user's token. As of Step 15.4, this is the ONLY source.
        // The Development-only fallback that used to read "Development:CompanyId"
        // from User Secrets has been removed now that real sign-in exists.
        var claimValue = _httpContextAccessor.HttpContext?
            .User.FindFirst(CompanyIdClaimType)?.Value;

        if (int.TryParse(claimValue, out var companyFromToken) && companyFromToken > 0)
        {
            return companyFromToken;
        }

        // No company for this request (no token, or a Super Admin token with none).
        return null;
    }
}