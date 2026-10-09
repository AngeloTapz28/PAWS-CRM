namespace PAWS.api.Services;

// The names of the values stored inside a token. Using constants means a typo
// is a build error instead of a silent bug.
public static class PawsClaims
{
    public const string UserId = "sub";
    public const string Name = "name";
    public const string Email = "email";
    public const string Role = "role";
    public const string TokenId = "jti";

    // The company (tenant) the user belongs to. Super Admin tokens have NO company.
    public const string CompanyId = "companyId";
}