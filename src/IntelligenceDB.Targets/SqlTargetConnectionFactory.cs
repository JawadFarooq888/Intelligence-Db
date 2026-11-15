using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Domain.Servers;
using Microsoft.Data.SqlClient;

namespace IntelligenceDB.Targets;

public enum TargetSessionMode
{
    /// <summary>Read-only collection: short lock timeout, low deadlock priority, dirty reads of DMVs allowed.</summary>
    Monitoring,

    /// <summary>Approved control actions: normal isolation, longer lock timeout.</summary>
    Admin,
}

public interface ITargetConnectionFactory
{
    Task<SqlConnection> OpenAsync(TargetConnectionSpec spec, TargetSessionMode mode, CancellationToken cancellationToken);
}

/// <summary>
/// Opens connections to monitored servers. Connection strings are built with SqlConnectionStringBuilder,
/// which escapes every value, so user input can never inject extra keywords.
/// </summary>
public sealed class SqlTargetConnectionFactory : ITargetConnectionFactory
{
    // Applied to every monitoring session so a collector query can never wait long on locks
    // and is always the preferred deadlock victim.
    internal const string MonitoringSessionSettings =
        "SET LOCK_TIMEOUT 3000; SET DEADLOCK_PRIORITY LOW; SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;";

    internal const string AdminSessionSettings =
        "SET LOCK_TIMEOUT 10000; SET TRANSACTION ISOLATION LEVEL READ COMMITTED;";

    public async Task<SqlConnection> OpenAsync(TargetConnectionSpec spec, TargetSessionMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var connection = new SqlConnection(BuildConnectionString(spec, mode));
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = mode == TargetSessionMode.Monitoring ? MonitoringSessionSettings : AdminSessionSettings;
            cmd.CommandTimeout = 10;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public static string BuildConnectionString(TargetConnectionSpec spec, TargetSessionMode mode)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var b = new SqlConnectionStringBuilder
        {
            DataSource = BuildDataSource(spec),
            InitialCatalog = string.IsNullOrWhiteSpace(spec.DatabaseName) ? "master" : spec.DatabaseName,
            ApplicationName = mode == TargetSessionMode.Monitoring ? "IntelligenceDB-Monitor" : "IntelligenceDB-Admin",
            ConnectTimeout = spec.ConnectTimeoutSeconds,
            Encrypt = spec.Encrypt ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = spec.TrustServerCertificate,
            Pooling = true,
            MaxPoolSize = 10,
            MultipleActiveResultSets = false,
            ConnectRetryCount = 1,
        };

        switch (spec.AuthType)
        {
            case AuthType.SqlLogin:
                b.UserID = spec.UserName ?? string.Empty;
                b.Password = spec.Secret ?? string.Empty;
                break;

            case AuthType.Windows:
                // Uses the identity of the IntelligenceDB service account (gMSA / domain account).
                b.IntegratedSecurity = true;
                break;

            case AuthType.EntraPassword:
#pragma warning disable CS0618 // ActiveDirectoryPassword is deprecated by Microsoft but still supported; kept for compatibility.
                b.Authentication = SqlAuthenticationMethod.ActiveDirectoryPassword;
#pragma warning restore CS0618
                b.UserID = spec.UserName ?? string.Empty;
                b.Password = spec.Secret ?? string.Empty;
                break;

            case AuthType.EntraServicePrincipal:
                b.Authentication = SqlAuthenticationMethod.ActiveDirectoryServicePrincipal;
                b.UserID = spec.UserName ?? string.Empty; // application (client) id
                b.Password = spec.Secret ?? string.Empty; // client secret
                break;

            case AuthType.EntraManagedIdentity:
                b.Authentication = SqlAuthenticationMethod.ActiveDirectoryManagedIdentity;
                if (!string.IsNullOrWhiteSpace(spec.ClientId))
                {
                    b.UserID = spec.ClientId; // user-assigned identity
                }

                break;

            case AuthType.EntraDefault:
                b.Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(spec), spec.AuthType, "Unsupported authentication type.");
        }

        return b.ConnectionString;
    }

    /// <summary>host[\instance][,port]. When a port is given SQL Server Browser is not needed.</summary>
    internal static string BuildDataSource(TargetConnectionSpec spec)
    {
        var ds = spec.Host.Trim();
        if (!string.IsNullOrWhiteSpace(spec.InstanceName)) ds += "\\" + spec.InstanceName.Trim();
        if (spec.Port is not null) ds += "," + spec.Port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ds;
    }
}
