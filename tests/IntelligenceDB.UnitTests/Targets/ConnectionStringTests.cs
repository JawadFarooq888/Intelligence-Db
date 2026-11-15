using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Servers;
using IntelligenceDB.Domain.Servers;
using IntelligenceDB.Infrastructure.Targets;
using Microsoft.Data.SqlClient;

namespace IntelligenceDB.UnitTests.Targets;

public class ConnectionStringTests
{
    private static TargetConnectionSpec Spec(AuthType auth = AuthType.SqlLogin, string? instance = null, int? port = null, string? db = null) => new()
    {
        Host = "sql01",
        InstanceName = instance,
        Port = port,
        DatabaseName = db,
        AuthType = auth,
        UserName = "mon",
        Secret = "p@ss;word=1",
    };

    [Theory]
    [InlineData(null, null, "sql01")]
    [InlineData("INST1", null, "sql01\\INST1")]
    [InlineData(null, 14333, "sql01,14333")]
    [InlineData("INST1", 14333, "sql01\\INST1,14333")]
    public void Data_source_formats(string? instance, int? port, string expected) =>
        SqlTargetConnectionFactory.BuildDataSource(Spec(instance: instance, port: port)).ShouldBe(expected);

    [Fact]
    public void Password_with_special_characters_cannot_inject_keywords()
    {
        var cs = SqlTargetConnectionFactory.BuildConnectionString(Spec(), TargetSessionMode.Monitoring);
        var parsed = new SqlConnectionStringBuilder(cs);

        parsed.Password.ShouldBe("p@ss;word=1");
        parsed.InitialCatalog.ShouldBe("master");
        parsed.ApplicationName.ShouldBe("IntelligenceDB-Monitor");
    }

    [Fact]
    public void Database_name_is_used_when_given()
    {
        var parsed = new SqlConnectionStringBuilder(
            SqlTargetConnectionFactory.BuildConnectionString(Spec(db: "AppDb"), TargetSessionMode.Admin));
        parsed.InitialCatalog.ShouldBe("AppDb");
        parsed.ApplicationName.ShouldBe("IntelligenceDB-Admin");
    }

    [Fact]
    public void Windows_auth_uses_integrated_security_and_no_password()
    {
        var parsed = new SqlConnectionStringBuilder(
            SqlTargetConnectionFactory.BuildConnectionString(Spec(AuthType.Windows), TargetSessionMode.Monitoring));
        parsed.IntegratedSecurity.ShouldBeTrue();
        parsed.Password.ShouldBeEmpty();
    }

    [Fact]
    public void Managed_identity_uses_entra_authentication()
    {
        var parsed = new SqlConnectionStringBuilder(
            SqlTargetConnectionFactory.BuildConnectionString(Spec(AuthType.EntraManagedIdentity), TargetSessionMode.Monitoring));
        parsed.Authentication.ShouldBe(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity);
    }

    [Fact]
    public void Spec_to_string_never_contains_the_secret() =>
        Spec().ToString().ShouldNotContain("p@ss");

    [Theory]
    [InlineData(16, null, null, "SQL Server 2022")]
    [InlineData(15, "RTM", "CU27", "SQL Server 2019 RTM CU27")]
    [InlineData(17, "RTM", null, "SQL Server 2025 RTM")]
    public void Version_labels(int major, string? level, string? cu, string expected) =>
        VersionNames.Describe(3, major, level, cu).ShouldBe(expected);

    [Theory]
    [InlineData("16.0.4135.4", 16)]
    [InlineData("12.0.2000.8", 12)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void Major_version_parsing(string? version, int expected) =>
        VersionNames.ParseMajor(version).ShouldBe(expected);
}
