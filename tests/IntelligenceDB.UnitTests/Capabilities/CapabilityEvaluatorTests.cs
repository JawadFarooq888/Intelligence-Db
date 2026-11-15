using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.UnitTests.Capabilities;

public class CapabilityEvaluatorTests
{
    private static CapabilityResult Get(IReadOnlyList<CapabilityResult> results, string key) =>
        results.Single(r => r.Key == key);

    [Fact]
    public void Fully_granted_on_prem_server_supports_everything_except_error_log()
    {
        var results = CapabilityEvaluator.Evaluate(PlatformType.OnPremises, Facts.Box(), Facts.FullAdmin());

        results.Where(r => !r.IsSupported).ShouldHaveSingleItem().Key.ShouldBe(CapabilityKeys.ErrorLog);
    }

    [Fact]
    public void Every_catalog_key_is_evaluated_exactly_once()
    {
        var results = CapabilityEvaluator.Evaluate(PlatformType.OnPremises, Facts.Box(), AdminProbeOutcome.NotConfigured);
        string.Join(",", results.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal))
            .ShouldBe(string.Join(",", CapabilityKeys.Catalog.Keys.OrderBy(k => k, StringComparer.Ordinal)));
    }

    [Fact]
    public void Azure_sql_database_has_no_agent_msdb_volumes_or_config_change()
    {
        var results = CapabilityEvaluator.Evaluate(PlatformType.AzureSqlDatabase, Facts.AzureSqlDb(), Facts.FullAdmin());

        foreach (var key in new[]
                 {
                     CapabilityKeys.SqlAgent, CapabilityKeys.MsdbAccess, CapabilityKeys.BackupHistory,
                     CapabilityKeys.VolumeStats, CapabilityKeys.ErrorLog, CapabilityKeys.SystemHealthSession,
                     CapabilityKeys.ServerConfigurationChange, CapabilityKeys.AgentJobControl,
                 })
        {
            var r = Get(results, key);
            r.IsSupported.ShouldBeFalse(key);
            r.Reason.ShouldNotBeNullOrWhiteSpace(key);
            r.Alternative.ShouldNotBeNullOrWhiteSpace(key);
        }

        Get(results, CapabilityKeys.QueryStore).IsSupported.ShouldBeTrue();
        Get(results, CapabilityKeys.OnlineIndexOperations).IsSupported.ShouldBeTrue();
        Get(results, CapabilityKeys.SqlAgent).Alternative!.ShouldContain("IntelligenceDB");
    }

    [Fact]
    public void Express_edition_has_no_agent()
    {
        var facts = Facts.Box() with { EngineEdition = 4, Edition = "Express Edition (64-bit)", IsAgentRunning = false };
        var agent = Get(CapabilityEvaluator.Evaluate(PlatformType.OnPremises, facts, Facts.FullAdmin()), CapabilityKeys.SqlAgent);

        agent.IsSupported.ShouldBeFalse();
        agent.Reason!.ShouldContain("Express");
    }

    [Fact]
    public void Stopped_agent_is_reported_and_blocks_job_control()
    {
        var facts = Facts.Box() with { IsAgentRunning = false };
        var results = CapabilityEvaluator.Evaluate(PlatformType.OnPremises, facts, Facts.FullAdmin());

        Get(results, CapabilityKeys.SqlAgent).Reason!.ShouldContain("not running");
        Get(results, CapabilityKeys.AgentJobControl).IsSupported.ShouldBeFalse();
    }

    [Fact]
    public void Unknown_agent_state_is_still_supported_with_a_note()
    {
        var facts = Facts.Box() with { IsAgentRunning = null };
        var agent = Get(CapabilityEvaluator.Evaluate(PlatformType.OnPremises, facts, Facts.FullAdmin()), CapabilityKeys.SqlAgent);

        agent.IsSupported.ShouldBeTrue();
        agent.Reason.ShouldNotBeNull();
    }

    [Fact]
    public void Missing_msdb_access_disables_agent_and_backups_with_fix()
    {
        var facts = Facts.Box() with { HasMsdbAccess = false };
        var results = CapabilityEvaluator.Evaluate(PlatformType.OnPremises, facts, AdminProbeOutcome.NotConfigured);

        Get(results, CapabilityKeys.SqlAgent).IsSupported.ShouldBeFalse();
        Get(results, CapabilityKeys.BackupHistory).IsSupported.ShouldBeFalse();
        Get(results, CapabilityKeys.MsdbAccess).Alternative!.ShouldContain("SQLAgentReaderRole");
    }

    [Fact]
    public void Missing_view_server_state_disables_dmvs_and_deadlocks()
    {
        var facts = Facts.Box() with { HasViewServerState = false };
        var results = CapabilityEvaluator.Evaluate(PlatformType.OnPremises, facts, AdminProbeOutcome.NotConfigured);

        Get(results, CapabilityKeys.ServerStateDmvs).IsSupported.ShouldBeFalse();
        Get(results, CapabilityKeys.SystemHealthSession).IsSupported.ShouldBeFalse();
    }

    [Theory]
    [InlineData(PlatformType.AwsRds, "parameter group")]
    [InlineData(PlatformType.GoogleCloudSql, "database flags")]
    public void Managed_box_platforms_cannot_change_instance_settings(PlatformType platform, string hint)
    {
        var results = CapabilityEvaluator.Evaluate(platform, Facts.Box(), Facts.FullAdmin());
        var config = Get(results, CapabilityKeys.ServerConfigurationChange);

        config.IsSupported.ShouldBeFalse();
        config.Alternative!.ShouldContain(hint);
    }

    [Fact]
    public void No_admin_login_disables_all_control_actions()
    {
        var results = CapabilityEvaluator.Evaluate(PlatformType.OnPremises, Facts.Box(), AdminProbeOutcome.NotConfigured);

        foreach (var key in new[]
                 {
                     CapabilityKeys.KillSession, CapabilityKeys.AgentJobControl,
                     CapabilityKeys.ServerConfigurationChange, CapabilityKeys.IndexMaintenance,
                 })
        {
            var r = Get(results, key);
            r.IsSupported.ShouldBeFalse(key);
            r.Reason!.ShouldContain("No admin login");
        }
    }

    [Fact]
    public void Failed_admin_login_reports_the_error()
    {
        var admin = new AdminProbeOutcome(true, null, "Login failed");
        var kill = Get(CapabilityEvaluator.Evaluate(PlatformType.OnPremises, Facts.Box(), admin), CapabilityKeys.KillSession);

        kill.IsSupported.ShouldBeFalse();
        kill.Reason!.ShouldContain("Login failed");
    }

    [Fact]
    public void Least_privilege_admin_without_alter_any_connection_cannot_kill()
    {
        var admin = new AdminProbeOutcome(true, new AdminFacts(false, false, true, true), null);
        var results = CapabilityEvaluator.Evaluate(PlatformType.OnPremises, Facts.Box(), admin);

        Get(results, CapabilityKeys.KillSession).IsSupported.ShouldBeFalse();
        Get(results, CapabilityKeys.AgentJobControl).IsSupported.ShouldBeTrue();
        Get(results, CapabilityKeys.ServerConfigurationChange).IsSupported.ShouldBeTrue();
    }

    [Theory]
    [InlineData(2, false)] // Standard
    [InlineData(3, true)]  // Enterprise / Developer
    [InlineData(4, false)] // Express
    [InlineData(8, true)]  // Managed Instance
    public void Online_index_rebuild_depends_on_edition(int engineEdition, bool expected)
    {
        var facts = Facts.Box() with { EngineEdition = engineEdition };
        Get(CapabilityEvaluator.Evaluate(PlatformType.OnPremises, facts, AdminProbeOutcome.NotConfigured),
            CapabilityKeys.OnlineIndexOperations).IsSupported.ShouldBe(expected);
    }

    [Fact]
    public void Sql_2014_has_no_query_store()
    {
        var facts = Facts.Box() with { MajorVersion = 12 };
        Get(CapabilityEvaluator.Evaluate(PlatformType.OnPremises, facts, AdminProbeOutcome.NotConfigured),
            CapabilityKeys.QueryStore).IsSupported.ShouldBeFalse();
    }

    [Fact]
    public void Never_throws_and_always_explains_for_any_combination()
    {
        // Exhaustive sweep: every platform x every boolean fact x admin states.
        var platforms = Enum.GetValues<PlatformType>();
        var admins = new[]
        {
            AdminProbeOutcome.NotConfigured,
            new AdminProbeOutcome(true, null, "boom"),
            new AdminProbeOutcome(true, new AdminFacts(false, false, false, false), null),
            Facts.FullAdmin(),
        };

        foreach (var platform in platforms)
        {
            for (int mask = 0; mask < 64; mask++)
            {
                var facts = Facts.Box() with
                {
                    IsSysadmin = (mask & 1) != 0,
                    HasViewServerState = (mask & 2) != 0,
                    HasViewAnyDefinition = (mask & 4) != 0,
                    HasMsdbAccess = (mask & 8) != 0,
                    CanReadVolumeStats = (mask & 16) != 0,
                    CanReadErrorLog = (mask & 32) != 0,
                };

                foreach (var admin in admins)
                {
                    var results = CapabilityEvaluator.Evaluate(platform, facts, admin);
                    results.Count.ShouldBe(CapabilityKeys.Catalog.Count);
                    foreach (var r in results.Where(r => !r.IsSupported))
                    {
                        r.Reason.ShouldNotBeNullOrWhiteSpace($"{platform}/{mask}/{r.Key}");
                    }
                }
            }
        }
    }
}
