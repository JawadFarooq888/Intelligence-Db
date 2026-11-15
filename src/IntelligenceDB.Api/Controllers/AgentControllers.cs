using IntelligenceDB.Application.Agents;
using IntelligenceDB.Application.Common;
using IntelligenceDB.Application.Monitoring;
using IntelligenceDB.Domain.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IntelligenceDB.Api.Controllers;

/// <summary>Agent management in the portal. Listing is allowed for server managers (to pick an agent); changes are Admin-only.</summary>
[ApiController]
[Route("api/agents")]
public sealed class AgentsController(AgentService agents) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.ManageServers)]
    public Task<IReadOnlyList<AgentDto>> List(CancellationToken ct) => agents.ListAsync(ct);

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<AgentCreatedDto> Create(AgentRequest request, CancellationToken ct) => agents.CreateAsync(request, ct);

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<AgentDto> Update(Guid id, AgentRequest request, CancellationToken ct) => agents.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/regenerate-key")]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<AgentCreatedDto> RegenerateKey(Guid id, CancellationToken ct) => agents.RegenerateKeyAsync(id, ct);

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await agents.DeleteAsync(id, ct);
        return NoContent();
    }
}

public sealed class CommandResultRequest
{
    public string ResultJson { get; init; } = "{}";
}

/// <summary>
/// Endpoints called BY agents (outbound from the customer network). Authenticated with the agent id and key
/// headers instead of a user token. Responses may contain connection secrets, so production must use HTTPS.
/// </summary>
[ApiController]
[Route("api/agent/v1")]
[AllowAnonymous]
public sealed class AgentApiController(AgentService agents, IngestionService ingestion, ILogger<AgentApiController> logger) : ControllerBase
{
    private const string AgentIdHeader = "X-Agent-Id";
    private const string AgentKeyHeader = "X-Agent-Key";

    [HttpPost("heartbeat")]
    public async Task<ActionResult<AgentConfig>> Heartbeat(AgentHeartbeat heartbeat, CancellationToken ct)
    {
        var agent = await AuthenticateAsync(ct);
        if (agent is null) return Unauthorized();
        return await agents.HeartbeatAsync(agent, heartbeat, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
    }

    [HttpGet("commands")]
    public async Task<ActionResult<IReadOnlyList<AgentCommandDto>>> Commands(CancellationToken ct)
    {
        var agent = await AuthenticateAsync(ct);
        if (agent is null) return Unauthorized();
        return Ok(await agents.TakeCommandsAsync(agent, ct));
    }

    [HttpPost("commands/{id:long}/result")]
    public async Task<IActionResult> CommandResult(long id, CommandResultRequest request, CancellationToken ct)
    {
        var agent = await AuthenticateAsync(ct);
        if (agent is null) return Unauthorized();
        await agents.CompleteCommandAsync(agent, id, request.ResultJson, ct);
        return NoContent();
    }

    [HttpPost("ingest")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Ingest(List<CollectionBatch> batches, CancellationToken ct)
    {
        var agent = await AuthenticateAsync(ct);
        if (agent is null) return Unauthorized();
        int accepted = await ingestion.IngestAsync(batches, $"agent:{agent.Name}", agent.Id, ct);
        return Ok(new { accepted });
    }

    private async Task<Agent?> AuthenticateAsync(CancellationToken ct)
    {
        var agent = await agents.AuthenticateAsync(Request.Headers[AgentIdHeader].ToString(), Request.Headers[AgentKeyHeader].ToString(), ct);
        if (agent is null)
        {
            logger.LogWarning("Rejected agent call from {Ip} (id {AgentId})", HttpContext.Connection.RemoteIpAddress, Request.Headers[AgentIdHeader].ToString());
        }

        return agent;
    }
}

/// <summary>Server Overview, Performance and Databases pages.</summary>
[ApiController]
[Route("api")]
public sealed class MonitoringController(MonitoringQueryService monitoring) : ControllerBase
{
    [HttpGet("servers/{id:int}/overview")]
    public Task<ServerOverviewDto> Overview(int id, CancellationToken ct) => monitoring.GetOverviewAsync(id, ct);

    [HttpGet("servers/{id:int}/performance")]
    public Task<PerformanceDto> Performance(int id, [FromQuery] int hours = 24, CancellationToken ct = default) =>
        monitoring.GetPerformanceAsync(id, hours, ct);

    [HttpGet("servers/{id:int}/databases")]
    public Task<IReadOnlyList<DatabaseRowDto>> ServerDatabases(int id, CancellationToken ct) =>
        monitoring.ListDatabasesAsync(id, includeDropped: false, ct);

    [HttpGet("databases")]
    public Task<IReadOnlyList<DatabaseRowDto>> Databases([FromQuery] bool includeDropped = false, CancellationToken ct = default) =>
        monitoring.ListDatabasesAsync(null, includeDropped, ct);

    [HttpGet("databases/{id:int}")]
    public Task<DatabaseDetailDto> Database(int id, CancellationToken ct) => monitoring.GetDatabaseAsync(id, ct);
}
