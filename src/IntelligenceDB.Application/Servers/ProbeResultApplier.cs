using System.Text.Json;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.Application.Servers;

/// <summary>
/// Copies a probe (connection test) result onto a server entity: status, info, platform and capabilities.
/// Used by interactive tests (API), the central collector (Worker) and probes sent in by agents.
/// </summary>
public static class ProbeResultApplier
{
    /// <param name="server">Tracked server with Info and Capabilities loaded.</param>
    public static (PlatformType? Platform, IReadOnlyList<CapabilityResult> Capabilities) Apply(Server server, ProbeResult result, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(result);

        server.LastCheckedUtc = now;
        server.LastStatus = result.Status;
        server.LastError = result.Success ? null : Truncate(result.Error, 2000);

        var (platform, caps) = Evaluate(result, server.DeclaredPlatform, server.AdminCredentialId is not null || server.AdminCredential is not null);
        if (platform is null || result.Info is null)
        {
            // Keep the last known info/capabilities; the status already shows the failure.
            return (null, []);
        }

        server.DetectedPlatform = platform.Value;

        var info = server.Info ??= new ServerInfo { ServerId = server.Id };
        var snap = result.Info;
        info.ProductVersion = snap.ProductVersion;
        info.MajorVersion = snap.MajorVersion;
        info.ProductLevel = snap.ProductLevel;
        info.ProductUpdateLevel = snap.ProductUpdateLevel;
        info.Edition = snap.Edition;
        info.EngineEdition = snap.EngineEdition;
        info.HostPlatform = snap.HostPlatform;
        info.CpuCount = snap.CpuCount;
        info.PhysicalMemoryMb = snap.PhysicalMemoryMb;
        info.SqlStartTimeUtc = snap.SqlStartTimeUtc;
        info.Collation = snap.Collation;
        info.ServerName = snap.ServerName;
        info.IsSysadmin = snap.IsSysadmin;
        info.PermissionsJson = JsonSerializer.Serialize(result.Permissions);
        info.DetectedUtc = now;

        // Update in place (rather than delete + insert) so EF never tracks two rows with the same key.
        var existing = server.Capabilities.ToDictionary(c => c.CapabilityKey, StringComparer.Ordinal);
        foreach (var cap in caps)
        {
            if (!existing.Remove(cap.Key, out var row))
            {
                row = new ServerCapability { ServerId = server.Id, CapabilityKey = cap.Key };
                server.Capabilities.Add(row);
            }

            row.IsSupported = cap.IsSupported;
            row.Reason = Truncate(cap.Reason, 1000);
            row.Alternative = Truncate(cap.Alternative, 1000);
            row.DetectedUtc = now;
        }

        foreach (var stale in existing.Values)
        {
            server.Capabilities.Remove(stale);
        }

        return (platform, caps);
    }

    public static (PlatformType? Platform, IReadOnlyList<CapabilityResult> Capabilities) Evaluate(
        ProbeResult result, PlatformType declared, bool adminConfigured)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Success || result.Facts is null)
        {
            return (null, []);
        }

        var platform = PlatformDetector.Detect(result.Facts, declared);
        var admin = new AdminProbeOutcome(adminConfigured, result.AdminFacts, result.AdminError);
        return (platform, CapabilityEvaluator.Evaluate(platform, result.Facts, admin));
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
