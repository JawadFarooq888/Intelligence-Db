using IntelligenceDB.Domain.Agents;
using IntelligenceDB.Domain.Monitoring;
using IntelligenceDB.Domain.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IntelligenceDB.Infrastructure.Repository.Configurations;

// Phase 2 tables. History tables are clustered on (ServerId, CollectedUtc) so time-range queries
// per server read a contiguous range.

internal sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> e)
    {
        e.ToTable("Agents", "inv");
        e.HasKey(a => a.Id);
        e.Property(a => a.Id).ValueGeneratedNever();
        e.Property(a => a.Name).HasMaxLength(100).IsRequired();
        e.HasIndex(a => a.Name).IsUnique();
        e.Property(a => a.KeyHash).HasMaxLength(64).IsRequired();
        e.Property(a => a.KeyPrefix).HasMaxLength(16).IsRequired();
        e.Property(a => a.Version).HasMaxLength(50);
        e.Property(a => a.MachineName).HasMaxLength(128);
        e.Property(a => a.LastIp).HasMaxLength(64);
    }
}

internal sealed class AgentCommandConfiguration : IEntityTypeConfiguration<AgentCommand>
{
    public void Configure(EntityTypeBuilder<AgentCommand> e)
    {
        e.ToTable("AgentCommands", "ops");
        e.HasKey(c => c.Id);
        e.Property(c => c.Type).HasMaxLength(32).IsRequired();
        e.Property(c => c.Status).HasMaxLength(16).IsRequired();
        e.HasIndex(c => new { c.AgentId, c.Status });
        e.HasOne<Agent>().WithMany().HasForeignKey(c => c.AgentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DatabaseConfiguration : IEntityTypeConfiguration<Database>
{
    public void Configure(EntityTypeBuilder<Database> e)
    {
        e.ToTable("Databases", "inv");
        e.HasKey(d => d.Id);
        e.Property(d => d.Name).HasMaxLength(128).IsRequired();
        e.HasIndex(d => new { d.ServerId, d.Name }).IsUnique();
        e.Property(d => d.State).HasMaxLength(60);
        e.Property(d => d.RecoveryModel).HasMaxLength(60);
        e.Property(d => d.Owner).HasMaxLength(128);
        e.Property(d => d.LogReuseWait).HasMaxLength(60);
        e.HasOne<Server>().WithMany().HasForeignKey(d => d.ServerId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(d => d.Files).WithOne().HasForeignKey(f => f.DatabaseId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DatabaseFileConfiguration : IEntityTypeConfiguration<DatabaseFile>
{
    public void Configure(EntityTypeBuilder<DatabaseFile> e)
    {
        e.ToTable("DatabaseFiles", "inv");
        e.HasKey(f => new { f.DatabaseId, f.FileId });
        e.Property(f => f.FileId).ValueGeneratedNever();
        e.Property(f => f.LogicalName).HasMaxLength(128);
        e.Property(f => f.PhysicalName).HasMaxLength(260);
        e.Property(f => f.TypeDesc).HasMaxLength(60);
        e.Property(f => f.VolumeMountPoint).HasMaxLength(260);
    }
}

internal sealed class ServerConfigSettingConfiguration : IEntityTypeConfiguration<ServerConfigSetting>
{
    public void Configure(EntityTypeBuilder<ServerConfigSetting> e)
    {
        e.ToTable("ServerConfig", "inv");
        e.HasKey(c => new { c.ServerId, c.Name });
        e.Property(c => c.Name).HasMaxLength(128);
        e.Property(c => c.Description).HasMaxLength(512);
        e.HasOne<Server>().WithMany().HasForeignKey(c => c.ServerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServerHealthConfiguration : IEntityTypeConfiguration<ServerHealth>
{
    public void Configure(EntityTypeBuilder<ServerHealth> e)
    {
        e.ToTable("ServerHealth", "hc");
        e.HasKey(h => h.ServerId);
        e.Property(h => h.ServerId).ValueGeneratedNever();
        e.Property(h => h.BreakdownJson).IsRequired();
    }
}

internal sealed class CollectionRunConfiguration : IEntityTypeConfiguration<CollectionRun>
{
    public void Configure(EntityTypeBuilder<CollectionRun> e)
    {
        e.ToTable("CollectionRuns", "mon");
        e.HasKey(r => r.Id).IsClustered(false);
        e.HasIndex(r => new { r.ServerId, r.StartedUtc }).IsClustered();
        e.Property(r => r.Collector).HasMaxLength(32).IsRequired();
        e.Property(r => r.Source).HasMaxLength(128).IsRequired();
        e.Property(r => r.Error).HasMaxLength(2000);
        e.HasOne<Server>().WithMany().HasForeignKey(r => r.ServerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServerMetricConfiguration : IEntityTypeConfiguration<ServerMetric>
{
    public void Configure(EntityTypeBuilder<ServerMetric> e)
    {
        e.ToTable("ServerMetrics", "mon", t => t.HasComment("Server performance samples"));
        e.HasKey(m => m.Id).IsClustered(false);
        e.HasIndex(m => new { m.ServerId, m.CollectedUtc }).IsClustered();
        e.HasOne<Server>().WithMany().HasForeignKey(m => m.ServerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WaitStatConfiguration : IEntityTypeConfiguration<WaitStat>
{
    public void Configure(EntityTypeBuilder<WaitStat> e)
    {
        e.ToTable("WaitStats", "mon");
        e.HasKey(w => w.Id).IsClustered(false);
        e.HasIndex(w => new { w.ServerId, w.CollectedUtc }).IsClustered();
        e.Property(w => w.WaitType).HasMaxLength(120).IsRequired();
        e.HasOne<Server>().WithMany().HasForeignKey(w => w.ServerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FileIoStatConfiguration : IEntityTypeConfiguration<FileIoStat>
{
    public void Configure(EntityTypeBuilder<FileIoStat> e)
    {
        e.ToTable("FileIoStats", "mon");
        e.HasKey(f => f.Id).IsClustered(false);
        e.HasIndex(f => new { f.ServerId, f.CollectedUtc }).IsClustered();
        e.Property(f => f.DatabaseName).HasMaxLength(128).IsRequired();
        e.HasOne<Server>().WithMany().HasForeignKey(f => f.ServerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VolumeStatConfiguration : IEntityTypeConfiguration<VolumeStat>
{
    public void Configure(EntityTypeBuilder<VolumeStat> e)
    {
        e.ToTable("VolumeStats", "mon");
        e.HasKey(v => v.Id).IsClustered(false);
        e.HasIndex(v => new { v.ServerId, v.CollectedUtc }).IsClustered();
        e.Property(v => v.MountPoint).HasMaxLength(260).IsRequired();
        e.Property(v => v.Label).HasMaxLength(128);
        e.HasOne<Server>().WithMany().HasForeignKey(v => v.ServerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DatabaseSizeSampleConfiguration : IEntityTypeConfiguration<DatabaseSizeSample>
{
    public void Configure(EntityTypeBuilder<DatabaseSizeSample> e)
    {
        e.ToTable("DatabaseSizes", "mon");
        e.HasKey(s => s.Id).IsClustered(false);
        e.HasIndex(s => new { s.DatabaseId, s.CollectedUtc }).IsClustered();
        e.HasOne<Database>().WithMany().HasForeignKey(s => s.DatabaseId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BackupRecordConfiguration : IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<BackupRecord> e)
    {
        e.ToTable("Backups", "mon");
        e.HasKey(b => b.Id).IsClustered(false);
        e.HasIndex(b => new { b.ServerId, b.DatabaseName, b.FinishUtc }).IsClustered();
        e.HasIndex(b => new { b.ServerId, b.SourceBackupSetId }).IsUnique();
        e.Property(b => b.DatabaseName).HasMaxLength(128).IsRequired();
        e.Property(b => b.BackupType).HasMaxLength(1).IsFixedLength().IsRequired();
        e.Property(b => b.RecoveryModel).HasMaxLength(60);
        e.Property(b => b.PhysicalDevice).HasMaxLength(400);
        e.HasOne<Server>().WithMany().HasForeignKey(b => b.ServerId).OnDelete(DeleteBehavior.Cascade);
    }
}
