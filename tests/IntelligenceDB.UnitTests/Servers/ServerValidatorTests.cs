using IntelligenceDB.Application.Common;
using IntelligenceDB.Application.Servers;
using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.UnitTests.Servers;

public class ServerValidatorTests
{
    private static ServerUpsertRequest Valid() => new()
    {
        DisplayName = "PROD-SQL-01",
        Host = "sql01.contoso.local",
        Monitoring = new CredentialInput { AuthType = AuthType.SqlLogin, UserName = "IntelligenceDB_Monitor", Secret = "x" },
    };

    private static AppValidationException Fails(ServerUpsertRequest r, bool storedMonitoringSecret = false) =>
        Should.Throw<AppValidationException>(() => ServerValidator.Validate(r, storedMonitoringSecret, false));

    [Fact]
    public void Valid_request_passes() => ServerValidator.Validate(Valid(), false, false);

    [Theory]
    [InlineData("sql01;Database=evil")]
    [InlineData("sql01 --")]
    [InlineData("'sql01'")]
    [InlineData("")]
    public void Host_with_unsafe_characters_is_rejected(string host)
    {
        var r = Valid();
        Fails(new ServerUpsertRequest { DisplayName = r.DisplayName, Host = host, Monitoring = r.Monitoring })
            .Errors.Keys.ShouldContain("host");
    }

    [Theory]
    [InlineData("tcp:myserver.database.windows.net")]
    [InlineData("10.0.0.15")]
    [InlineData("sql_01-a.example.com")]
    public void Normal_hosts_are_accepted(string host)
    {
        var r = Valid();
        ServerValidator.Validate(new ServerUpsertRequest { DisplayName = r.DisplayName, Host = host, Monitoring = r.Monitoring }, false, false);
    }

    [Fact]
    public void Azure_sql_database_requires_a_database_name()
    {
        var r = Valid();
        Fails(new ServerUpsertRequest
        {
            DisplayName = r.DisplayName,
            Host = "x.database.windows.net",
            DeclaredPlatform = PlatformType.AzureSqlDatabase,
            Monitoring = r.Monitoring,
        }).Errors.Keys.ShouldContain("databaseName");
    }

    [Fact]
    public void Sql_login_needs_password_unless_one_is_stored()
    {
        var r = new ServerUpsertRequest
        {
            DisplayName = "a",
            Host = "b",
            Monitoring = new CredentialInput { AuthType = AuthType.SqlLogin, UserName = "u" },
        };

        Fails(r).Errors.Keys.ShouldContain("monitoring.secret");
        ServerValidator.Validate(r, monitoringHasStoredSecret: true, adminHasStoredSecret: false);
    }

    [Theory]
    [InlineData(AuthType.Windows)]
    [InlineData(AuthType.EntraManagedIdentity)]
    [InlineData(AuthType.EntraDefault)]
    public void Passwordless_auth_types_need_no_secret(AuthType authType)
    {
        ServerValidator.Validate(new ServerUpsertRequest
        {
            DisplayName = "a",
            Host = "b",
            Monitoring = new CredentialInput { AuthType = authType },
        }, false, false);
    }

    [Fact]
    public void Admin_and_remove_admin_together_is_rejected()
    {
        var r = Valid();
        Fails(new ServerUpsertRequest
        {
            DisplayName = r.DisplayName,
            Host = r.Host,
            Monitoring = r.Monitoring,
            Admin = new CredentialInput { AuthType = AuthType.Windows },
            RemoveAdmin = true,
        }).Errors.Keys.ShouldContain("admin");
    }

    [Fact]
    public void Port_out_of_range_is_rejected()
    {
        var r = Valid();
        Fails(new ServerUpsertRequest { DisplayName = r.DisplayName, Host = r.Host, Port = 70000, Monitoring = r.Monitoring })
            .Errors.Keys.ShouldContain("port");
    }
}
