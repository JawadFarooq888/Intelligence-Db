namespace IntelligenceDB.Domain.Audit;

/// <summary>
/// One row of the audit log. Rows are only ever inserted, never updated or deleted.
/// (Phase 5 converts the table to an append-only ledger table.)
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }
    public DateTime OccurredUtc { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? ClientIp { get; set; }
    public string Action { get; set; } = string.Empty;
    public int? ServerId { get; set; }
    public string? ServerName { get; set; }
    public string? DatabaseName { get; set; }
    public string? Tsql { get; set; }
    public string Result { get; set; } = AuditResults.Succeeded;
    public int? DurationMs { get; set; }
    public string? Error { get; set; }

    /// <summary>Extra JSON details. Must never contain secrets.</summary>
    public string? Details { get; set; }
}

public static class AuditResults
{
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Denied = "Denied";
}
