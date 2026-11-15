using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Application.Servers;
using IntelligenceDB.Domain.Servers;
using Microsoft.EntityFrameworkCore;

namespace IntelligenceDB.Application.Dashboard;

public sealed record ServerCardDto(
    int Id,
    string DisplayName,
    string Host,
    PlatformType Platform,
    ServerEnvironment Environment,
    string? GroupName,
    bool IsEnabled,
    ConnectionStatus Status,
    DateTime? LastCheckedUtc,
    string? LastError,
    string? VersionLabel,
    string? Edition,
    int? CpuCount,
    long? PhysicalMemoryMb,
    DateTime? SqlStartTimeUtc,
    IReadOnlyList<string> Issues);

public sealed record DashboardDto(
    int TotalServers,
    int Online,
    int Unreachable,
    int NotChecked,
    int Disabled,
    IReadOnlyList<ServerCardDto> Servers);

/// <summary>
/// Phase 1 dashboard: reachability, version and capability problems per server.
/// The 0-100 health score is added in Phase 2 when the collector stores metrics.
/// </summary>
public sealed class DashboardService(IRepoDbContext db, ICurrentUser user)
{
    // Capability problems worth showing on the dashboard card (others only matter on detail pages).
    private static readonly HashSet<string> CardCapabilities =
    [
        CapabilityKeys.ServerStateDmvs,
        CapabilityKeys.MsdbAccess,
        CapabilityKeys.SqlAgent,
    ];

    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var scope = await user.GetScopeAsync(ct);
        var servers = await db.Servers.AsNoTracking()
            .VisibleTo(scope)
            .Include(s => s.Group)
            .Include(s => s.Info)
            .Include(s => s.Capabilities)
            .OrderBy(s => s.DisplayName)
            .AsSplitQuery()
            .ToListAsync(ct);

        var cards = servers
            .Select(ToCard)
            // Problems first, then by name.
            .OrderBy(c => c.Status == ConnectionStatus.Online ? 1 : 0)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DashboardDto(
            servers.Count,
            servers.Count(s => s.IsEnabled && s.LastStatus == ConnectionStatus.Online),
            servers.Count(s => s.IsEnabled && s.LastStatus is ConnectionStatus.Unreachable or ConnectionStatus.AuthFailed),
            servers.Count(s => s.IsEnabled && s.LastStatus == ConnectionStatus.Unknown),
            servers.Count(s => !s.IsEnabled),
            cards);
    }

    private static ServerCardDto ToCard(Server s)
    {
        var issues = new List<string>();
        if (!s.IsEnabled) issues.Add("Monitoring is disabled for this server.");
        if (s.LastStatus == ConnectionStatus.Unreachable) issues.Add("Server is unreachable.");
        if (s.LastStatus == ConnectionStatus.AuthFailed) issues.Add("Login failed - check the monitoring credentials.");
        if (s.LastStatus == ConnectionStatus.Unknown) issues.Add("Not tested yet.");

        if (s.LastStatus == ConnectionStatus.Online)
        {
            issues.AddRange(s.Capabilities
                .Where(c => !c.IsSupported && CardCapabilities.Contains(c.CapabilityKey) && c.Reason is not null)
                .Select(c => c.Reason!));
        }

        return new ServerCardDto(
            s.Id,
            s.DisplayName,
            s.Host,
            s.EffectivePlatform,
            s.Environment,
            s.Group?.Name,
            s.IsEnabled,
            s.LastStatus,
            s.LastCheckedUtc,
            s.LastError,
            s.Info is null ? null : VersionNames.Describe(s.Info.EngineEdition, s.Info.MajorVersion, s.Info.ProductLevel, s.Info.ProductUpdateLevel),
            s.Info?.Edition,
            s.Info?.CpuCount,
            s.Info?.PhysicalMemoryMb,
            s.Info?.SqlStartTimeUtc,
            issues);
    }
}
