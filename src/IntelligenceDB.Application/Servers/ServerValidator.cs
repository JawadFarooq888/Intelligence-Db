using System.Text.RegularExpressions;
using IntelligenceDB.Application.Common;
using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.Application.Servers;

/// <summary>
/// Validates server input. Values end up in a connection string (built with SqlConnectionStringBuilder,
/// which escapes them), never in T-SQL text, but we still restrict them to sane characters.
/// </summary>
public static partial class ServerValidator
{
    [GeneratedRegex(@"^(tcp:)?[A-Za-z0-9][A-Za-z0-9.\-_]*$")]
    private static partial Regex HostRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_$#\-]{0,15}$")]
    private static partial Regex InstanceRegex();

    [GeneratedRegex(@"^[\w\-. ]{1,50}$")]
    private static partial Regex TagRegex();

    public static void Validate(ServerUpsertRequest r, bool monitoringHasStoredSecret, bool adminHasStoredSecret)
    {
        ArgumentNullException.ThrowIfNull(r);
        var e = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(r.DisplayName)) e.Add("displayName", "Display name is required.");
        else if (r.DisplayName.Trim().Length > 128) e.Add("displayName", "Display name must be 128 characters or fewer.");

        if (string.IsNullOrWhiteSpace(r.Host)) e.Add("host", "Host name or IP address is required.");
        else if (r.Host.Trim().Length > 253 || !HostRegex().IsMatch(r.Host.Trim()))
            e.Add("host", "Use a host name, FQDN or IP address (letters, digits, '.', '-', '_').");

        if (r.Port is < 1 or > 65535) e.Add("port", "Port must be between 1 and 65535.");

        if (!string.IsNullOrWhiteSpace(r.InstanceName) && !InstanceRegex().IsMatch(r.InstanceName.Trim()))
            e.Add("instanceName", "Instance name must be 1-16 characters (letters, digits, _ $ # -).");

        if (r.DatabaseName is { Length: > 128 }) e.Add("databaseName", "Database name must be 128 characters or fewer.");
        if (r.DeclaredPlatform == PlatformType.AzureSqlDatabase && string.IsNullOrWhiteSpace(r.DatabaseName))
            e.Add("databaseName", "Azure SQL Database needs the database name to connect to.");

        if (!Enum.IsDefined(r.DeclaredPlatform)) e.Add("declaredPlatform", "Unknown platform.");
        if (!Enum.IsDefined(r.Environment)) e.Add("environment", "Unknown environment.");

        if (r.ConnectTimeoutSeconds is < 5 or > 120) e.Add("connectTimeoutSeconds", "Connect timeout must be 5-120 seconds.");
        if (r.CollectionIntervalSeconds is < 15 or > 3600) e.Add("collectionIntervalSeconds", "Collection interval must be 15-3600 seconds.");

        if (r.Tags.Count > 20) e.Add("tags", "At most 20 tags.");
        foreach (var tag in r.Tags)
        {
            if (!TagRegex().IsMatch(tag.Trim())) e.Add("tags", $"Tag '{tag}' is invalid (1-50 letters, digits, space, '-', '.', '_').");
        }

        ValidateCredential(e, "monitoring", r.Monitoring, monitoringHasStoredSecret);

        if (r.Admin is not null)
        {
            if (r.RemoveAdmin) e.Add("admin", "Either send an admin login or ask to remove it, not both.");
            else ValidateCredential(e, "admin", r.Admin, adminHasStoredSecret);
        }

        e.ThrowIfAny();
    }

    /// <summary>Auth types that need a password or client secret.</summary>
    public static bool NeedsSecret(AuthType t) =>
        t is AuthType.SqlLogin or AuthType.EntraPassword or AuthType.EntraServicePrincipal;

    /// <summary>Auth types that need a user name (or client id).</summary>
    public static bool NeedsUserName(AuthType t) =>
        t is AuthType.SqlLogin or AuthType.EntraPassword or AuthType.EntraServicePrincipal;

    private static void ValidateCredential(ValidationErrors e, string prefix, CredentialInput? c, bool hasStoredSecret)
    {
        if (c is null)
        {
            e.Add(prefix, "Login details are required.");
            return;
        }

        if (!Enum.IsDefined(c.AuthType))
        {
            e.Add($"{prefix}.authType", "Unknown authentication type.");
            return;
        }

        if (NeedsUserName(c.AuthType) && string.IsNullOrWhiteSpace(c.UserName))
        {
            e.Add($"{prefix}.userName", c.AuthType == AuthType.EntraServicePrincipal
                ? "Application (client) id is required."
                : "User name is required.");
        }

        if (c.UserName is { Length: > 256 }) e.Add($"{prefix}.userName", "User name must be 256 characters or fewer.");

        if (NeedsSecret(c.AuthType) && string.IsNullOrEmpty(c.Secret) && !hasStoredSecret)
        {
            e.Add($"{prefix}.secret", c.AuthType == AuthType.EntraServicePrincipal
                ? "Client secret is required."
                : "Password is required.");
        }

        if (c.Secret is { Length: > 1024 }) e.Add($"{prefix}.secret", "Secret is too long.");

        if (!string.IsNullOrWhiteSpace(c.TenantId) && !Guid.TryParse(c.TenantId, out _))
            e.Add($"{prefix}.tenantId", "Tenant id must be a GUID.");
        if (!string.IsNullOrWhiteSpace(c.ClientId) && !Guid.TryParse(c.ClientId, out _))
            e.Add($"{prefix}.clientId", "Client id must be a GUID.");
    }
}
