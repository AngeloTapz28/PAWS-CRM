namespace PAWS.api.Services;

// Settings for sign-in tokens. Issuer, audience and lifetime are NOT secret, so they have
// default values here. The SigningKey IS secret: it comes only from User Secrets
// ("Jwt:SigningKey") on your PC, or from an environment variable on the hosted server.
public class JwtSettings
{
    public const string SectionName = "Jwt";

    // Who issued the token (this API).
    public string Issuer { get; set; } = "PAWS.api";

    // Who the token is for (the WinUI app).
    public string Audience { get; set; } = "PAWS.winui";

    // How long a token works before the user must sign in again.
    public int AccessTokenMinutes { get; set; } = 60;

    // The secret used to sign tokens. NEVER put a real value in a file.
    public string SigningKey { get; set; } = string.Empty;
}