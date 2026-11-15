using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.Application.Monitoring;

/// <summary>One rule that cost points, shown to the user so the score is never a black box.</summary>
public sealed record HealthDeduction(string Rule, int Points, string Message);

public sealed record HealthResult(int Score, IReadOnlyList<HealthDeduction> Deductions);

public sealed record VolumeState(string MountPoint, long TotalMb, long FreeMb);

/// <summary>Everything the health rules look at. Null values mean "not measured" and cost nothing.</summary>
public sealed record HealthInputs(
    ConnectionStatus Status,
    double? AvgSqlCpuPct,
    long? PageLifeExpectancy,
    int? MemoryGrantsPending,
    int? BlockedSessions,
    IReadOnlyList<VolumeState> Volumes,
    IReadOnlyList<AdviceItem> DatabaseAdvice,
    IReadOnlyList<AdviceItem> ConfigAdvice);

/// <summary>
/// Health score 0-100: start at 100, subtract points per problem, never below 0.
/// Phase 3/4 add failed jobs, long blocking and deadlocks to the same rules.
/// </summary>
public static class HealthScoreCalculator
{
    public static HealthResult Calculate(HealthInputs input, MonitoringThresholds t)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(t);

        if (input.Status is ConnectionStatus.Unreachable or ConnectionStatus.AuthFailed)
        {
            return new HealthResult(0, [new HealthDeduction("UNREACHABLE", 100,
                input.Status == ConnectionStatus.AuthFailed ? "The monitoring login is rejected." : "The server cannot be reached.")]);
        }

        var d = new List<HealthDeduction>();

        if (input.AvgSqlCpuPct is { } cpu)
        {
            if (cpu >= t.CpuCriticalPct) d.Add(new("CPU_CRITICAL", 15, $"SQL Server CPU averaged {cpu:0}% over the last 15 minutes."));
            else if (cpu >= t.CpuWarningPct) d.Add(new("CPU_HIGH", 5, $"SQL Server CPU averaged {cpu:0}% over the last 15 minutes."));
        }

        if (input.MemoryGrantsPending is > 0)
        {
            d.Add(new("MEMORY_GRANTS", 10, $"{input.MemoryGrantsPending} queries are waiting for memory to start (memory pressure)."));
        }
        else if (input.PageLifeExpectancy is { } ple && ple < t.PageLifeExpectancyWarning)
        {
            d.Add(new("LOW_PLE", 5, $"Page life expectancy is {ple} s: data pages leave memory quickly (memory pressure or big scans)."));
        }

        if (input.BlockedSessions is > 0)
        {
            d.Add(new("BLOCKING", 5, $"{input.BlockedSessions} session(s) were blocked at the last sample."));
        }

        AddDatabaseRule(d, input.DatabaseAdvice, "DB_NO_FULL_BACKUP", "MISSING_FULL_BACKUP", 15, "database(s) without a recent full backup");
        AddDatabaseRule(d, input.DatabaseAdvice, "DB_NO_LOG_BACKUP", "MISSING_LOG_BACKUP", 10, "database(s) in FULL recovery without recent log backups");
        AddDatabaseRule(d, input.DatabaseAdvice, "DB_CHECKDB", "CHECKDB_OVERDUE", 10, "database(s) with DBCC CHECKDB overdue");
        AddDatabaseRule(d, input.DatabaseAdvice, "DB_STATE", "DATABASE_OFFLINE", 15, "database(s) not online");

        var low = input.Volumes.Where(v => v.TotalMb > 0).Select(v => (v, pct: 100.0 * v.FreeMb / v.TotalMb)).ToList();
        var critical = low.Where(x => x.pct < t.VolumeFreeCriticalPct).ToList();
        var warning = low.Where(x => x.pct >= t.VolumeFreeCriticalPct && x.pct < t.VolumeFreeWarningPct).ToList();
        if (critical.Count > 0)
        {
            d.Add(new("DISK_CRITICAL", 15, $"Disk almost full: {string.Join(", ", critical.Select(x => $"{x.v.MountPoint} {x.pct:0}% free"))}."));
        }
        else if (warning.Count > 0)
        {
            d.Add(new("DISK_LOW", 5, $"Disk space low: {string.Join(", ", warning.Select(x => $"{x.v.MountPoint} {x.pct:0}% free"))}."));
        }

        int criticalConfig = input.ConfigAdvice.Count(a => a.Severity == AdviceSeverity.Critical);
        if (criticalConfig > 0)
        {
            d.Add(new("CONFIG_CRITICAL", Math.Min(10, 5 * criticalConfig), $"{criticalConfig} critical configuration problem(s)."));
        }

        int score = Math.Max(0, 100 - d.Sum(x => x.Points));
        return new HealthResult(score, d);
    }

    private static void AddDatabaseRule(List<HealthDeduction> d, IReadOnlyList<AdviceItem> advice, string code, string rule, int points, string text)
    {
        var dbs = advice.Where(a => a.Code == code).Select(a => a.Target).Distinct().ToList();
        if (dbs.Count == 0) return;
        var names = string.Join(", ", dbs.Take(5)) + (dbs.Count > 5 ? $" and {dbs.Count - 5} more" : string.Empty);
        d.Add(new(rule, points, $"{dbs.Count} {text}: {names}."));
    }
}
