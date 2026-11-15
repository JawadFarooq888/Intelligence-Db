using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IntelligenceDB.Infrastructure.Identity;
using IntelligenceDB.Infrastructure.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IntelligenceDB.Api.Auth;

public sealed record RotatedRefreshToken(Guid UserId, string RawToken, DateTime ExpiresUtc);

/// <summary>Creates short-lived JWT access tokens and rotating refresh tokens.</summary>
public sealed class TokenService(
    IOptions<JwtOptions> options,
    JwtSigningKey signingKey,
    RepoDbContext db,
    TimeProvider clock,
    ILogger<TokenService> logger)
{
    public const string RefreshCookieName = "idb_rt";
    public const string RefreshCookiePath = "/api/auth";

    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresUtc) CreateAccessToken(AppUser user, IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("name", string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? string.Empty : user.DisplayName),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(roles.Select(r => new Claim("role", r)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(signingKey.Key, SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    public async Task<(string RawToken, DateTime ExpiresUtc)> IssueRefreshTokenAsync(Guid userId, string? ip, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        // Housekeeping: drop this user's expired tokens.
        await db.RefreshTokens.Where(t => t.UserId == userId && t.ExpiresUtc < now).ExecuteDeleteAsync(ct);

        var (raw, entity) = NewToken(userId, ip, now);
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return (raw, entity.ExpiresUtc);
    }

    /// <summary>
    /// Exchanges a refresh token for a new one. If an already-used token is presented again
    /// (likely stolen), every token of that user is revoked.
    /// </summary>
    public async Task<RotatedRefreshToken?> RotateAsync(string rawToken, string? ip, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = Hash(rawToken);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null) return null;

        if (existing.RevokedUtc is not null)
        {
            logger.LogWarning("Re-use of a revoked refresh token for user {UserId}; revoking all sessions", existing.UserId);
            await RevokeAllAsync(existing.UserId, ct);
            return null;
        }

        if (existing.ExpiresUtc <= now) return null;

        var (raw, replacement) = NewToken(existing.UserId, ip, now);
        existing.RevokedUtc = now;
        existing.ReplacedById = replacement.Id;
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);
        return new RotatedRefreshToken(existing.UserId, raw, replacement.ExpiresUtc);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        var hash = Hash(rawToken);
        var now = clock.GetUtcNow().UtcDateTime;
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, (DateTime?)now), ct);
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, (DateTime?)now), ct);
    }

    public CookieOptions RefreshCookieOptions(DateTime expiresUtc) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = RefreshCookiePath,
        Expires = expiresUtc,
        IsEssential = true,
    };

    private (string Raw, RefreshToken Entity) NewToken(Guid userId, string? ip, DateTime now)
    {
        var raw = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(64));
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(raw),
            CreatedUtc = now,
            ExpiresUtc = now.AddDays(_options.RefreshTokenDays),
            CreatedIp = ip is { Length: > 64 } ? ip[..64] : ip,
        };
        return (raw, entity);
    }

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
