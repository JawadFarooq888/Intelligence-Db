using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Domain.Agents;
using IntelligenceDB.Domain.Monitoring;
using IntelligenceDB.Domain.Audit;
using IntelligenceDB.Domain.Servers;
using IntelligenceDB.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IntelligenceDB.Infrastructure.Repository;

/// <summary>
/// EF Core context for IntelligenceDB_Repo. Tables are grouped by schema:
/// auth (portal users), inv (inventory), audit, ops (operational), log (application log).
/// </summary>
public class RepoDbContext(DbContextOptions<RepoDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IRepoDbContext, IDataProtectionKeyContext
{
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<ServerGroup> ServerGroups => Set<ServerGroup>();
    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<ServerTag> ServerTags => Set<ServerTag>();
    public DbSet<ServerInfo> ServerInfos => Set<ServerInfo>();
    public DbSet<ServerCapability> ServerCapabilities => Set<ServerCapability>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentCommand> AgentCommands => Set<AgentCommand>();
    public DbSet<Database> Databases => Set<Database>();
    public DbSet<DatabaseFile> DatabaseFiles => Set<DatabaseFile>();
    public DbSet<ServerConfigSetting> ServerConfigSettings => Set<ServerConfigSetting>();
    public DbSet<ServerHealth> ServerHealth => Set<ServerHealth>();
    public DbSet<CollectionRun> CollectionRuns => Set<CollectionRun>();
    public DbSet<ServerMetric> ServerMetrics => Set<ServerMetric>();
    public DbSet<WaitStat> WaitStats => Set<WaitStat>();
    public DbSet<FileIoStat> FileIoStats => Set<FileIoStat>();
    public DbSet<VolumeStat> VolumeStats => Set<VolumeStat>();
    public DbSet<DatabaseSizeSample> DatabaseSizeSamples => Set<DatabaseSizeSample>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();
    public DbSet<UserServerGroup> UserServerGroups => Set<UserServerGroup>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AppLogEntry> AppLog => Set<AppLogEntry>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // All timestamps are UTC with millisecond precision: datetime2(3).
        configurationBuilder.Properties<DateTime>().HavePrecision(3);
        configurationBuilder.Properties<DateTime?>().HavePrecision(3);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        ConfigureIdentity(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(RepoDbContext).Assembly);

        builder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys", "ops");
    }

    private static void ConfigureIdentity(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.ToTable("Users", "auth");
            e.Property(u => u.DisplayName).HasMaxLength(128);
        });
        b.Entity<AppRole>().ToTable("Roles", "auth");
        b.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles", "auth");
        b.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", "auth");
        b.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", "auth");
        b.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims", "auth");
        b.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", "auth");

        b.Entity<UserServerGroup>(e =>
        {
            e.ToTable("UserServerGroups", "auth");
            e.HasKey(x => new { x.UserId, x.ServerGroupId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ServerGroup>().WithMany().HasForeignKey(x => x.ServerGroupId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshTokens", "auth");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.CreatedIp).HasMaxLength(64);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Serilog MSSqlServer sink table (default column layout).</summary>
public class AppLogEntry
{
    public int Id { get; set; }
    public string? Message { get; set; }
    public string? MessageTemplate { get; set; }
    public string? Level { get; set; }
    public DateTime TimeStamp { get; set; }
    public string? Exception { get; set; }
    public string? Properties { get; set; }
}
