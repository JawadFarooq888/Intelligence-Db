using IntelligenceDB.Domain.Agents;
using IntelligenceDB.Domain.Audit;
using IntelligenceDB.Domain.Monitoring;
using IntelligenceDB.Domain.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace IntelligenceDB.Application.Abstractions;

/// <summary>The repository database (IntelligenceDB_Repo) as seen by the application layer.</summary>
public interface IRepoDbContext
{
    DbSet<Server> Servers { get; }
    DbSet<ServerGroup> ServerGroups { get; }
    DbSet<Credential> Credentials { get; }
    DbSet<ServerTag> ServerTags { get; }
    DbSet<ServerInfo> ServerInfos { get; }
    DbSet<ServerCapability> ServerCapabilities { get; }
    DbSet<AuditEntry> AuditEntries { get; }

    // Phase 2: agents, inventory and monitoring history
    DbSet<Agent> Agents { get; }
    DbSet<AgentCommand> AgentCommands { get; }
    DbSet<Database> Databases { get; }
    DbSet<DatabaseFile> DatabaseFiles { get; }
    DbSet<ServerConfigSetting> ServerConfigSettings { get; }
    DbSet<ServerHealth> ServerHealth { get; }
    DbSet<CollectionRun> CollectionRuns { get; }
    DbSet<ServerMetric> ServerMetrics { get; }
    DbSet<WaitStat> WaitStats { get; }
    DbSet<FileIoStat> FileIoStats { get; }
    DbSet<VolumeStat> VolumeStats { get; }
    DbSet<DatabaseSizeSample> DatabaseSizeSamples { get; }
    DbSet<BackupRecord> BackupRecords { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs a connection test through an agent (for servers inside a customer network).
/// Waits for the agent's answer; returns a failed result if the agent does not answer in time.
/// </summary>
public interface IAgentProbeDispatcher
{
    Task<ProbeResult> ProbeAsync(Guid agentId, TargetConnectionSpec monitoring, TargetConnectionSpec? admin, CancellationToken cancellationToken);
}

/// <summary>Payload of an agent "Probe" command (contains secrets: stored encrypted, deleted after use).</summary>
public sealed class ProbeCommandPayload
{
    public TargetConnectionSpec Monitoring { get; set; } = null!;
    public TargetConnectionSpec? Admin { get; set; }
}
