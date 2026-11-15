using System.Diagnostics;
using Dapper;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Application.Servers;
using IntelligenceDB.Domain.Servers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace IntelligenceDB.Infrastructure.Targets;

/// <summary>
/// Test Connection + capability detection. Connects with the monitoring login, reads version and
/// permission facts, then (optionally) checks the admin login. Never throws for connection or
/// permission problems: they become a failed <see cref="ProbeResult"/> or a "false" fact.
/// </summary>
public sealed class ServerProbe(ITargetConnectionFactory factory, ILogger<ServerProbe> logger) : IServerProbe
{
    private const int QueryTimeoutSeconds = 15;

    public async Task<ProbeResult> ProbeAsync(TargetConnectionSpec monitoring, TargetConnectionSpec? admin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(monitoring);
        var sw = Stopwatch.StartNew();

        try
        {
            await using var conn = await factory.OpenAsync(monitoring, TargetSessionMode.Monitoring, cancellationToken);

            var basics = await conn.QuerySingleAsync<BasicsRow>(Cmd(ProbeQueries.Basics, cancellationToken));
            int major = VersionNames.ParseMajor(basics.ProductVersion);
            bool sqlDb = basics.EngineEdition == 5;

            var perms = await conn.QuerySingleAsync<PermissionRow>(
                Cmd(sqlDb ? ProbeQueries.DatabasePermissions : ProbeQueries.ServerPermissions, cancellationToken));

            bool msdb = !sqlDb
                        && await CanRunAsync(conn, ProbeQueries.MsdbJobsAccess, cancellationToken)
                        && await CanRunAsync(conn, ProbeQueries.MsdbBackupAccess, cancellationToken);

            bool? agentRunning = !sqlDb && perms.ViewState
                ? await ScalarOrDefaultAsync<bool?>(conn, ProbeQueries.AgentRunning, cancellationToken)
                : null;

            bool volumes = !sqlDb && await CanRunAsync(conn, ProbeQueries.VolumeStats, cancellationToken);

            bool errorLog = !sqlDb && (basics.IsSysadmin || basics.IsSecurityAdmin
                || (perms.HasRdsAdmin && await ScalarOrDefaultAsync<bool?>(conn, ProbeQueries.RdsErrorLogAccess, cancellationToken) == true));

            SysInfoRow? sysInfo = perms.ViewState ? await SysInfoOrDefaultAsync(conn, cancellationToken) : null;

            string? hostPlatform = !sqlDb && major >= 14
                ? await ScalarOrDefaultAsync<string?>(conn, ProbeQueries.HostInfo, cancellationToken)
                : null;

            var permissions = await ListOrEmptyAsync(conn,
                sqlDb ? ProbeQueries.MyDatabasePermissions : ProbeQueries.MyServerPermissions, cancellationToken);

            var facts = new ProbeFacts(
                basics.EngineEdition,
                major,
                basics.Edition ?? string.Empty,
                perms.HasRdsAdmin,
                perms.HasCloudSqlMarker,
                basics.IsSysadmin,
                perms.ViewState,
                perms.ViewDefinition,
                msdb,
                agentRunning,
                volumes,
                errorLog);

            var info = new ServerInfoSnapshot(
                basics.ProductVersion ?? string.Empty,
                major,
                basics.ProductLevel,
                basics.ProductUpdateLevel,
                basics.Edition ?? string.Empty,
                basics.EngineEdition,
                hostPlatform,
                sysInfo?.CpuCount,
                sysInfo?.PhysicalMemoryMb,
                sysInfo?.SqlStartTimeUtc is { } start ? DateTime.SpecifyKind(start, DateTimeKind.Utc) : null,
                basics.Collation,
                basics.ServerName,
                basics.IsSysadmin);

            AdminFacts? adminFacts = null;
            string? adminError = null;
            if (admin is not null)
            {
                (adminFacts, adminError) = await ProbeAdminAsync(admin, sqlDb, cancellationToken);
            }

            return new ProbeResult(true, ConnectionStatus.Online, null, (int)sw.ElapsedMilliseconds,
                facts, info, permissions, admin is not null, adminFacts, adminError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqlException ex)
        {
            logger.LogWarning("Connection test to {Target} failed: SQL error {Number}: {Message}", monitoring, ex.Number, ex.Message);
            return ProbeResult.Failed(Classify(ex), ConnectionErrors.Describe(ex, monitoring), (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // Entra token errors, DNS errors, TLS errors etc. arrive as non-SqlException types.
            logger.LogWarning(ex, "Connection test to {Target} failed", monitoring);
            return ProbeResult.Failed(ConnectionStatus.Unreachable, ConnectionErrors.Describe(ex, monitoring), (int)sw.ElapsedMilliseconds);
        }
    }

    private async Task<(AdminFacts?, string?)> ProbeAdminAsync(TargetConnectionSpec admin, bool sqlDb, CancellationToken ct)
    {
        try
        {
            await using var conn = await factory.OpenAsync(admin, TargetSessionMode.Admin, ct);
            var row = await conn.QuerySingleAsync<AdminRow>(Cmd(sqlDb ? ProbeQueries.AdminDatabase : ProbeQueries.AdminServer, ct));
            bool agentControl = row.IsSysadmin
                || (!sqlDb && await ScalarOrDefaultAsync<bool?>(conn, ProbeQueries.AgentOperatorMember, ct) == true);

            return (new AdminFacts(row.IsSysadmin, row.AlterAnyConnection, row.AlterSettings, agentControl), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SqlException ex)
        {
            logger.LogWarning("Admin login test to {Target} failed: SQL error {Number}: {Message}", admin, ex.Number, ex.Message);
            return (null, ConnectionErrors.Describe(ex, admin));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin login test to {Target} failed", admin);
            return (null, ConnectionErrors.Describe(ex, admin));
        }
    }

    private static ConnectionStatus Classify(SqlException ex) => ex.Number switch
    {
        // Login failed / account disabled / password expired / must change / cannot open database.
        18456 or 18452 or 18470 or 18487 or 18488 or 4060 => ConnectionStatus.AuthFailed,
        _ => ConnectionStatus.Unreachable,
    };

    private static CommandDefinition Cmd(string sql, CancellationToken ct) =>
        new(sql, commandTimeout: QueryTimeoutSeconds, cancellationToken: ct);

    /// <summary>True if the statement runs; false if it fails (missing permission or object).</summary>
    private async Task<bool> CanRunAsync(SqlConnection conn, string sql, CancellationToken ct)
    {
        try
        {
            await conn.ExecuteScalarAsync(Cmd(sql, ct));
            return true;
        }
        catch (SqlException ex)
        {
            logger.LogDebug("Probe statement not available (SQL error {Number}): {Message}", ex.Number, ex.Message);
            return false;
        }
    }

    private async Task<T?> ScalarOrDefaultAsync<T>(SqlConnection conn, string sql, CancellationToken ct)
    {
        try
        {
            return await conn.ExecuteScalarAsync<T>(Cmd(sql, ct));
        }
        catch (SqlException ex)
        {
            logger.LogDebug("Probe scalar not available (SQL error {Number}): {Message}", ex.Number, ex.Message);
            return default;
        }
    }

    private async Task<SysInfoRow?> SysInfoOrDefaultAsync(SqlConnection conn, CancellationToken ct)
    {
        try
        {
            return await conn.QuerySingleOrDefaultAsync<SysInfoRow>(Cmd(ProbeQueries.SysInfo, ct));
        }
        catch (SqlException ex)
        {
            logger.LogDebug("sys.dm_os_sys_info not available (SQL error {Number}): {Message}", ex.Number, ex.Message);
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> ListOrEmptyAsync(SqlConnection conn, string sql, CancellationToken ct)
    {
        try
        {
            return (await conn.QueryAsync<string>(Cmd(sql, ct))).ToList();
        }
        catch (SqlException ex)
        {
            logger.LogDebug("Permission list not available (SQL error {Number}): {Message}", ex.Number, ex.Message);
            return [];
        }
    }

    // Dapper row types (mapped by column name).
    internal sealed class BasicsRow
    {
        public int EngineEdition { get; set; }
        public string? ProductVersion { get; set; }
        public string? ProductLevel { get; set; }
        public string? ProductUpdateLevel { get; set; }
        public string? Edition { get; set; }
        public string? Collation { get; set; }
        public string? ServerName { get; set; }
        public bool IsSysadmin { get; set; }
        public bool IsSecurityAdmin { get; set; }
    }

    internal sealed class PermissionRow
    {
        public bool ViewState { get; set; }
        public bool ViewDefinition { get; set; }
        public bool HasRdsAdmin { get; set; }
        public bool HasCloudSqlMarker { get; set; }
    }

    internal sealed class SysInfoRow
    {
        public int CpuCount { get; set; }
        public long PhysicalMemoryMb { get; set; }
        public DateTime? SqlStartTimeUtc { get; set; }
    }

    internal sealed class AdminRow
    {
        public bool IsSysadmin { get; set; }
        public bool AlterAnyConnection { get; set; }
        public bool AlterSettings { get; set; }
    }
}
