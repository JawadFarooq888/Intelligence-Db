using System.Text.Json;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Agents;
using IntelligenceDB.Application.Monitoring;
using IntelligenceDB.Domain.Agents;
using IntelligenceDB.Domain.Servers;
using IntelligenceDB.Infrastructure.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IntelligenceDB.Infrastructure.Agents;

/// <summary>
/// Sends a "Probe" command to an agent and waits for the answer. Uses its own DbContext per call,
/// so several probes can run in parallel (e.g. "Check all now").
/// </summary>
public sealed class AgentProbeDispatcher(
    IDbContextFactory<RepoDbContext> contextFactory,
    ISecretProtector secrets,
    TimeProvider clock,
    ILogger<AgentProbeDispatcher> logger) : IAgentProbeDispatcher
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async Task<ProbeResult> ProbeAsync(Guid agentId, TargetConnectionSpec monitoring, TargetConnectionSpec? admin, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        var agent = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null || !agent.IsEnabled)
        {
            return ProbeResult.Failed(ConnectionStatus.Unreachable, "The agent for this server does not exist or is disabled.", 0);
        }

        if (agent.LastSeenUtc is null || now - agent.LastSeenUtc.Value > AgentService.OnlineWindow)
        {
            return ProbeResult.Failed(ConnectionStatus.Unreachable,
                $"Agent '{agent.Name}' is offline (last seen {(agent.LastSeenUtc is null ? "never" : agent.LastSeenUtc.Value.ToString("u", System.Globalization.CultureInfo.InvariantCulture))}). " +
                "Check that the IntelligenceDB Agent service is running and can reach the portal.", 0);
        }

        var payload = JsonSerializer.Serialize(new ProbeCommandPayload { Monitoring = monitoring, Admin = admin }, WireJson.Options);
        var command = new AgentCommand
        {
            AgentId = agentId,
            Type = AgentCommandTypes.Probe,
            ProtectedPayload = secrets.Protect(payload),
            Status = AgentCommandStatus.Pending,
            CreatedUtc = now,
            ExpiresUtc = now + Timeout,
        };
        db.AgentCommands.Add(command);
        await db.SaveChangesAsync(cancellationToken);

        var started = clock.GetUtcNow();
        while (clock.GetUtcNow() - started < Timeout)
        {
            await Task.Delay(PollInterval, cancellationToken);
            var row = await db.AgentCommands.AsNoTracking()
                .Where(c => c.Id == command.Id)
                .Select(c => new { c.Status, c.ResultJson })
                .FirstAsync(cancellationToken);

            if (row.Status == AgentCommandStatus.Completed)
            {
                var result = AgentService.ParseProbeResult(row.ResultJson);
                return result ?? ProbeResult.Failed(ConnectionStatus.Unreachable, "The agent sent an unreadable answer.", 0);
            }
        }

        // Timed out: make sure the agent does not run it later, and drop the secrets.
        await db.AgentCommands.Where(c => c.Id == command.Id && c.Status != AgentCommandStatus.Completed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, AgentCommandStatus.Expired)
                .SetProperty(c => c.ProtectedPayload, (byte[]?)null), cancellationToken);
        logger.LogWarning("Agent {Agent} did not answer probe command {CommandId} in time", agent.Name, command.Id);
        return ProbeResult.Failed(ConnectionStatus.Unreachable,
            $"Agent '{agent.Name}' did not answer within {Timeout.TotalSeconds:0} seconds.", (int)Timeout.TotalMilliseconds);
    }
}
