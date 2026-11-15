using System.Net.Http.Json;
using IntelligenceDB.Application.Agents;
using IntelligenceDB.Application.Monitoring;

namespace IntelligenceDB.Agent;

/// <summary>HTTPS client for the portal's agent endpoints (/api/agent/v1/...). Every call is outbound.</summary>
public sealed class PortalClient(HttpClient http)
{
    public const string AgentIdHeader = "X-Agent-Id";
    public const string AgentKeyHeader = "X-Agent-Key";

    public async Task<AgentConfig> HeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync("api/agent/v1/heartbeat", heartbeat, WireJson.Options, ct);
        await EnsureOkAsync(res, ct);
        return await res.Content.ReadFromJsonAsync<AgentConfig>(WireJson.Options, ct) ?? new AgentConfig();
    }

    public async Task<IReadOnlyList<AgentCommandDto>> GetCommandsAsync(CancellationToken ct)
    {
        using var res = await http.GetAsync("api/agent/v1/commands", ct);
        await EnsureOkAsync(res, ct);
        return await res.Content.ReadFromJsonAsync<List<AgentCommandDto>>(WireJson.Options, ct) ?? [];
    }

    public async Task CompleteCommandAsync(long id, string resultJson, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync($"api/agent/v1/commands/{id}/result", new { resultJson }, WireJson.Options, ct);
        await EnsureOkAsync(res, ct);
    }

    public async Task IngestAsync(IReadOnlyList<CollectionBatch> batches, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync("api/agent/v1/ingest", batches, WireJson.Options, ct);
        await EnsureOkAsync(res, ct);
    }

    private static async Task EnsureOkAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        var body = await res.Content.ReadAsStringAsync(ct);
        throw new PortalException((int)res.StatusCode, body.Length > 500 ? body[..500] : body);
    }
}

public sealed class PortalException(int status, string body)
    : Exception($"Portal returned HTTP {status}: {body}")
{
    public int Status { get; } = status;
}
