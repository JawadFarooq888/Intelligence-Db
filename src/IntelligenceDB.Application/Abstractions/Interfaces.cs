using IntelligenceDB.Domain.Audit;

namespace IntelligenceDB.Application.Abstractions;

/// <summary>Encrypts and decrypts stored secrets (passwords, client secrets, webhook URLs).</summary>
public interface ISecretProtector
{
    byte[] Protect(string plainText);
    string Unprotect(byte[] protectedData);
}

/// <summary>The portal user making the current request.</summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? UserName { get; }
    string? ClientIp { get; }
    bool IsInRole(string role);

    /// <summary>Which server groups this user may see. Admins always see everything.</summary>
    Task<AccessScope> GetScopeAsync(CancellationToken cancellationToken);
}

public sealed record AccessScope(bool AllGroups, IReadOnlySet<int> GroupIds)
{
    public static readonly AccessScope Everything = new(true, new HashSet<int>());

    public bool CanSee(int? groupId) =>
        AllGroups || (groupId.HasValue && GroupIds.Contains(groupId.Value));
}

/// <summary>Writes rows to the immutable audit log.</summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default);
}

/// <param name="Details">Any object; serialised to JSON. Never put secrets in here.</param>
public sealed record AuditRecord(
    string Action,
    int? ServerId = null,
    string? ServerName = null,
    string? DatabaseName = null,
    string? Tsql = null,
    string Result = AuditResults.Succeeded,
    int? DurationMs = null,
    string? Error = null,
    object? Details = null);
