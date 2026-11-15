using IntelligenceDB.Domain.Audit;
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

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
