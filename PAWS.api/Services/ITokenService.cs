namespace PAWS.api.Services;

// A token and the moment it stops working.
public record AccessToken(string Token, DateTime ExpiresAtUtc);

// The ONE place that creates sign-in tokens.
public interface ITokenService
{
    // companyId is null for the Super Admin (a platform account with no company).
    AccessToken CreateAccessToken(
        int userId,
        string fullName,
        string email,
        IEnumerable<string> roles,
        int? companyId);
}