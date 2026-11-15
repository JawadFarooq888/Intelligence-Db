using IntelligenceDB.Application.Monitoring;
using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.UnitTests.Monitoring;

public class ConfigAdvisorTests
{
    private static ServerInfo Info(int cpus = 16, long memMb = 65536, int major = 16, string level = "RTM", string? cu = "CU14") => new()
    {
        CpuCount = cpus,
        PhysicalMemoryMb = memMb,
        MajorVersion = major,
        ProductVersion = $"{major}.0.4135.4",
        ProductLevel = level,
        ProductUpdateLevel = cu,
        EngineEdition = 3,
        Edition = "Developer Edition",
    };

    private static Dictionary<string, long> Defaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["max server memory (MB)"] = 2147483647,
        ["max degree of parallelism"] = 0,
        ["cost threshold for parallelism"] = 5,
        ["optimize for ad hoc workloads"] = 0,
        ["backup compression default"] = 0,
        ["remote admin connections"] = 0,
        ["priority boost"] = 0,
        ["xp_cmdshell"] = 0,
    };

    [Fact]
    public void Out_of_the_box_settings_produce_the_classic_findings()
    {
        var codes = ConfigAdvisor.Evaluate(Defaults(), Info(), [], null).Select(a => a.Code).ToList();

        codes.ShouldContain("CFG_MAXMEM_DEFAULT");
        codes.ShouldContain("CFG_MAXDOP");
        codes.ShouldContain("CFG_CTFP");
        codes.ShouldContain("CFG_ADHOC");
        codes.ShouldNotContain("CFG_PRIORITY_BOOST");
    }

    [Fact]
    public void Max_memory_recommendation_leaves_memory_for_the_os()
    {
        var item = ConfigAdvisor.Evaluate(Defaults(), Info(memMb: 65536), [], null).Single(a => a.Code == "CFG_MAXMEM_DEFAULT");
        // 65536 - max(4096, 65536 / 10 = 6553) = 58983 MB
        item.Recommended.ShouldBe("58983 MB");
    }

    [Fact]
    public void Maxdop_zero_is_fine_on_small_servers()
    {
        ConfigAdvisor.Evaluate(Defaults(), Info(cpus: 4), [], null).ShouldNotContain(a => a.Code == "CFG_MAXDOP");
    }

    [Fact]
    public void Tuned_server_has_no_warnings()
    {
        var tuned = Defaults();
        tuned["max server memory (MB)"] = 57000;
        tuned["max degree of parallelism"] = 8;
        tuned["cost threshold for parallelism"] = 50;
        tuned["optimize for ad hoc workloads"] = 1;
        tuned["backup compression default"] = 1;
        tuned["remote admin connections"] = 1;
        var tempdb = Enumerable.Range(1, 8).Select(i => new DatabaseFile { FileId = i, TypeDesc = "ROWS", SizeMb = 1024 }).ToList();

        ConfigAdvisor.Evaluate(tuned, Info(), tempdb, null).ShouldBeEmpty();
    }

    [Fact]
    public void Priority_boost_is_critical()
    {
        var c = Defaults();
        c["priority boost"] = 1;
        ConfigAdvisor.Evaluate(c, Info(), [], null).Single(a => a.Code == "CFG_PRIORITY_BOOST").Severity.ShouldBe(AdviceSeverity.Critical);
    }

    [Fact]
    public void Tempdb_with_one_file_on_many_cpus_is_flagged()
    {
        var tempdb = new List<DatabaseFile> { new() { FileId = 1, TypeDesc = "ROWS", SizeMb = 8 }, new() { FileId = 2, TypeDesc = "LOG", SizeMb = 8 } };
        ConfigAdvisor.Evaluate(new Dictionary<string, long>(), Info(cpus: 16), tempdb, null).ShouldContain(a => a.Code == "TEMPDB_FILES" && a.Recommended == "8");
    }

    [Fact]
    public void Rtm_without_cumulative_update_is_flagged()
    {
        ConfigAdvisor.Evaluate(new Dictionary<string, long>(), Info(cu: null), [], null).ShouldContain(a => a.Code == "VER_NO_CU");
        ConfigAdvisor.Evaluate(new Dictionary<string, long>(), Info(cu: "CU14"), [], null).ShouldNotContain(a => a.Code == "VER_NO_CU");
    }

    [Fact]
    public void Older_build_than_configured_latest_is_flagged()
    {
        var latest = new LatestBuild(16, "16.0.4200.1", "SQL Server 2022 CU20");
        ConfigAdvisor.Evaluate(new Dictionary<string, long>(), Info(), [], latest).ShouldContain(a => a.Code == "VER_OUTDATED_CU");
        ConfigAdvisor.Evaluate(new Dictionary<string, long>(), Info(), [], new LatestBuild(16, "16.0.4135.4", "same")).ShouldNotContain(a => a.Code == "VER_OUTDATED_CU");
    }
}

