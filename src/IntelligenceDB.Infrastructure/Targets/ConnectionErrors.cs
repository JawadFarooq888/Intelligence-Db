using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Domain.Servers;
using Microsoft.Data.SqlClient;

namespace IntelligenceDB.Infrastructure.Targets;

/// <summary>Turns connection errors into plain-English advice. The original message is kept for DBAs.</summary>
internal static class ConnectionErrors
{
    public static string Describe(Exception ex, TargetConnectionSpec spec)
    {
        string advice = ex is SqlException sql ? DescribeSql(sql, spec) : DescribeOther(ex);

        if (spec.AuthType == AuthType.Windows && !OperatingSystem.IsWindows())
        {
            advice += " Note: Windows authentication from a Linux container needs Kerberos to be configured; " +
                      "run IntelligenceDB on a domain-joined Windows host or use SQL / Entra authentication.";
        }

        var original = ex is SqlException s ? $"SQL error {s.Number}: {s.Message}" : ex.Message;
        return $"{advice} ({original})";
    }

    private static string DescribeSql(SqlException ex, TargetConnectionSpec spec)
    {
        if (ex.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase))
        {
            return "The server's TLS certificate is not trusted. Install a trusted certificate on the server, " +
                   "or (test environments only) tick 'Trust server certificate'.";
        }

        return ex.Number switch
        {
            18456 => "Login failed. Check the user name and password, and that the login exists and is enabled on the server.",
            18470 => "The login is disabled on the server.",
            18487 or 18488 => "The login's password has expired or must be changed.",
            4060 => $"The login works but cannot open database '{spec.DatabaseName ?? "master"}'. Check the database name and that the login has a user in it.",
            40615 => "The Azure SQL firewall blocks this IP address. Add a firewall rule for the IntelligenceDB host.",
            40532 => "Azure SQL cannot open the server requested by the login. Check the server name.",
            -2 or 258 => "The connection timed out. Check network, firewall and that the server is running.",
            53 or 2 or 11001 or 26 or -1 or 40 => "Could not reach the server. Check the host name, port or instance name, firewalls, and that SQL Server accepts remote TCP connections.",
            _ => "Could not connect to the server.",
        };
    }

    private static string DescribeOther(Exception ex)
    {
        var typeName = ex.GetType().FullName ?? string.Empty;
        if (typeName.Contains("Msal", StringComparison.Ordinal) || typeName.Contains("Azure.Identity", StringComparison.Ordinal))
        {
            return "Microsoft Entra ID sign-in failed. Check the tenant, client id / user name and secret, and that the identity has a login or user on the server.";
        }

        return "Could not connect to the server.";
    }
}
