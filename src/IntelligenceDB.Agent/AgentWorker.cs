using System.Reflection;
using System.Text.Json;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Agents;
using IntelligenceDB.Application.Monitoring;
using IntelligenceDB.Domain.Agents;
using IntelligenceDB.Targets;
using Microsoft.Extensions.Options;

namespace IntelligenceDB.Agent;

/// <summary>
/// Two loops:
/// 1. Collection: heartbeat (gets the server list) every minute, runs due collectors every 15 s and
///    uploads the results. If the portal is unreachable, results are buffered and sent later.
/// 2. Commands: polls every few seconds for "test this connection" requests from the portal.
/// </summary>
public sealed class AgentWorker(
    PortalClient portal,
    CollectionEngine engine,
    IServerProbe probe,
    IOptions<AgentOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<AgentWorker> logger) : BackgroundService
{
    private const int MaxBufferedBatches = 2000;
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);
    private static readonly string Version =
        typeof(AgentWorker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";

    private readonly List<CollectionBatch> _buffer = [];
    private TimeSpan _heartbeatEvery = TimeSpan.FromSeconds(60);
    private TimeSpan _commandPollEvery = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsValid(out var error))
        {
            logger.LogCritical("IntelligenceDB Agent cannot start: {Error}", error);
            lifetime.StopApplication();
            return;
        }

        logger.LogInformation("IntelligenceDB Agent {Version} started; portal {Portal}", Version, options.Value.PortalUrl);
        await Task.WhenAll(CollectionLoopAsync(stoppingToken), CommandLoopAsync(stoppingToken));
    }

    private async Task CollectionLoopAsync(CancellationToken ct)
    {
        var lastHeartbeat = DateTime.MinValue;
        int heartbeatFailures = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow - lastHeartbeat >= _heartbeatEvery)
                {
                    var config = await portal.HeartbeatAsync(new AgentHeartbeat { Version = Version, MachineName = Environment.MachineName }, ct);
                    engine.UpdateTargets(config.Targets);
                    _heartbeatEvery = TimeSpan.FromSeconds(Math.Clamp(config.HeartbeatSeconds, 15, 600));
                    _commandPollEvery = TimeSpan.FromSeconds(Math.Clamp(config.CommandPollSeconds, 1, 60));
                    lastHeartbeat = DateTime.UtcNow;
                    if (heartbeatFailures > 0) logger.LogInformation("Connected to the portal again");
                    heartbeatFailures = 0;
                    logger.LogDebug("Heartbeat ok: {Count} server(s) assigned", config.Targets.Count);
                }

                var batches = await engine.RunDueAsync(options.Value.MaxParallelServers, ct);
                _buffer.AddRange(batches);
                if (_buffer.Count > MaxBufferedBatches)
                {
                    _buffer.RemoveRange(0, _buffer.Count - MaxBufferedBatches); // drop the oldest
                }

                if (_buffer.Count > 0)
                {
                    await portal.IngestAsync(_buffer, ct);
                    _buffer.Clear();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                heartbeatFailures++;
                if (ex is PortalException { Status: 401 })
                {
                    logger.LogError("The portal rejected this agent's id/key (HTTP 401). Check the agent is enabled and the key is current.");
                }
                else
                {
                    logger.LogWarning("Portal not reachable ({Failures}): {Message}. {Buffered} result(s) kept for later.", heartbeatFailures, ex.Message, _buffer.Count);
                }
            }

            await Delay(Tick, ct);
        }
    }

    private async Task CommandLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var cmd in await portal.GetCommandsAsync(ct))
                {
                    await HandleAsync(cmd, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogDebug("Command poll failed: {Message}", ex.Message);
            }

            await Delay(_commandPollEvery, ct);
        }
    }

    private async Task HandleAsync(AgentCommandDto cmd, CancellationToken ct)
    {
        string resultJson;
        if (cmd.Type == AgentCommandTypes.Probe)
        {
            var payload = JsonSerializer.Deserialize<ProbeCommandPayload>(cmd.PayloadJson, WireJson.Options);
            var result = payload?.Monitoring is null
                ? ProbeResult.Failed(Domain.Servers.ConnectionStatus.Unreachable, "The agent received an empty test request.", 0)
                : await probe.ProbeAsync(payload.Monitoring, payload.Admin, ct);
            resultJson = JsonSerializer.Serialize(result, WireJson.Options);
            logger.LogInformation("Connection test {CommandId} for {Target}: {Status}", cmd.Id, payload?.Monitoring?.ToString() ?? "?", result.Status);
        }
        else
        {
            logger.LogWarning("Unknown command type {Type}; this agent may be older than the portal", cmd.Type);
            resultJson = JsonSerializer.Serialize(ProbeResult.Failed(Domain.Servers.ConnectionStatus.Unknown, $"Agent {Version} does not support '{cmd.Type}'. Update the agent.", 0), WireJson.Options);
        }

        await portal.CompleteCommandAsync(cmd.Id, resultJson, ct);
    }

    private static async Task Delay(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
    }
}
