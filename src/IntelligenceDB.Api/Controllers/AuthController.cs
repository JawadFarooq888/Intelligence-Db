using System.Security.Cryptography;
using System.Text.Encodings.Web;
using IntelligenceDB.Api.Auth;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Domain.Audit;
using IntelligenceDB.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IntelligenceDB.Api.Controllers;

/// <summary>
/// Portal sign-in. Access tokens (15 min) are returned in the body and kept in memory by the SPA;
/// the refresh token lives only in an HttpOnly, Secure, SameSite=Strict cookie.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<AppUser> users,
    TokenService tokens,
    IDataProtectionProvider dataProtection,
    IAuditWriter audit,
    TimeProvider clock) : ControllerBase
{
    /// <summary>Sent by the SPA on cookie-based calls; a cross-site form post cannot set it (CSRF defence).</summary>
    private const string CsrfHeader = "X-Requested-With";
    private const string CsrfValue = "IntelligenceDB";

    private const string InvalidLogin = "Invalid email or password.";
    private static readonly TimeSpan TwoFactorWindow = TimeSpan.FromMinutes(5);

    private ITimeLimitedDataProtector TwoFactorProtector =>
        dataProtection.CreateProtector("IntelligenceDB.Login.TwoFactor.v1").ToTimeLimitedDataProtector();

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            await AuditLoginAsync("Auth.Login", AuditResults.Failed, request.Email, "Unknown email", ct);
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidLogin);
        }

        if (await users.IsLockedOutAsync(user))
        {
            await AuditLoginAsync("Auth.Login", AuditResults.Denied, request.Email, "Locked out", ct);
            return Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "This account is temporarily locked after too many failed attempts. Try again later.");
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            await AuditLoginAsync("Auth.Login", AuditResults.Failed, request.Email, "Wrong password", ct);
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidLogin);
        }

        if (await users.GetTwoFactorEnabledAsync(user))
        {
            // Password is right; hand out a 5-minute token that proves it for the second step.
            var token = TwoFactorProtector.Protect(user.Id.ToString(), TwoFactorWindow);
            return new LoginResponse(true, token, null);
        }

        await users.ResetAccessFailedCountAsync(user);
        return new LoginResponse(false, null, await IssueSessionAsync(user, ct));
    }

    [AllowAnonymous]
    [HttpPost("login/2fa")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> LoginTwoFactor(TwoFactorLoginRequest request, CancellationToken ct)
    {
        string userId;
        try
        {
            userId = TwoFactorProtector.Unprotect(request.TwoFactorToken);
        }
        catch (CryptographicException)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The sign-in step expired. Enter your password again.");
        }

        var user = await users.FindByIdAsync(userId);
        if (user is null || await users.IsLockedOutAsync(user))
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in is not possible for this account right now.");
        }

        var code = request.Code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        bool valid = request.IsRecoveryCode
            ? (await users.RedeemTwoFactorRecoveryCodeAsync(user, request.Code.Trim())).Succeeded
            : await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code);

        if (!valid)
        {
            await users.AccessFailedAsync(user);
            await AuditLoginAsync("Auth.Login2fa", AuditResults.Failed, user.Email, "Wrong code", ct);
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The verification code is not valid.");
        }

        await users.ResetAccessFailedCountAsync(user);
        return new LoginResponse(false, null, await IssueSessionAsync(user, ct));
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthSession>> Refresh(CancellationToken ct)
    {
        if (!HasCsrfHeader()) return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Missing request header.");

        var raw = Request.Cookies[TokenService.RefreshCookieName];
        if (string.IsNullOrEmpty(raw)) return Unauthorized();

        var rotated = await tokens.RotateAsync(raw, ClientIp, ct);
        if (rotated is null)
        {
            ClearCookie();
            return Unauthorized();
        }

        var user = await users.FindByIdAsync(rotated.UserId.ToString());
        if (user is null || await users.IsLockedOutAsync(user))
        {
            await tokens.RevokeAsync(rotated.RawToken, ct);
            ClearCookie();
            return Unauthorized();
        }

        Response.Cookies.Append(TokenService.RefreshCookieName, rotated.RawToken, tokens.RefreshCookieOptions(rotated.ExpiresUtc));
        var roles = await users.GetRolesAsync(user);
        var (access, expires) = tokens.CreateAccessToken(user, roles);
        return new AuthSession(access, expires, await ToDtoAsync(user, roles));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!HasCsrfHeader()) return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Missing request header.");

        var raw = Request.Cookies[TokenService.RefreshCookieName];
        if (!string.IsNullOrEmpty(raw)) await tokens.RevokeAsync(raw, ct);
        ClearCookie();
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();
        return await ToDtoAsync(user, await users.GetRolesAsync(user));
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded) return IdentityProblem(result);

        // Sign out other devices.
        await tokens.RevokeAllAsync(user.Id, ct);
        await audit.WriteAsync(new AuditRecord("Auth.ChangePassword"), ct);
        return NoContent();
    }

    [HttpGet("2fa")]
    public async Task<ActionResult<TwoFactorStatusDto>> TwoFactorStatus()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();
        return new TwoFactorStatusDto(await users.GetTwoFactorEnabledAsync(user), await users.CountRecoveryCodesAsync(user));
    }

    /// <summary>Returns (and creates if needed) the authenticator key to scan into an authenticator app.</summary>
    [HttpPost("2fa/setup")]
    public async Task<ActionResult<TwoFactorSetupDto>> TwoFactorSetup()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();
        if (await users.GetTwoFactorEnabledAsync(user))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Two-factor authentication is already on. Turn it off first to re-pair.");
        }

        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
        }

        var email = user.Email ?? user.UserName ?? user.Id.ToString();
        var uri = $"otpauth://totp/{UrlEncoder.Default.Encode("IntelligenceDB")}:{UrlEncoder.Default.Encode(email)}" +
                  $"?secret={key}&issuer={UrlEncoder.Default.Encode("IntelligenceDB")}&digits=6";
        return new TwoFactorSetupDto(FormatKey(key!), uri);
    }

    [HttpPost("2fa/enable")]
    public async Task<ActionResult<RecoveryCodesDto>> TwoFactorEnable(CodeRequest request, CancellationToken ct)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        var code = request.Code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The code is not valid. Check the time on your phone and try again.");
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await audit.WriteAsync(new AuditRecord("Auth.TwoFactorEnabled"), ct);
        return new RecoveryCodesDto(codes?.ToList() ?? []);
    }

    [HttpPost("2fa/disable")]
    public async Task<IActionResult> TwoFactorDisable(CodeRequest request, CancellationToken ct)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        var code = request.Code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The code is not valid.");
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await audit.WriteAsync(new AuditRecord("Auth.TwoFactorDisabled"), ct);
        return NoContent();
    }

    // ------------------------------------------------------------------

    private async Task<AuthSession> IssueSessionAsync(AppUser user, CancellationToken ct)
    {
        var roles = await users.GetRolesAsync(user);
        var (access, expires) = tokens.CreateAccessToken(user, roles);
        var (refresh, refreshExpires) = await tokens.IssueRefreshTokenAsync(user.Id, ClientIp, ct);
        Response.Cookies.Append(TokenService.RefreshCookieName, refresh, tokens.RefreshCookieOptions(refreshExpires));

        user.LastLoginUtc = clock.GetUtcNow().UtcDateTime;
        await users.UpdateAsync(user);
        await AuditLoginAsync("Auth.Login", AuditResults.Succeeded, user.Email, null, ct);

        return new AuthSession(access, expires, await ToDtoAsync(user, roles));
    }

    private async Task<UserDto> ToDtoAsync(AppUser user, IList<string> roles) =>
        new(user.Id, user.Email ?? string.Empty, user.DisplayName, roles.ToList(), await users.GetTwoFactorEnabledAsync(user));

    private async Task<AppUser?> CurrentUserAsync()
    {
        var id = User.FindFirst("sub")?.Value;
        return id is null ? null : await users.FindByIdAsync(id);
    }

    private Task AuditLoginAsync(string action, string result, string? email, string? error, CancellationToken ct) =>
        audit.WriteAsync(new AuditRecord(action, Result: result, Error: error, Details: new { email }), ct);

    private bool HasCsrfHeader() =>
        string.Equals(Request.Headers[CsrfHeader].ToString(), CsrfValue, StringComparison.Ordinal);

    private void ClearCookie() =>
        Response.Cookies.Delete(TokenService.RefreshCookieName, new CookieOptions
        {
            Path = TokenService.RefreshCookiePath,
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
        });

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private ObjectResult IdentityProblem(IdentityResult result)
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["password"] = result.Errors.Select(e => e.Description).ToArray(),
        })
        {
            Title = "The request could not be completed.",
            Status = StatusCodes.Status400BadRequest,
        };
        return BadRequest(problem);
    }

    /// <summary>Groups the key in blocks of 4 so it is easier to type.</summary>
    private static string FormatKey(string key)
    {
        var lower = key.ToLowerInvariant();
        return string.Join(' ', Enumerable.Range(0, (lower.Length + 3) / 4)
            .Select(i => lower.Substring(i * 4, Math.Min(4, lower.Length - (i * 4)))));
    }
}
