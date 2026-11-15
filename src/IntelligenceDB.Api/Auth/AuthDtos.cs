using System.ComponentModel.DataAnnotations;

namespace IntelligenceDB.Api.Auth;

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(256)]
    public string Password { get; init; } = string.Empty;
}

public sealed class TwoFactorLoginRequest
{
    [Required, MaxLength(2048)]
    public string TwoFactorToken { get; init; } = string.Empty;

    [Required, MaxLength(32)]
    public string Code { get; init; } = string.Empty;

    public bool IsRecoveryCode { get; init; }
}

public sealed class CodeRequest
{
    [Required, MaxLength(32)]
    public string Code { get; init; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required, MaxLength(256)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required, MaxLength(256)]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed record UserDto(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, bool TwoFactorEnabled);

public sealed record AuthSession(string AccessToken, DateTime ExpiresUtc, UserDto User);

/// <summary>Either a session, or a request for the second factor.</summary>
public sealed record LoginResponse(bool RequiresTwoFactor, string? TwoFactorToken, AuthSession? Session);

public sealed record TwoFactorStatusDto(bool Enabled, int RecoveryCodesLeft);

public sealed record TwoFactorSetupDto(string SharedKey, string AuthenticatorUri);

public sealed record RecoveryCodesDto(IReadOnlyList<string> Codes);
