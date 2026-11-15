using System.Collections.Concurrent;
using System.Diagnostics;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Monitoring;
using IntelligenceDB.Domain.Servers;
using IntelligenceDB.Targets.Collectors;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace IntelligenceDB.Targets;

/// <summary>
/// Decides which collectors are due for which servers and runs them.
/// - Several servers run in parallel (limit configurable), each server uses ONE connection for all its due collectors.
/// - A server that cannot be reached is retried with growing back-off (30 s ... 5 min), so it never slows the others.
/// - A failing collector only marks its own batch as failed; the others still run.
/// Used by the central Worker and by the on-premises Agent.
/// </summary>
public sealed class CollectionEngine(
    ITargetConnectionFactory connections,
    IServerProbe probe,
    IEnumerable<ICollector> collectors,
    TimeProvider clock,
    ILogger<CollectionEngine> logger)
{
    /// <summary>Capability detection is repeated this often (also right after start).</summary>
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromHours(6);

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);
    private readonly ICollector[] _collectors = collectors.ToArray();
    private readonly ConcurrentDictionary<int, ServerRuntime> _servers = new();

    public int ServerCount => _servers.Count;

    /// <summary>Replaces the list of servers to collect. Unchanged servers keep their schedule and counters.</summary>
    public void UpdateTargets(IEnumerable<CollectionTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var now = clock.GetUtcNow().UtcDateTime;
        var wanted = targets.ToDictionary(t => t.ServerId);

        foreach (var id in _servers.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
        {
            _servers.TryRemove(id, out _);
        }

        foreach (var t in wanted.Values)
        {
            _servers.AddOrUpdate(t.ServerId,
                _ => new ServerRuntime(t, now),
                (_, existing) =>
                {
                    if (existing.Target.Fingerprint == t.Fingerprint)
                    {
                        existing.Target = t; // fresh secrets etc., same schedule
                        return existing;
                    }

                    // Connection or capabilities changed: start clean (probe again first).
                    return new ServerRuntime(t, now);
                });
        }
    }

    /// <summary>Runs everything that is due now. Returns the batches to store.</summary>
    public async Task<IReadOnlyList<CollectionBatch>> RunDueAsync(int maxParallelServers, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = _servers.Values.Where(s => s.NextAttemptUtc <= now && s.HasWorkDue(now, _collectors)).ToList();
        if (due.Count == 0) return [];

        var results = new ConcurrentBag<CollectionBatch>();
        using var throttle = new SemaphoreSlim(Math.Max(1, maxParallelServers));
        await Task.WhenAll(due.Select(async s =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                foreach (var b in await RunServerAsync(s, ct)) results.Add(b);
            }
            finally
            {
                throttle.Release();
            }
        }));

        return results.OrderBy(b => b.StartedUtc).ToList();
    }

    private async Task<List<CollectionBatch>> RunServerAsync(ServerRuntime rt, CancellationToken ct)
    {
        var batches = new List<CollectionBatch>();
        if (!await rt.Lock.WaitAsync(0, ct)) return batches; // previous run still busy

        try
        {
            var t = rt.Target;
            var now = clock.GetUtcNow().UtcDateTime;

            if (rt.NextProbeUtc <= now)
            {
                var sw = Stopwatch.StartNew();
                var result = await probe.ProbeAsync(t.Monitoring, t.Admin, ct);
                batches.Add(new CollectionBatch
                {
                    ServerId = t.ServerId,
                    Collector = CollectorNames.Probe,
                    StartedUtc = now,
                    DurationMs = (int)sw.ElapsedMilliseconds,
                    Success = result.Success,
                    ConnectionStatus = result.Success ? null : result.Status,
                    Error = result.Error,
                    Probe = result,
                });

                if (!result.Success)
                {
                    rt.Fail(now, MaxBackoff);
                    return batches;
                }

                rt.NextProbeUtc = now + ProbeInterval;
            }

            var dueCollectors = _collectors.Where(c => c.IsSupported(t) && rt.NextDue(c.Name) <= now).ToList();
            if (dueCollectors.Count == 0)
            {
                rt.Succeed();
                return batches;
            }

            SqlConnection connection;
            var connectWatch = Stopwatch.StartNew();
            try
            {
                connection = await connections.OpenAsync(t.Monitoring, TargetSessionMode.Monitoring, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Cannot connect to server {Server} ({ServerId}): {Message}", t.DisplayName, t.ServerId, ex.Message);
                batches.Add(new CollectionBatch
                {
                    ServerId = t.ServerId,
                    Collector = CollectorNames.Connection,
                    StartedUtc = now,
                    DurationMs = (int)connectWatch.ElapsedMilliseconds,
                    Success = false,
                    ConnectionStatus = ConnectionErrors.StatusOf(ex),
                    Error = ConnectionErrors.Describe(ex, t.Monitoring),
                });
                rt.Fail(now, MaxBackoff);
                return batches;
            }

            await using (connection)
            {
                foreach (var c in dueCollectors)
                {
                    var started = clock.GetUtcNow().UtcDateTime;
                    var batch = new CollectionBatch { ServerId = t.ServerId, Collector = c.Name, StartedUtc = started };
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        await c.CollectAsync(connection, t, rt.State, batch, ct);
                        batch.Success = true;
                    }
                    catch (SqlException ex)
                    {
                        batch.Success = false;
                        batch.Error = $"SQL error {ex.Number}: {ex.Message}";
                        logger.LogWarning("Collector {Collector} failed on server {ServerId}: {Message}", c.Name, t.ServerId, ex.Message);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        batch.Success = false;
                        batch.Error = ex.Message;
                        logger.LogError(ex, "Collector {Collector} crashed on server {ServerId}", c.Name, t.ServerId);
                    }

                    batch.DurationMs = (int)sw.ElapsedMilliseconds;
                    rt.SetNextDue(c.Name, started + c.Interval(t));
                    batches.Add(batch);
                }
            }

            rt.Succeed();
            return batches;
        }
        finally
        {
            rt.Lock.Release();
        }
    }

    private sealed class ServerRuntime(CollectionTarget target, DateTime now)
    {
        private readonly Dictionary<string, DateTime> _nextDue = new(StringComparer.Ordinal);
        private int _failures;

        public CollectionTarget Target { get; set; } = target;
        public CollectorState State { get; } = new();
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public DateTime NextProbeUtc { get; set; } = now;
        public DateTime NextAttemptUtc { get; private set; } = now;

        public DateTime NextDue(string collector) =>
            _nextDue.TryGetValue(collector, out var at) ? at : DateTime.MinValue;

        public void SetNextDue(string collector, DateTime at) => _nextDue[collector] = at;

        public bool HasWorkDue(DateTime now, IEnumerable<ICollector> all) =>
            NextProbeUtc <= now || all.Any(c => c.IsSupported(Target) && NextDue(c.Name) <= now);

        /// <summary>Exponential back-off: 30 s, 1 min, 2 min, 4 min, then every 5 min.</summary>
        public void Fail(DateTime now, TimeSpan max)
        {
            _failures++;
            var delay = TimeSpan.FromSeconds(30 * Math.Pow(2, Math.Min(_failures - 1, 6)));
            NextAttemptUtc = now + (delay > max ? max : delay);
            NextProbeUtc = now; // re-detect capabilities once it is back
        }

        public void Succeed() => _failures = 0;
    }
}
