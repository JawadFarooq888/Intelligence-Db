using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IntelligenceDB.IntegrationTests;

/// <summary>
/// A real SQL Server for integration tests.
/// If IDB_TEST_SQL_CONNECTION is set (e.g. the devcontainer's "sql" service) that server is used;
/// otherwise a throw-away SQL Server 2022 Docker container is started with Testcontainers.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("IDB_TEST_SQL_CONNECTION");
        if (!string.IsNullOrWhiteSpace(external))
        {
            ConnectionString = external;
            return;
        }

        // Newer Testcontainers versions mark the parameterless builder as obsolete in favour of an image argument;
        // WithImage below sets the image explicitly either way.
#pragma warning disable CS0618
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
#pragma warning restore CS0618
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    public SqlConnectionStringBuilder Builder => new(ConnectionString);

    public async Task ExecuteAsync(string sql)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}
