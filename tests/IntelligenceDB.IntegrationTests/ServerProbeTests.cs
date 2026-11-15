using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Domain.Servers;
using IntelligenceDB.Targets;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntelligenceDB.IntegrationTests;

/// <summary>Capability detection against a real SQL Server.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ServerProbeTests(SqlServerFixture sql)
{
    private readonly ServerProbe _probe = new(new SqlTargetConnectionFactory(), NullLogger<ServerProbe>.Instance);

    private TargetConnectionSpec Spec(string user, string password)
    {
        var b = sql.Builder;
        // Data Source is "host,port" for Testcontainers and "host" or "host,port" otherwise.
        var parts = b.DataSource.Split(',', 2);
        return new TargetConnectionSpec
        {
            Host = parts[0],
            Port = parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : null,
            AuthType = AuthType.SqlLogin,
            UserName = user,
            Secret = password,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeoutSeconds = 15,
        };
    }

    private TargetConnectionSpec SaSpec() => Spec(sql.Builder.UserID, sql.Builder.Password);

    [Fact]
    public async Task Sysadmin_login_is_detected_as_on_prem_with_full_capabilities()
    {
        var result = await _probe.ProbeAsync(SaSpec(), SaSpec(), CancellationToken.None);

        result.Success.ShouldBeTrue(result.Error);
        result.Status.ShouldBe(ConnectionStatus.Online);
        result.Info.ShouldNotBeNull();
        result.Info.MajorVersion.ShouldBeGreaterThanOrEqualTo(16);
        result.Info.IsSysadmin.ShouldBeTrue();
        result.Info.CpuCount.ShouldNotBeNull();
        result.Facts.ShouldNotBeNull();

        var platform = PlatformDetector.Detect(result.Facts, PlatformType.Unknown);
        platform.ShouldBe(PlatformType.OnPremises);

        var caps = CapabilityEvaluator.Evaluate(platform, result.Facts,
            new AdminProbeOutcome(true, result.AdminFacts, result.AdminError));
        caps.Single(c => c.Key == CapabilityKeys.ServerStateDmvs).IsSupported.ShouldBeTrue();
        caps.Single(c => c.Key == CapabilityKeys.MsdbAccess).IsSupported.ShouldBeTrue();
        caps.Single(c => c.Key == CapabilityKeys.KillSession).IsSupported.ShouldBeTrue();
        result.Permissions.ShouldContain("VIEW SERVER STATE");
    }

    [Fact]
    public async Task Login_without_grants_gets_clear_unsupported_capabilities()
    {
        var login = "idb_test_nogrants_" + Guid.NewGuid().ToString("N")[..8];
        const string password = "Test!Passw0rd_NoGrants";
        await sql.ExecuteAsync($"CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;");
        try
        {
            var result = await _probe.ProbeAsync(Spec(login, password), null, CancellationToken.None);

            result.Success.ShouldBeTrue(result.Error);
            result.Facts.ShouldNotBeNull();
            result.Facts.HasViewServerState.ShouldBeFalse();
            result.Facts.HasMsdbAccess.ShouldBeFalse();
            result.Facts.IsSysadmin.ShouldBeFalse();

            var caps = CapabilityEvaluator.Evaluate(PlatformType.OnPremises, result.Facts, AdminProbeOutcome.NotConfigured);
            var dmvs = caps.Single(c => c.Key == CapabilityKeys.ServerStateDmvs);
            dmvs.IsSupported.ShouldBeFalse();
            dmvs.Reason!.ShouldContain("VIEW SERVER STATE");
            caps.Single(c => c.Key == CapabilityKeys.SqlAgent).IsSupported.ShouldBeFalse();
        }
        finally
        {
            await sql.DropLoginAsync(login);
        }
    }

    [Fact]
    public async Task Monitoring_script_grants_give_monitoring_capabilities()
    {
        var login = "idb_test_monitor_" + Guid.NewGuid().ToString("N")[..8];
        const string password = "Test!Passw0rd_Monitor";
        await sql.ExecuteAsync($"""
            CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;
            GRANT VIEW SERVER STATE TO [{login}];
            GRANT VIEW ANY DEFINITION TO [{login}];
            GRANT CONNECT ANY DATABASE TO [{login}];
            EXEC ('USE msdb; CREATE USER [{login}] FOR LOGIN [{login}]; ALTER ROLE SQLAgentReaderRole ADD MEMBER [{login}]; ALTER ROLE db_datareader ADD MEMBER [{login}];');
            """);
        try
        {
            var result = await _probe.ProbeAsync(Spec(login, password), null, CancellationToken.None);

            result.Success.ShouldBeTrue(result.Error);
            result.Facts.ShouldNotBeNull();
            result.Facts.HasViewServerState.ShouldBeTrue();
            result.Facts.HasViewAnyDefinition.ShouldBeTrue();
            result.Facts.HasMsdbAccess.ShouldBeTrue();
            result.Facts.CanReadVolumeStats.ShouldBeTrue();
            result.Facts.IsSysadmin.ShouldBeFalse();
        }
        finally
        {
            await sql.DropLoginAsync(login, dropMsdbUser: true);
        }
    }

    [Fact]
    public async Task Wrong_password_is_auth_failed_with_advice()
    {
        var result = await _probe.ProbeAsync(Spec(sql.Builder.UserID, "definitely-wrong"), null, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.Status.ShouldBe(ConnectionStatus.AuthFailed);
        result.Error!.ShouldContain("Login failed");
        result.Error!.ShouldNotContain("definitely-wrong");
    }

    [Fact]
    public async Task Unreachable_host_is_reported_not_thrown()
    {
        var spec = new TargetConnectionSpec
        {
            Host = "127.0.0.1",
            Port = 1, // nothing listens here
            AuthType = AuthType.SqlLogin,
            UserName = "x",
            Secret = "y",
            TrustServerCertificate = true,
            ConnectTimeoutSeconds = 5,
        };

        var result = await _probe.ProbeAsync(spec, null, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.Status.ShouldBe(ConnectionStatus.Unreachable);
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }
}
