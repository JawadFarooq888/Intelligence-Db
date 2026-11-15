using IntelligenceDB.Domain.Audit;
using IntelligenceDB.Domain.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IntelligenceDB.Infrastructure.Repository.Configurations;

internal sealed class ServerConfiguration : IEntityTypeConfiguration<Server>
{
    public void Configure(EntityTypeBuilder<Server> e)
    {
        e.ToTable("Servers", "inv");
        e.HasKey(s => s.Id);
        e.Property(s => s.DisplayName).HasMaxLength(128).IsRequired();
        e.HasIndex(s => s.DisplayName).IsUnique();
        e.Property(s => s.Host).HasMaxLength(253).IsRequired();
        e.Property(s => s.InstanceName).HasMaxLength(16);
        e.Property(s => s.DatabaseName).HasMaxLength(128);
        e.Property(s => s.LastError).HasMaxLength(2000);
        e.Property(s => s.RowVersion).IsRowVersion();
        e.Ignore(s => s.EffectivePlatform);

        e.HasOne(s => s.Group).WithMany(g => g.Servers).HasForeignKey(s => s.GroupId).OnDelete(DeleteBehavior.SetNull);

        // Two FKs to the same table: SQL Server forbids multiple cascade paths, so credentials
        // are deleted explicitly by the service when a server is removed.
        e.HasOne(s => s.MonitoringCredential).WithMany().HasForeignKey(s => s.MonitoringCredentialId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(s => s.AdminCredential).WithMany().HasForeignKey(s => s.AdminCredentialId).OnDelete(DeleteBehavior.Restrict);

        e.HasMany(s => s.Tags).WithOne().HasForeignKey(t => t.ServerId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(s => s.Info).WithOne().HasForeignKey<ServerInfo>(i => i.ServerId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(s => s.Capabilities).WithOne().HasForeignKey(c => c.ServerId).OnDelete(DeleteBehavior.Cascade);

        // Phase 2: optional agent (cannot delete an agent that still has servers) and health score.
        e.HasOne(s => s.Agent).WithMany().HasForeignKey(s => s.AgentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(s => s.Health).WithOne().HasForeignKey<ServerHealth>(h => h.ServerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServerGroupConfiguration : IEntityTypeConfiguration<ServerGroup>
{
    public void Configure(EntityTypeBuilder<ServerGroup> e)
    {
        e.ToTable("ServerGroups", "inv");
        e.HasKey(g => g.Id);
        e.Property(g => g.Name).HasMaxLength(100).IsRequired();
        e.HasIndex(g => g.Name).IsUnique();
        e.Property(g => g.Description).HasMaxLength(500);
    }
}

internal sealed class CredentialConfiguration : IEntityTypeConfiguration<Credential>
{
    public void Configure(EntityTypeBuilder<Credential> e)
    {
        e.ToTable("Credentials", "inv");
        e.HasKey(c => c.Id);
        e.Property(c => c.UserName).HasMaxLength(256);
        e.Property(c => c.TenantId).HasMaxLength(64);
        e.Property(c => c.ClientId).HasMaxLength(64);
        // ProtectedSecret: varbinary(max), already encrypted by Data Protection.
    }
}

internal sealed class ServerTagConfiguration : IEntityTypeConfiguration<ServerTag>
{
    public void Configure(EntityTypeBuilder<ServerTag> e)
    {
        e.ToTable("ServerTags", "inv");
        e.HasKey(t => new { t.ServerId, t.Tag });
        e.Property(t => t.Tag).HasMaxLength(50);
        e.HasIndex(t => t.Tag);
    }
}

internal sealed class ServerInfoConfiguration : IEntityTypeConfiguration<ServerInfo>
{
    public void Configure(EntityTypeBuilder<ServerInfo> e)
    {
        e.ToTable("ServerInfo", "inv");
        e.HasKey(i => i.ServerId);
        e.Property(i => i.ServerId).ValueGeneratedNever();
        e.Property(i => i.ProductVersion).HasMaxLength(128).IsRequired();
        e.Property(i => i.ProductLevel).HasMaxLength(128);
        e.Property(i => i.ProductUpdateLevel).HasMaxLength(128);
        e.Property(i => i.Edition).HasMaxLength(128).IsRequired();
        e.Property(i => i.HostPlatform).HasMaxLength(256);
        e.Property(i => i.Collation).HasMaxLength(128);
        e.Property(i => i.ServerName).HasMaxLength(256);
        e.Property(i => i.PermissionsJson).IsRequired();
    }
}

internal sealed class ServerCapabilityConfiguration : IEntityTypeConfiguration<ServerCapability>
{
    public void Configure(EntityTypeBuilder<ServerCapability> e)
    {
        e.ToTable("ServerCapabilities", "inv");
        e.HasKey(c => new { c.ServerId, c.CapabilityKey });
        e.Property(c => c.CapabilityKey).HasMaxLength(64);
        e.Property(c => c.Reason).HasMaxLength(1000);
        e.Property(c => c.Alternative).HasMaxLength(1000);
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> e)
    {
        e.ToTable("AuditLog", "audit");
        e.HasKey(a => a.Id);
        e.Property(a => a.UserId).HasMaxLength(64);
        e.Property(a => a.UserName).HasMaxLength(256);
        e.Property(a => a.ClientIp).HasMaxLength(64);
        e.Property(a => a.Action).HasMaxLength(100).IsRequired();
        e.Property(a => a.ServerName).HasMaxLength(128);
        e.Property(a => a.DatabaseName).HasMaxLength(128);
        e.Property(a => a.Result).HasMaxLength(20).IsRequired();
        e.Property(a => a.Error).HasMaxLength(4000);
        e.HasIndex(a => a.OccurredUtc);
        e.HasIndex(a => new { a.ServerId, a.OccurredUtc });
    }
}

internal sealed class AppLogConfiguration : IEntityTypeConfiguration<AppLogEntry>
{
    public void Configure(EntityTypeBuilder<AppLogEntry> e)
    {
        e.ToTable("AppLog", "log");
        e.HasKey(l => l.Id);
        e.Property(l => l.Level).HasMaxLength(128);
        e.HasIndex(l => l.TimeStamp);
    }
}
