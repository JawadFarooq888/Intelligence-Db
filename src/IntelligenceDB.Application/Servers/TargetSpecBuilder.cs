using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.Application.Servers;

/// <summary>Builds <see cref="TargetConnectionSpec"/>s, decrypting stored secrets only at the moment of use.</summary>
public sealed class TargetSpecBuilder(ISecretProtector secrets)
{
    public TargetConnectionSpec FromServer(Server server, Credential credential)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(credential);

        return new TargetConnectionSpec
        {
            Host = server.Host,
            Port = server.Port,
            InstanceName = server.InstanceName,
            DatabaseName = server.DatabaseName,
            AuthType = credential.AuthType,
            UserName = credential.UserName,
            Secret = credential.ProtectedSecret is { Length: > 0 } p ? secrets.Unprotect(p) : null,
            TenantId = credential.TenantId,
            ClientId = credential.ClientId,
            Encrypt = server.Encrypt,
            TrustServerCertificate = server.TrustServerCertificate,
            ConnectTimeoutSeconds = server.ConnectTimeoutSeconds,
            Platform = server.EffectivePlatform,
        };
    }

    /// <summary>
    /// Builds a spec from unsaved form input. If the form left the secret empty, the stored secret
    /// of the existing credential (when editing) is used.
    /// </summary>
    public TargetConnectionSpec FromRequest(ServerUpsertRequest request, CredentialInput input, Credential? existing)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(input);

        string? secret = !string.IsNullOrEmpty(input.Secret)
            ? input.Secret
            : existing?.ProtectedSecret is { Length: > 0 } p ? secrets.Unprotect(p) : null;

        return new TargetConnectionSpec
        {
            Host = request.Host.Trim(),
            Port = request.Port,
            InstanceName = NullIfBlank(request.InstanceName),
            DatabaseName = NullIfBlank(request.DatabaseName),
            AuthType = input.AuthType,
            UserName = NullIfBlank(input.UserName),
            Secret = secret,
            TenantId = NullIfBlank(input.TenantId),
            ClientId = NullIfBlank(input.ClientId),
            Encrypt = request.Encrypt,
            TrustServerCertificate = request.TrustServerCertificate,
            ConnectTimeoutSeconds = request.ConnectTimeoutSeconds,
            Platform = request.DeclaredPlatform,
        };
    }

    internal static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
