using System.Text.Json;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Agents;
using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Application.Monitoring;
using IntelligenceDB.Domain.Servers;
using IntelligenceDB.Targets.Collectors;

namespace IntelligenceDB.UnitTests.Monitoring;

public class AgentKeyTests
{
    [Fact]
    public void Keys_are_random_prefixed_and_only_hashes_are_kept()
    {
        var (k1, h1) = AgentService.NewKey();
        var (k2, h2) = AgentService.NewKey();

        k1.ShouldStartWith("idbk_");
        k1.ShouldNotBe(k2);
        h1.ShouldNotBe(h2);
        h1.ShouldNotContain(k1);
        AgentService.HashKey(k1).ShouldBe(h1);
        h1.Length.ShouldBe(64); // SHA-256 hex
    }
}

public class CheckDbDateTests
{
    [Fact]
    public void Never_unknown_and_local_times_are_mapped()
    {
        DatabasesCollector.CheckDbUtc(null, 0).ShouldBeNull();
        DatabasesCollector.CheckDbUtc(new DateTime(1900, 1, 1), 3600).ShouldBe(Database.NeverChecked);

        var utc = DatabasesCollector.CheckDbUtc(new DateTime(2026, 11, 1, 10, 0, 0), -3600);
        utc.ShouldBe(new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc));
        utc!.Value.Kind.ShouldBe(DateTimeKind.Utc);
    }
}

public class WireFormatTests
{
    [Fact]
    public void Batches_and_probe_results_survive_the_agent_json_round_trip()
    {
        var probe = new ProbeResult(true, ConnectionStatus.Online, null, 12,
            new ProbeFacts(3, 16, "Developer", false, false, true, true, true, true, true, true, true),
            new ServerInfoSnapshot("16.0.1.1", 16, "RTM", "CU1", "Developer", 3, "Linux", 4, 8192, DateTime.UtcNow, "SQL_Latin1_General_CP1_CI_AS", "sql", true),
            ["VIEW SERVER STATE"], false, null, null);

        var batch = new CollectionBatch
        {
            ServerId = 7,
            Collector = CollectorNames.Probe,
            StartedUtc = DateTime.UtcNow,
            Success = true,
            Probe = probe,
            Waits = [new WaitSample("PAGEIOLATCH_SH", 100, 5, 3, 300)],
        };

        var json = JsonSerializer.Serialize(new List<CollectionBatch> { batch }, WireJson.Options);
        json.ShouldContain("\"Online\""); // enums as text
        var back = JsonSerializer.Deserialize<List<CollectionBatch>>(json, WireJson.Options)!.Single();

        back.ServerId.ShouldBe(7);
        back.Probe!.Facts!.MajorVersion.ShouldBe(16);
        back.Probe.Permissions.ShouldContain("VIEW SERVER STATE");
        back.Waits!.Single().WaitTimeMs.ShouldBe(100);
    }

    [Fact]
    public void Collection_targets_carry_the_connection_spec()
    {
        var target = new CollectionTarget
        {
            ServerId = 1,
            Monitoring = new TargetConnectionSpec { Host = "sql01", AuthType = AuthType.SqlLogin, UserName = "mon", Secret = "pw" },
            SupportedCapabilities = [CapabilityKeys.ServerStateDmvs],
        };

        var back = JsonSerializer.Deserialize<CollectionTarget>(JsonSerializer.Serialize(target, WireJson.Options), WireJson.Options)!;
        back.Monitoring.Host.ShouldBe("sql01");
        back.Monitoring.Secret.ShouldBe("pw");
        back.SupportedCapabilities.ShouldContain(CapabilityKeys.ServerStateDmvs);
    }
}
