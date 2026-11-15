using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Application.Monitoring;
using IntelligenceDB.Domain.Servers;
using IntelligenceDB.Targets;
using IntelligenceDB.Targets.Collectors;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntelligenceDB.IntegrationTests;

/// <summary>Runs every collector against a real SQL Server and checks the collected data.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class CollectionEngineTests(SqlServerFixture sql)
{
    private readonly SqlTargetConnectionFactory _factory = new();

    private TargetConnectionSpec Spec()
    {
        var b = sql.Builder;
        var parts = b.DataSource.Split(',', 2);
        return new TargetConnectionSpec
        {
            Host = parts[0],
            Port = parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : null,
            AuthType = AuthType.SqlLogin,
            UserName = b.UserID,
            Secret = b.Password,
            TrustServerCertificate = true,
            Platform = PlatformType.OnPremises,
        };
    }

    private async Task<CollectionTarget> TargetAsync()
    {
        var probe = new ServerProbe(_factory, NullLogger<ServerProbe>.Instance);
        var result = await probe.ProbeAsync(Spec(), null, CancellationToken.None);
        result.Success.ShouldBeTrue(result.Error);
        var caps = CapabilityEvaluator.Evaluate(PlatformType.OnPremises, result.Facts!, AdminProbeOutcome.NotConfigured);
        return new CollectionTarget
        {
            ServerId = 1,
            DisplayName = "test",
            Monitoring = Spec(),
            Platform = PlatformType.OnPremises,
            SupportedCapabilities = caps.Where(c => c.IsSupported).Select(c => c.Key).ToHashSet(),
            MetricIntervalSeconds = 60,
            Fingerprint = "x",
        };
    }

    private CollectionEngine Engine() => new(
        _factory,
        new ServerProbe(_factory, NullLogger<ServerProbe>.Instance),
        new ICollector[]
        {
            new MetricsCollector(NullLogger<MetricsCollector>.Instance), new WaitsCollector(), new FileIoCollector(),
            new DatabasesCollector(NullLogger<DatabasesCollector>.Instance), new BackupsCollector(), new ConfigCollector(),
        },
        TimeProvider.System,
        NullLogger<CollectionEngine>.Instance);

    [Fact]
    public async Task All_collectors_return_data_from_a_real_server()
    {
        var db = "idb_collect_" + Guid.NewGuid().ToString("N")[..8];
        await sql.ExecuteAsync($"CREATE DATABASE [{db}];");
        await sql.ExecuteAsync($"BACKUP DATABASE [{db}] TO DISK = N'/var/opt/mssql/data/{db}.bak' WITH INIT;");
        try
        {
            var engine = Engine();
            engine.UpdateTargets([await TargetAsync()]);
            var batches = await engine.RunDueAsync(4, CancellationToken.None);

            foreach (var b in batches) b.Success.ShouldBeTrue($"{b.Collector}: {b.Error}");
            var byName = batches.ToDictionary(b => b.Collector);

            byName.Keys.ShouldBe(
                [CollectorNames.Probe, CollectorNames.Metrics, CollectorNames.Waits, CollectorNames.FileIo,
                 CollectorNames.Databases, CollectorNames.Backups, CollectorNames.Config], ignoreOrder: true);

            var metric = byName[CollectorNames.Metrics].Metric!;
            metric.PageLifeExpectancy.ShouldNotBeNull();
            metric.TotalServerMemoryMb.ShouldNotBeNull();
            metric.UserConnections.ShouldNotBeNull();

            var dbs = byName[CollectorNames.Databases].Databases!;
            dbs.Databases.Select(d => d.Name).ShouldContain("master");
            dbs.Databases.Select(d => d.Name).ShouldContain(db);
            var files = dbs.Files.Where(f => f.DatabaseId == dbs.Databases.Single(d => d.Name == db).DatabaseId).ToList();
            files.ShouldContain(f => f.TypeDesc == "ROWS" && f.UsedMb > 0);
            files.ShouldContain(f => f.TypeDesc == "LOG");
            dbs.Databases.Single(d => d.Name == db).VlfCount.ShouldNotBeNull();
            byName[CollectorNames.Databases].Volumes!.ShouldNotBeEmpty();

            var backups = byName[CollectorNames.Backups].Backups!;
            backups.Latest.ShouldContain(l => l.DatabaseName == db && l.BackupType == "D");
            backups.History.ShouldContain(h => h.DatabaseName == db && h.SizeBytes > 0);

            byName[CollectorNames.Config].Config!.ShouldContain(c => c.Name == "max server memory (MB)");

            // Nothing is due straight after a full run.
            (await engine.RunDueAsync(4, CancellationToken.None)).ShouldBeEmpty();
        }
        finally
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            await sql.ExecuteAsync($"ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}];");
        }
    }

    [Fact]
    public async Task Wait_stats_are_sent_as_deltas_after_the_first_sample()
    {
        var target = await TargetAsync();
        var collector = new WaitsCollector();
        var state = new CollectorState();

        await using var conn = await _factory.OpenAsync(target.Monitoring, TargetSessionMode.Monitoring, CancellationToken.None);
        var first = new CollectionBatch { StartedUtc = DateTime.UtcNow };
        await collector.CollectAsync(conn, target, state, first, CancellationToken.None);
        first.Waits!.ShouldBeEmpty(); // baseline only

        await sql.ExecuteAsync("WAITFOR DELAY '00:00:01.100';");
        var second = new CollectionBatch { StartedUtc = DateTime.UtcNow };
        await collector.CollectAsync(conn, target, state, second, CancellationToken.None);

        second.Waits.ShouldNotBeNull();
        second.Waits!.ShouldAllBe(w => w.WaitTimeMs > 0 && w.IntervalSeconds >= 1);
    }

    [Fact]
    public async Task Unreachable_server_backs_off_instead_of_retrying_every_tick()
    {
        var engine = Engine();
        var target = await TargetAsync();
        target.Monitoring = new TargetConnectionSpec { Host = "127.0.0.1", Port = 1, AuthType = AuthType.SqlLogin, UserName = "x", Secret = "y", ConnectTimeoutSeconds = 5, TrustServerCertificate = true };
        engine.UpdateTargets([target]);

        var first = await engine.RunDueAsync(4, CancellationToken.None);
        first.ShouldHaveSingleItem().Success.ShouldBeFalse();
        first[0].ConnectionStatus.ShouldBe(ConnectionStatus.Unreachable);

        (await engine.RunDueAsync(4, CancellationToken.None)).ShouldBeEmpty();
    }
}
