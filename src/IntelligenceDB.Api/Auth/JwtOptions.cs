using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace IntelligenceDB.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "IntelligenceDB";
    public string Audience { get; set; } = "IntelligenceDB";

    /// <summary>At least 32 characters. Set via Jwt__SigningKey; never commit a real key.</summary>
    public string? SigningKey { get; set; }

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
}

/// <summary>The HMAC key used to sign and validate access tokens.</summary>
public sealed class JwtSigningKey
{
    private JwtSigningKey(SymmetricSecurityKey key, bool isEphemeral)
    {
        Key = key;
        IsEphemeral = isEphemeral;
    }

    public SymmetricSecurityKey Key { get; }

    /// <summary>True when a random development key was generated (sessions end when the API restarts).</summary>
    public bool IsEphemeral { get; }

    public static JwtSigningKey Create(string? configuredKey, bool isDevelopment)
    {
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            var bytes = Encoding.UTF8.GetBytes(configuredKey);
            if (bytes.Length < 32)
            {
                throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters long.");
            }

            return new JwtSigningKey(new SymmetricSecurityKey(bytes), false);
        }

        if (!isDevelopment)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set the Jwt__SigningKey environment variable to a random value of 32+ characters.");
        }

        return new JwtSigningKey(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)), true);
    }
}
