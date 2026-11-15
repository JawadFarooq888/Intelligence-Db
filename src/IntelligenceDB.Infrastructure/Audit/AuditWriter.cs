using System.Text.Json;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Domain.Audit;
using IntelligenceDB.Infrastructure.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IntelligenceDB.Infrastructure.Audit;

/// <summary>
/// Inserts audit rows using its own short-lived DbContext, so an audit write never accidentally
/// commits (or is rolled back with) unrelated pending changes of the calling request.
/// </summary>
public sealed class AuditWriter(
    IDbContextFactory<RepoDbContext> contextFactory,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<AuditWriter> logger) : IAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var entry = new AuditEntry
        {
            OccurredUtc = clock.GetUtcNow().UtcDateTime,
            UserId = user.UserId,
            UserName = user.UserName,
            ClientIp = user.ClientIp,
            Action = record.Action,
            ServerId = record.ServerId,
            ServerName = Truncate(record.ServerName, 128),
            DatabaseName = Truncate(record.DatabaseName, 128),
            Tsql = record.Tsql,
            Result = record.Result,
            DurationMs = record.DurationMs,
            Error = Truncate(record.Error, 4000),
            Details = record.Details is null ? null : JsonSerializer.Serialize(record.Details, JsonOptions),
        };

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            db.AuditEntries.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An action that cannot be audited must not look successful, so the error is re-thrown.
            logger.LogError(ex, "Failed to write audit entry {Action} for server {ServerId}", record.Action, record.ServerId);
            throw;
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
