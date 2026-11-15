using System.Diagnostics;
using System.Text.Json;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Application.Common;
using IntelligenceDB.Domain.Audit;
using IntelligenceDB.Domain.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IntelligenceDB.Application.Servers;

/// <summary>Adding, editing, testing and removing monitored servers.</summary>
public sealed class ServerService(
    IRepoDbContext db,
    ISecretProtector secrets,
    TargetSpecBuilder specs,
    IServerProbe probe,
    IAgentProbeDispatcher agentProbe,
    ICurrentUser user,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<ServerService> logger)
{
    /// <summary>How many servers are probed at the same time by "refresh all".</summary>
    private const int RefreshParallelism = 8;

    public async Task<IReadOnlyList<ServerSummaryDto>> ListAsync(CancellationToken ct)
    {
        var scope = await user.GetScopeAsync(ct);
        var servers = await db.Servers.AsNoTracking()
            .VisibleTo(scope)
            .Include(s => s.Group)
            .Include(s => s.Tags)
            .Include(s => s.Info)
            .Include(s => s.Agent)
            .Include(s => s.Health)
            .OrderBy(s => s.DisplayName)
            .AsSplitQuery()
            .ToListAsync(ct);

        return servers.Select(ServerMapper.ToSummary).ToList();
    }

    public async Task<ServerDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var scope = await user.GetScopeAsync(ct);
        var server = await LoadAsync(id, tracked: false, scope, ct);
        return ServerMapper.ToDetail(server);
    }

    public async Task<ServerDetailDto> CreateAsync(ServerUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ServerValidator.Validate(request, monitoringHasStoredSecret: false, adminHasStoredSecret: false);

        var scope = await user.GetScopeAsync(ct);
        await EnsureGroupAllowedAsync(request.GroupId, scope, ct);
        await EnsureAgentUsableAsync(request.AgentId, ct);
        await EnsureNameUniqueAsync(request.DisplayName.Trim(), exceptId: null, ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var server = new Server { CreatedUtc = now };
        ApplyFields(server, request);
        server.MonitoringCredential = NewCredential(CredentialPurpose.Monitoring, request.Monitoring, now);
        if (request.Admin is not null)
        {
            server.AdminCredential = NewCredential(CredentialPurpose.Admin, request.Admin, now);
        }

        foreach (var tag in NormalizeTags(request.Tags))
        {
            server.Tags.Add(new ServerTag { Tag = tag });
        }

        db.Servers.Add(server);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditRecord("Server.Create", server.Id, server.DisplayName, Details: Describe(server)), ct);

        // Test right away so the user immediately sees version, platform and capabilities.
        await ProbeAndApplyAsync(server, ct);

        return await GetAsync(server.Id, ct);
    }

    public async Task<ServerDetailDto> UpdateAsync(int id, ServerUpsertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await user.GetScopeAsync(ct);
        var server = await LoadAsync(id, tracked: true, scope, ct);

        // When switching to an auth type that needs a secret, a stored secret only counts if one exists.
        ServerValidator.Validate(
            request,
            monitoringHasStoredSecret: server.MonitoringCredential.ProtectedSecret is { Length: > 0 },
            adminHasStoredSecret: server.AdminCredential?.ProtectedSecret is { Length: > 0 });

        if (!string.IsNullOrEmpty(request.RowVersion))
        {
            db.Entry(server).Property(s => s.RowVersion).OriginalValue = DecodeRowVersion(request.RowVersion);
        }

        await EnsureGroupAllowedAsync(request.GroupId, scope, ct);
        await EnsureAgentUsableAsync(request.AgentId, ct);
        await EnsureNameUniqueAsync(request.DisplayName.Trim(), exceptId: id, ct);

        var now = clock.GetUtcNow().UtcDateTime;
        ApplyFields(server, request);
        UpdateCredential(server.MonitoringCredential, request.Monitoring, now);

        if (request.RemoveAdmin && server.AdminCredential is not null)
        {
            var old = server.AdminCredential;
            server.AdminCredential = null;
            server.AdminCredentialId = null;
            db.Credentials.Remove(old);
        }
        else if (request.Admin is not null)
        {
            if (server.AdminCredential is null)
            {
                server.AdminCredential = NewCredential(CredentialPurpose.Admin, request.Admin, now);
            }
            else
            {
                UpdateCredential(server.AdminCredential, request.Admin, now);
            }
        }

        var wanted = NormalizeTags(request.Tags).ToHashSet(StringComparer.OrdinalIgnoreCase);
        server.Tags.RemoveAll(t => !wanted.Contains(t.Tag));
        foreach (var tag in wanted.Where(w => !server.Tags.Exists(t => string.Equals(t.Tag, w, StringComparison.OrdinalIgnoreCase))))
        {
            server.Tags.Add(new ServerTag { ServerId = server.Id, Tag = tag });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This server was changed by someone else. Reload the page and try again.");
        }

        await audit.WriteAsync(new AuditRecord("Server.Update", server.Id, server.DisplayName, Details: Describe(server)), ct);
        await ProbeAndApplyAsync(server, ct);
        return await GetAsync(server.Id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var scope = await user.GetScopeAsync(ct);
        var server = await LoadAsync(id, tracked: true, scope, ct);

        var monitoring = server.MonitoringCredential;
        var admin = server.AdminCredential;
        db.Servers.Remove(server);
        db.Credentials.Remove(monitoring);
        if (admin is not null) db.Credentials.Remove(admin);

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditRecord("Server.Delete", id, server.DisplayName), ct);
    }

    /// <summary>Tests a saved server and stores the detected version, platform and capabilities.</summary>
    public async Task<TestConnectionResultDto> TestAsync(int id, CancellationToken ct)
    {
        var scope = await user.GetScopeAsync(ct);
        var server = await LoadAsync(id, tracked: true, scope, ct);
        var dto = await ProbeAndApplyAsync(server, ct);

        await audit.WriteAsync(new AuditRecord(
            "Server.Test", server.Id, server.DisplayName,
            Result: dto.Success ? AuditResults.Succeeded : AuditResults.Failed,
            DurationMs: dto.ElapsedMs,
            Error: dto.Error), ct);
        return dto;
    }

    /// <summary>Tests connection details from the add/edit form without saving anything.</summary>
    public async Task<TestConnectionResultDto> TestUnsavedAsync(ServerUpsertRequest request, int? existingId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Server? existing = null;
        if (existingId is not null)
        {
            var scope = await user.GetScopeAsync(ct);
            existing = await LoadAsync(existingId.Value, tracked: false, scope, ct);
        }

        ServerValidator.Validate(
            request,
            monitoringHasStoredSecret: existing?.MonitoringCredential.ProtectedSecret is { Length: > 0 },
            adminHasStoredSecret: existing?.AdminCredential?.ProtectedSecret is { Length: > 0 });

        var monitoringSpec = specs.FromRequest(request, request.Monitoring, existing?.MonitoringCredential);
        TargetConnectionSpec? adminSpec = null;
        bool adminConfigured = false;
        if (!request.RemoveAdmin)
        {
            if (request.Admin is not null)
            {
                adminSpec = specs.FromRequest(request, request.Admin, existing?.AdminCredential);
                adminConfigured = true;
            }
            else if (existing?.AdminCredential is { } storedAdmin)
            {
                // Keep the stored admin login, but test it against the host typed in the form.
                var storedInput = new CredentialInput
                {
                    AuthType = storedAdmin.AuthType,
                    UserName = storedAdmin.UserName,
                    TenantId = storedAdmin.TenantId,
                    ClientId = storedAdmin.ClientId,
                };
                adminSpec = specs.FromRequest(request, storedInput, storedAdmin);
                adminConfigured = true;
            }
        }

        if (request.AgentId is { } agentForTest) await EnsureAgentUsableAsync(agentForTest, ct);
        var result = request.AgentId is { } viaAgent
            ? await agentProbe.ProbeAsync(viaAgent, monitoringSpec, adminSpec, ct)
            : await probe.ProbeAsync(monitoringSpec, adminSpec, ct);
        var (platform, caps) = ProbeResultApplier.Evaluate(result, request.DeclaredPlatform, adminConfigured);

        await audit.WriteAsync(new AuditRecord(
            "Server.TestUnsaved", existingId, request.DisplayName,
            Result: result.Success ? AuditResults.Succeeded : AuditResults.Failed,
            DurationMs: result.ElapsedMs,
            Error: result.Error,
            Details: new { host = request.Host, request.Port, request.InstanceName, authType = request.Monitoring.AuthType.ToString() }), ct);

        return ServerMapper.ToTestDto(result, request.DeclaredPlatform, platform, caps);
    }

    /// <summary>
    /// Probes every enabled server the user can see, several at a time.
    /// One slow or unreachable server never blocks the others (each probe has its own timeout).
    /// </summary>
    public async Task<int> RefreshAllAsync(CancellationToken ct)
    {
        var scope = await user.GetScopeAsync(ct);
        var servers = await db.Servers
            .VisibleTo(scope)
            .Where(s => s.IsEnabled)
            .Include(s => s.MonitoringCredential)
            .Include(s => s.AdminCredential)
            .Include(s => s.Info)
            .Include(s => s.Capabilities)
            .AsSplitQuery()
            .ToListAsync(ct);

        using var throttle = new SemaphoreSlim(RefreshParallelism);

        // Probing touches only the target servers, so it can run in parallel.
        // Applying results touches the DbContext, which is not thread-safe, so that part runs afterwards, in order.
        var probes = servers.Select(async s =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                return (Server: s, Result: await RunProbeAsync(s, ct));
            }
            finally
            {
                throttle.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(probes);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var (server, result) in results)
        {
            ProbeResultApplier.Apply(server, result, now);
        }

        await db.SaveChangesAsync(ct);
        return results.Length;
    }

    // ------------------------------------------------------------------

    private async Task<TestConnectionResultDto> ProbeAndApplyAsync(Server server, CancellationToken ct)
    {
        var result = await RunProbeAsync(server, ct);
        var (platform, caps) = ProbeResultApplier.Apply(server, result, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return ServerMapper.ToTestDto(result, server.DeclaredPlatform, platform, caps);
    }

    /// <summary>Decrypts the credentials and probes. Never throws except on cancellation.</summary>
    private async Task<ProbeResult> RunProbeAsync(Server server, CancellationToken ct)
    {
        TargetConnectionSpec monitoringSpec;
        TargetConnectionSpec? adminSpec;
        try
        {
            monitoringSpec = specs.FromServer(server, server.MonitoringCredential);
            adminSpec = server.AdminCredential is null ? null : specs.FromServer(server, server.AdminCredential);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            logger.LogError(ex, "Stored secret for server {ServerId} could not be decrypted", server.Id);
            return ProbeResult.Failed(ConnectionStatus.AuthFailed,
                "The stored password could not be decrypted (the data protection keys may have changed). Edit the server and enter the password again.", 0);
        }

        // Servers inside a customer network are tested by their agent; the rest directly from here.
        return server.AgentId is { } agentId
            ? await agentProbe.ProbeAsync(agentId, monitoringSpec, adminSpec, ct)
            : await probe.ProbeAsync(monitoringSpec, adminSpec, ct);
    }

    private async Task<Server> LoadAsync(int id, bool tracked, AccessScope scope, CancellationToken ct)
    {
        var query = db.Servers
            .Include(s => s.Group)
            .Include(s => s.Tags)
            .Include(s => s.Info)
            .Include(s => s.Capabilities)
            .Include(s => s.MonitoringCredential)
            .Include(s => s.AdminCredential)
            .Include(s => s.Agent)
            .Include(s => s.Health)
            .AsSplitQuery();

        var server = await (tracked ? query : query.AsNoTracking()).FirstOrDefaultAsync(s => s.Id == id, ct);

        // Servers outside the user's groups are reported as "not found" so their existence is not leaked.
        if (server is null || !scope.CanSee(server.GroupId))
        {
            throw new NotFoundException($"Server {id} was not found.");
        }

        return server;
    }

    private async Task EnsureGroupAllowedAsync(int? groupId, AccessScope scope, CancellationToken ct)
    {
        if (groupId is null)
        {
            if (!scope.AllGroups)
            {
                var e = new ValidationErrors();
                e.Add("groupId", "Choose one of your server groups.");
                e.ThrowIfAny();
            }

            return;
        }

        bool exists = await db.ServerGroups.AnyAsync(g => g.Id == groupId, ct);
        if (!exists)
        {
            var e = new ValidationErrors();
            e.Add("groupId", "The selected group does not exist.");
            e.ThrowIfAny();
        }

        if (!scope.CanSee(groupId))
        {
            throw new ForbiddenException("You do not have access to that server group.");
        }
    }

    private async Task EnsureAgentUsableAsync(Guid? agentId, CancellationToken ct)
    {
        if (agentId is null) return;
        bool ok = await db.Agents.AnyAsync(a => a.Id == agentId && a.IsEnabled, ct);
        if (!ok)
        {
            var e = new ValidationErrors();
            e.Add("agentId", "The selected agent does not exist or is disabled.");
            e.ThrowIfAny();
        }
    }

    private async Task EnsureNameUniqueAsync(string name, int? exceptId, CancellationToken ct)
    {
        bool taken = await db.Servers.AnyAsync(s => s.DisplayName == name && (exceptId == null || s.Id != exceptId), ct);
        if (taken)
        {
            throw new ConflictException($"A server named '{name}' already exists.");
        }
    }

    private static void ApplyFields(Server s, ServerUpsertRequest r)
    {
        s.DisplayName = r.DisplayName.Trim();
        s.Host = r.Host.Trim();
        s.Port = r.Port;
        s.InstanceName = TargetSpecBuilder.NullIfBlank(r.InstanceName);
        s.DatabaseName = TargetSpecBuilder.NullIfBlank(r.DatabaseName);
        s.DeclaredPlatform = r.DeclaredPlatform;
        s.Environment = r.Environment;
        s.GroupId = r.GroupId;
        s.AgentId = r.AgentId;
        s.IsEnabled = r.IsEnabled;
        s.Encrypt = r.Encrypt;
        s.TrustServerCertificate = r.TrustServerCertificate;
        s.ConnectTimeoutSeconds = r.ConnectTimeoutSeconds;
        s.CollectionIntervalSeconds = r.CollectionIntervalSeconds;
    }

    private Credential NewCredential(CredentialPurpose purpose, CredentialInput input, DateTime now)
    {
        var c = new Credential { Purpose = purpose, CreatedUtc = now };
        UpdateCredential(c, input, now);
        c.RotatedUtc = null;
        return c;
    }

    private void UpdateCredential(Credential c, CredentialInput input, DateTime now)
    {
        c.AuthType = input.AuthType;
        c.UserName = TargetSpecBuilder.NullIfBlank(input.UserName);
        c.TenantId = TargetSpecBuilder.NullIfBlank(input.TenantId);
        c.ClientId = TargetSpecBuilder.NullIfBlank(input.ClientId);

        if (!ServerValidator.NeedsSecret(input.AuthType))
        {
            c.ProtectedSecret = null;
        }
        else if (!string.IsNullOrEmpty(input.Secret))
        {
            c.ProtectedSecret = secrets.Protect(input.Secret);
            c.RotatedUtc = now;
        }

        // Otherwise: keep the stored secret (validator already ensured one exists).
    }

    private static IEnumerable<string> NormalizeTags(IEnumerable<string> tags) =>
        tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    private static byte[] DecodeRowVersion(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            var e = new ValidationErrors();
            e.Add("rowVersion", "Invalid row version.");
            e.ThrowIfAny();
            throw; // unreachable: ThrowIfAny always throws here
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    /// <summary>Audit details for a server. Contains no secrets.</summary>
    private static object Describe(Server s) => new
    {
        s.Host,
        s.Port,
        s.InstanceName,
        s.DatabaseName,
        declaredPlatform = s.DeclaredPlatform.ToString(),
        environment = s.Environment.ToString(),
        s.GroupId,
        monitoringAuth = s.MonitoringCredential.AuthType.ToString(),
        monitoringUser = s.MonitoringCredential.UserName,
        adminAuth = s.AdminCredential?.AuthType.ToString(),
        adminUser = s.AdminCredential?.UserName,
        tags = s.Tags.Select(t => t.Tag).ToArray(),
    };
}

internal static class ServerQueryExtensions
{
    /// <summary>Restricts a server query to the groups the user may see.</summary>
    public static IQueryable<Server> VisibleTo(this IQueryable<Server> query, AccessScope scope)
    {
        if (scope.AllGroups) return query;
        var allowed = scope.GroupIds.ToList();
        return query.Where(s => s.GroupId != null && allowed.Contains(s.GroupId.Value));
    }
}