public class DatabaseAdvisorTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly MonitoringThresholds T = new();

    private static Database Db(string recovery = "FULL") => new()
    {
        Name = "Sales",
        State = "ONLINE",
        RecoveryModel = recovery,
        CompatibilityLevel = 160,
        LogReuseWait = "NOTHING",
        LastFullBackupUtc = Now.AddDays(-1),
        LastLogBackupUtc = Now.AddMinutes(-15),
        LastGoodCheckDbUtc = Now.AddDays(-3),
        VlfCount = 50,
    };

    [Fact]
    public void Healthy_database_has_no_findings() =>
        DatabaseAdvisor.Evaluate(Db(), true, T, Now, 160).ShouldBeEmpty();

    [Fact]
    public void Missing_full_backup_is_critical()
    {
        var d = Db();
        d.LastFullBackupUtc = null;
        DatabaseAdvisor.Evaluate(d, true, T, Now, 160).Single(a => a.Code == "DB_NO_FULL_BACKUP").Severity.ShouldBe(AdviceSeverity.Critical);
    }

    [Fact]
    public void Backup_rules_are_skipped_when_history_is_not_available()
    {
        var d = Db();
        d.LastFullBackupUtc = null;
        DatabaseAdvisor.Evaluate(d, backupHistoryAvailable: false, T, Now, 160).ShouldNotContain(a => a.Code == "DB_NO_FULL_BACKUP");
    }

    [Fact]
    public void Log_backups_only_required_in_full_recovery()
    {
        var full = Db("FULL");
        full.LastLogBackupUtc = Now.AddDays(-3);
        DatabaseAdvisor.Evaluate(full, true, T, Now, 160).ShouldContain(a => a.Code == "DB_NO_LOG_BACKUP");

        var simple = Db("SIMPLE");
        simple.LastLogBackupUtc = null;
        DatabaseAdvisor.Evaluate(simple, true, T, Now, 160).ShouldNotContain(a => a.Code == "DB_NO_LOG_BACKUP");
    }

    [Fact]
    public void Checkdb_never_and_overdue_are_flagged_but_unknown_is_not()
    {
        var never = Db();
        never.LastGoodCheckDbUtc = Database.NeverChecked;
        DatabaseAdvisor.Evaluate(never, true, T, Now, 160).Single(a => a.Code == "DB_CHECKDB").Title.ShouldContain("never");

        var old = Db();
        old.LastGoodCheckDbUtc = Now.AddDays(-30);
        DatabaseAdvisor.Evaluate(old, true, T, Now, 160).ShouldContain(a => a.Code == "DB_CHECKDB");

        var unknown = Db();
        unknown.LastGoodCheckDbUtc = null; // version does not report it
        DatabaseAdvisor.Evaluate(unknown, true, T, Now, 160).ShouldNotContain(a => a.Code == "DB_CHECKDB");
    }

    [Fact]
    public void Offline_database_is_critical_and_skips_other_rules()
    {
        var d = Db();
        d.State = "SUSPECT";
        d.LastFullBackupUtc = null;
        var items = DatabaseAdvisor.Evaluate(d, true, T, Now, 160);
        items.ShouldHaveSingleItem().Code.ShouldBe("DB_STATE");
    }

    [Fact]
    public void File_settings_are_checked()
    {
        var d = Db();
        d.IsAutoShrinkOn = true;
        d.VlfCount = 5000;
        d.Files.Add(new DatabaseFile { FileId = 1, LogicalName = "Sales", TypeDesc = "ROWS", IsPercentGrowth = true, GrowthValue = 10 });
        d.Files.Add(new DatabaseFile { FileId = 2, LogicalName = "Sales_log", TypeDesc = "LOG", GrowthValue = 1, UsedMb = 950, MaxSizeMb = 1000 });

        var codes = DatabaseAdvisor.Evaluate(d, true, T, Now, 160).Select(a => a.Code).ToList();
        codes.ShouldContain("DB_AUTO_SHRINK");
        codes.ShouldContain("DB_VLF");
        codes.ShouldContain("FILE_PERCENT_GROWTH");
        codes.ShouldContain("FILE_SMALL_GROWTH");
        codes.ShouldContain("FILE_NEAR_MAX");
    }

    [Fact]
    public void Tempdb_needs_no_backups()
    {
        var d = Db("SIMPLE");
        d.Name = "tempdb";
        d.LastFullBackupUtc = null;
        d.LastGoodCheckDbUtc = Database.NeverChecked;
        DatabaseAdvisor.Evaluate(d, true, T, Now, 160).ShouldBeEmpty();
    }
}

