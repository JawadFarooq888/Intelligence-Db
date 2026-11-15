using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Common;
using IntelligenceDB.Application.Monitoring;
using IntelligenceDB.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IntelligenceDB.Application.Agents;

public sealed record AgentDto(
    Guid Id, string Name, bool IsEnabled, bool IsOnline, DateTime CreatedUtc, DateTime? LastSeenUtc,
    string? Version, string? MachineName, string? LastIp, string KeyPrefix, int ServerCount);

/// <summary>Returned once when an agent is created or its key is regenerated. The key is never shown again.</summary>
public sealed record AgentCreatedDto(AgentDto Agent, string Key);

public sealed class AgentRequest
{
    public string Name { get; init; } = string.Empty;
    public bool IsEnabled { get; init; } = true;
}

/// <summary>Sent by the agent with each heartbeat.</summary>
public sealed class AgentHeartbeat
{
    public string? Version { get; set; }
    public string? MachineName { get; set; }
}

/// <summary>What the agent must collect, returned on every heartbeat.</summary>
public sealed class AgentConfig
{
    public List<CollectionTarget> Targets { get; set; } = [];
    public int HeartbeatSeconds { get; set; } = 60;
    public int CommandPollSeconds { get; set; } = 3;
}

public sealed class AgentCommandDto
{
    public long Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
}

