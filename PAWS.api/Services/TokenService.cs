using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace PAWS.api.Services;

public class TokenService : ITokenService
{
    private readonly JwtSettings _settings;
    private readonly JsonWebTokenHandler _handler = new();

    public TokenService(IOptions<JwtSettings> options)
    {
        _settings = options.Value;
    }

    public AccessToken CreateAccessToken(
        int userId,
        string fullName,
        string email,
        IEnumerable<string> roles,
        int? companyId)
    {
        // The facts stored inside the token.
        var claims = new List<Claim>
        {
            new Claim(PawsClaims.UserId, userId.ToString()),
            new Claim(PawsClaims.Name, fullName),
            new Claim(PawsClaims.Email, email),
            new Claim(PawsClaims.TokenId, Guid.NewGuid().ToString())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(PawsClaims.Role, role));
        }

        if (companyId.HasValue)
        {
            claims.Add(new Claim(PawsClaims.CompanyId, companyId.Value.ToString()));
        }

        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_settings.AccessTokenMinutes);

        // The secret key turns into a signature. Without the key, nobody can forge a token.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }
}