public class HealthScoreTests
{
    private static readonly MonitoringThresholds T = new();

    private static HealthInputs Healthy() => new(ConnectionStatus.Online, 20, 5000, 0, 0,
        [new VolumeState("C:\\", 100_000, 60_000)], [], []);

    [Fact]
    public void Healthy_server_scores_100() =>
        HealthScoreCalculator.Calculate(Healthy(), T).Score.ShouldBe(100);

    [Theory]
    [InlineData(ConnectionStatus.Unreachable)]
    [InlineData(ConnectionStatus.AuthFailed)]
    public void Unreachable_server_scores_0(ConnectionStatus status)
    {
        var r = HealthScoreCalculator.Calculate(Healthy() with { Status = status }, T);
        r.Score.ShouldBe(0);
        r.Deductions.ShouldHaveSingleItem();
    }

    [Fact]
    public void Every_lost_point_is_explained()
    {
        var input = Healthy() with
        {
            AvgSqlCpuPct = 95,
            MemoryGrantsPending = 3,
            Volumes = [new VolumeState("D:\\", 100_000, 5_000)],
            DatabaseAdvice =
            [
                new AdviceItem(AdviceSeverity.Critical, "DB_NO_FULL_BACKUP", "x", "x", Target: "Sales"),
                new AdviceItem(AdviceSeverity.Critical, "DB_NO_FULL_BACKUP", "x", "x", Target: "HR"),
            ],
        };

        var r = HealthScoreCalculator.Calculate(input, T);
        r.Score.ShouldBe(100 - r.Deductions.Sum(d => d.Points));
        r.Deductions.Select(d => d.Rule).ShouldBe(["CPU_CRITICAL", "MEMORY_GRANTS", "MISSING_FULL_BACKUP", "DISK_CRITICAL"], ignoreOrder: true);
        r.Deductions.Single(d => d.Rule == "MISSING_FULL_BACKUP").Message.ShouldContain("2 database(s)");
    }

    [Fact]
    public void Score_never_goes_below_zero()
    {
        var advice = new[] { "DB_NO_FULL_BACKUP", "DB_NO_LOG_BACKUP", "DB_CHECKDB", "DB_STATE" }
            .Select(c => new AdviceItem(AdviceSeverity.Critical, c, "x", "x", Target: "a")).ToList();
        var input = Healthy() with
        {
            AvgSqlCpuPct = 99,
            MemoryGrantsPending = 5,
            BlockedSessions = 10,
            Volumes = [new VolumeState("D:\\", 100, 1)],
            DatabaseAdvice = advice,
            ConfigAdvice = [new AdviceItem(AdviceSeverity.Critical, "CFG", "x", "x"), new AdviceItem(AdviceSeverity.Critical, "CFG2", "x", "x")],
        };

        HealthScoreCalculator.Calculate(input, T).Score.ShouldBe(0);
    }

    [Fact]
    public void Unmeasured_values_cost_nothing()
    {
        var input = new HealthInputs(ConnectionStatus.Online, null, null, null, null, [], [], []);
        HealthScoreCalculator.Calculate(input, T).Score.ShouldBe(100);
    }
}

public class WaitTypeCatalogTests
{
    [Theory]
    [InlineData("SLEEP_TASK")]
    [InlineData("LAZYWRITER_SLEEP")]
    [InlineData("XE_TIMER_EVENT")]
    [InlineData("BROKER_TASK_STOP")]
    [InlineData("QDS_PERSIST_TASK_MAIN_LOOP_SLEEP")]
    [InlineData("WAITFOR")]
    public void Background_waits_are_benign(string wait) => WaitTypeCatalog.IsBenign(wait).ShouldBeTrue();

    [Theory]
    [InlineData("PAGEIOLATCH_SH", "Disk reads")]
    [InlineData("LCK_M_X", "Blocking")]
    [InlineData("CXPACKET", "Parallelism")]
    [InlineData("WRITELOG", "Log disk")]
    [InlineData("RESOURCE_SEMAPHORE", "Memory")]
    public void Important_waits_are_explained(string wait, string category)
    {
        WaitTypeCatalog.IsBenign(wait).ShouldBeFalse();
        WaitTypeCatalog.Describe(wait).Category.ShouldBe(category);
    }

    [Fact]
    public void Unknown_waits_still_get_text() =>
        WaitTypeCatalog.Describe("SOMETHING_NEW").Explanation.ShouldNotBeNullOrWhiteSpace();
}