/// <summary>Agent registration, authentication, configuration and the command channel.</summary>
public sealed class AgentService(
    IRepoDbContext db,
    ISecretProtector secrets,
    CollectionTargetFactory targets,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<AgentService> logger)
{
    private const string KeyPrefixMarker = "idbk_";

    /// <summary>An agent is shown as online when it sent a heartbeat within this time.</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(3);

    // ---------------- Admin management ----------------

    public async Task<IReadOnlyList<AgentDto>> ListAsync(CancellationToken ct)
    {
        var agents = await db.Agents.AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);
        var counts = await db.Servers.Where(s => s.AgentId != null)
            .GroupBy(s => s.AgentId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return agents.Select(a => ToDto(a, counts.GetValueOrDefault(a.Id), now)).ToList();
    }

    public async Task<AgentCreatedDto> CreateAsync(AgentRequest request, CancellationToken ct)
    {
        var name = ValidateName(request);
        if (await db.Agents.AnyAsync(a => a.Name == name, ct)) throw new ConflictException($"An agent named '{name}' already exists.");

        var (key, hash) = NewKey();
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsEnabled = request.IsEnabled,
            KeyHash = hash,
            KeyPrefix = key[..12],
            CreatedUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditRecord("Agent.Create", Details: new { agent.Id, agent.Name }), ct);
        return new AgentCreatedDto(ToDto(agent, 0, agent.CreatedUtc), key);
    }

    public async Task<AgentCreatedDto> RegenerateKeyAsync(Guid id, CancellationToken ct)
    {
        var agent = await FindAsync(id, ct);
        var (key, hash) = NewKey();
        agent.KeyHash = hash;
        agent.KeyPrefix = key[..12];
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditRecord("Agent.RegenerateKey", Details: new { agent.Id, agent.Name }), ct);
        var count = await db.Servers.CountAsync(s => s.AgentId == id, ct);
        return new AgentCreatedDto(ToDto(agent, count, clock.GetUtcNow().UtcDateTime), key);
    }

    public async Task<AgentDto> UpdateAsync(Guid id, AgentRequest request, CancellationToken ct)
    {
        var name = ValidateName(request);
        var agent = await FindAsync(id, ct);
        if (await db.Agents.AnyAsync(a => a.Name == name && a.Id != id, ct)) throw new ConflictException($"An agent named '{name}' already exists.");
        agent.Name = name;
        agent.IsEnabled = request.IsEnabled;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditRecord("Agent.Update", Details: new { agent.Id, agent.Name, agent.IsEnabled }), ct);
        var count = await db.Servers.CountAsync(s => s.AgentId == id, ct);
        return ToDto(agent, count, clock.GetUtcNow().UtcDateTime);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var agent = await FindAsync(id, ct);
        var count = await db.Servers.CountAsync(s => s.AgentId == id, ct);
        if (count > 0)
        {
            throw new ConflictException($"{count} server(s) still use this agent. Move them to another agent or to direct connection first.");
        }

        db.Agents.Remove(agent);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditRecord("Agent.Delete", Details: new { agent.Id, agent.Name }), ct);
    }

    // ---------------- Agent channel ----------------

    /// <summary>Checks an agent's id and key. Returns null when they are wrong or the agent is disabled.</summary>
    public async Task<Agent?> AuthenticateAsync(string? agentId, string? key, CancellationToken ct)
    {
        if (!Guid.TryParse(agentId, out var id) || string.IsNullOrEmpty(key) || key.Length > 200) return null;
        var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent is null || !agent.IsEnabled) return null;

        var expected = Convert.FromHexString(agent.KeyHash);
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return CryptographicOperations.FixedTimeEquals(expected, actual) ? agent : null;
    }

    public async Task<AgentConfig> HeartbeatAsync(Agent agent, AgentHeartbeat heartbeat, string? ip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(heartbeat);
        agent.LastSeenUtc = clock.GetUtcNow().UtcDateTime;
        agent.Version = Truncate(heartbeat.Version, 50);
        agent.MachineName = Truncate(heartbeat.MachineName, 128);
        agent.LastIp = Truncate(ip, 64);
        await db.SaveChangesAsync(ct);

        var servers = await db.Servers
            .AsNoTracking()
            .Include(s => s.MonitoringCredential)
            .Include(s => s.AdminCredential)
            .Include(s => s.Capabilities)
            .Where(s => s.AgentId == agent.Id && s.IsEnabled)
            .AsSplitQuery()
            .ToListAsync(ct);

        var config = new AgentConfig();
        foreach (var s in servers)
        {
            try
            {
                config.Targets.Add(targets.Create(s));
            }
            catch (CryptographicException ex)
            {
                logger.LogError(ex, "Credentials of server {ServerId} cannot be decrypted; it is skipped for agent {Agent}", s.Id, agent.Name);
            }
        }

        return config;
    }

    /// <summary>Pending commands for the agent. Payloads are decrypted only for this response.</summary>
    public async Task<IReadOnlyList<AgentCommandDto>> TakeCommandsAsync(Agent agent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var now = clock.GetUtcNow().UtcDateTime;
        agent.LastSeenUtc = now;

        var commands = await db.AgentCommands
            .Where(c => c.AgentId == agent.Id && c.Status == AgentCommandStatus.Pending && c.ExpiresUtc > now)
            .OrderBy(c => c.Id)
            .Take(10)
            .ToListAsync(ct);

        var result = new List<AgentCommandDto>();
        foreach (var c in commands)
        {
            c.Status = AgentCommandStatus.Delivered;
            c.DeliveredUtc = now;
            result.Add(new AgentCommandDto
            {
                Id = c.Id,
                Type = c.Type,
                PayloadJson = c.ProtectedPayload is null ? "{}" : secrets.Unprotect(c.ProtectedPayload),
            });
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task CompleteCommandAsync(Agent agent, long commandId, string resultJson, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var c = await db.AgentCommands.FirstOrDefaultAsync(x => x.Id == commandId && x.AgentId == agent.Id, ct)
                ?? throw new NotFoundException($"Command {commandId} was not found.");
        c.Status = AgentCommandStatus.Completed;
        c.CompletedUtc = clock.GetUtcNow().UtcDateTime;
        c.ResultJson = resultJson.Length > 200_000 ? resultJson[..200_000] : resultJson;
        c.ProtectedPayload = null; // never keep secrets longer than needed
        await db.SaveChangesAsync(ct);
    }

    // ---------------- helpers ----------------

    /// <summary>New random key: "idbk_" + 43 base64url characters (256 bits). Only its SHA-256 is stored.</summary>
    internal static (string Key, string Hash) NewKey()
    {
        var key = KeyPrefixMarker + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (key, HashKey(key));
    }

    internal static string HashKey(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private async Task<Agent> FindAsync(Guid id, CancellationToken ct) =>
        await db.Agents.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException($"Agent {id} was not found.");

    private static AgentDto ToDto(Agent a, int serverCount, DateTime now) => new(
        a.Id, a.Name, a.IsEnabled, a.LastSeenUtc is { } seen && now - seen <= OnlineWindow,
        a.CreatedUtc, a.LastSeenUtc, a.Version, a.MachineName, a.LastIp, a.KeyPrefix, serverCount);

    private static string ValidateName(AgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = request.Name?.Trim() ?? string.Empty;
        var e = new ValidationErrors();
        if (name.Length == 0) e.Add("name", "Agent name is required.");
        if (name.Length > 100) e.Add("name", "Agent name must be 100 characters or fewer.");
        e.ThrowIfAny();
        return name;
    }

    private static string? Truncate(string? v, int max) => v is null || v.Length <= max ? v : v[..max];

    /// <summary>Parses a probe result sent back by an agent.</summary>
    public static ProbeResult? ParseProbeResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ProbeResult>(json, WireJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
