using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.UnitTests.Capabilities;

public class PlatformDetectorTests
{
    [Theory]
    [InlineData(5, PlatformType.AzureSqlDatabase)]
    [InlineData(8, PlatformType.AzureSqlManagedInstance)]
    [InlineData(2, PlatformType.OnPremises)]
    [InlineData(3, PlatformType.OnPremises)]
    [InlineData(4, PlatformType.OnPremises)]
    [InlineData(6, PlatformType.Unknown)]   // Synapse is not a supported target
    [InlineData(9, PlatformType.Unknown)]   // SQL Edge
    public void Engine_edition_decides_the_platform(int engineEdition, PlatformType expected)
    {
        var facts = Facts.Box() with { EngineEdition = engineEdition };
        PlatformDetector.Detect(facts, PlatformType.Unknown).ShouldBe(expected);
    }

    [Fact]
    public void Rdsadmin_database_means_aws_rds()
    {
        var facts = Facts.Box() with { HasRdsAdminDatabase = true };
        PlatformDetector.Detect(facts, PlatformType.OnPremises).ShouldBe(PlatformType.AwsRds);
    }

    [Fact]
    public void Cloud_sql_marker_means_google_cloud_sql()
    {
        var facts = Facts.Box() with { HasCloudSqlMarker = true };
        PlatformDetector.Detect(facts, PlatformType.Unknown).ShouldBe(PlatformType.GoogleCloudSql);
    }

    [Theory]
    [InlineData(PlatformType.AwsRds)]
    [InlineData(PlatformType.GoogleCloudSql)]
    public void Declared_cloud_box_platform_is_trusted_when_markers_are_hidden(PlatformType declared)
    {
        PlatformDetector.Detect(Facts.Box(), declared).ShouldBe(declared);
    }

    [Fact]
    public void Declared_azure_is_overridden_by_detected_box_product()
    {
        // Declaring Azure SQL Database for a box server must not hide box features.
        PlatformDetector.Detect(Facts.Box(), PlatformType.AzureSqlDatabase).ShouldBe(PlatformType.OnPremises);
    }

    [Fact]
    public void Mismatch_warning_only_when_both_known_and_different()
    {
        PlatformDetector.MismatchWarning(PlatformType.Unknown, PlatformType.OnPremises).ShouldBeNull();
        PlatformDetector.MismatchWarning(PlatformType.OnPremises, PlatformType.OnPremises).ShouldBeNull();
        PlatformDetector.MismatchWarning(PlatformType.AwsRds, PlatformType.AzureSqlDatabase).ShouldNotBeNull();
    }
}

internal static class Facts
{
    /// <summary>A healthy on-premises Developer edition server with a fully granted monitoring login.</summary>
    public static ProbeFacts Box() => new(
        EngineEdition: 3,
        MajorVersion: 16,
        Edition: "Developer Edition (64-bit)",
        HasRdsAdminDatabase: false,
        HasCloudSqlMarker: false,
        IsSysadmin: false,
        HasViewServerState: true,
        HasViewAnyDefinition: true,
        HasMsdbAccess: true,
        IsAgentRunning: true,
        CanReadVolumeStats: true,
        CanReadErrorLog: false);

    public static ProbeFacts AzureSqlDb() => Box() with
    {
        EngineEdition = 5,
        MajorVersion = 12,
        Edition = "SQL Azure",
        HasMsdbAccess = false,
        IsAgentRunning = null,
        CanReadVolumeStats = false,
    };

    public static AdminProbeOutcome FullAdmin() =>
        new(true, new AdminFacts(IsSysadmin: true, HasAlterAnyConnection: true, HasAlterSettings: true, CanControlAgentJobs: true), null);
}
