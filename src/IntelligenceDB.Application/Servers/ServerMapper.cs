using System.Text.Json;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Capabilities;
using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.Application.Servers;

internal static class ServerMapper
{
    public static ServerSummaryDto ToSummary(Server s) => new(
        s.Id,
        s.DisplayName,
        s.Host,
        s.Port,
        s.InstanceName,
        s.DatabaseName,
        s.DeclaredPlatform,
        s.DetectedPlatform,
        s.EffectivePlatform,
        s.Environment,
        s.GroupId,
        s.Group?.Name,
        s.Tags.Select(t => t.Tag).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(),
        s.IsEnabled,
        s.LastStatus,
        s.LastCheckedUtc,
        s.LastError,
        s.Info is null ? null : VersionNames.Describe(s.Info.EngineEdition, s.Info.MajorVersion, s.Info.ProductLevel, s.Info.ProductUpdateLevel),
        s.Info?.Edition,
        s.AdminCredentialId is not null,
        s.AgentId,
        s.Agent?.Name,
        s.Health?.Score);

    public static ServerDetailDto ToDetail(Server s) => new(
        ToSummary(s),
        s.Encrypt,
        s.TrustServerCertificate,
        s.ConnectTimeoutSeconds,
        s.CollectionIntervalSeconds,
        ToDto(s.MonitoringCredential),
        s.AdminCredential is null ? null : ToDto(s.AdminCredential),
        s.Info is null ? null : ToDto(s.Info),
        ParsePermissions(s.Info?.PermissionsJson),
        OrderCapabilities(s.Capabilities.Select(c =>
            ToDto(new CapabilityResult(c.CapabilityKey, c.IsSupported, c.Reason, c.Alternative)))),
        PlatformDetector.MismatchWarning(s.DeclaredPlatform, s.DetectedPlatform),
        Convert.ToBase64String(s.RowVersion));

    public static CredentialDto ToDto(Credential c) =>
        new(c.AuthType, c.UserName, c.ProtectedSecret is { Length: > 0 }, c.TenantId, c.ClientId);

    public static ServerInfoDto ToDto(ServerInfo i) => new(
        i.ProductVersion,
        i.MajorVersion,
        i.ProductLevel,
        i.ProductUpdateLevel,
        VersionNames.Describe(i.EngineEdition, i.MajorVersion, i.ProductLevel, i.ProductUpdateLevel),
        i.Edition,
        i.EngineEdition,
        i.HostPlatform,
        i.CpuCount,
        i.PhysicalMemoryMb,
        i.SqlStartTimeUtc,
        i.Collation,
        i.ServerName,
        i.IsSysadmin,
        i.DetectedUtc);

    public static CapabilityDto ToDto(CapabilityResult r)
    {
        var (title, description) = CapabilityKeys.Catalog.TryGetValue(r.Key, out var entry)
            ? entry
            : (r.Key, string.Empty);
        return new CapabilityDto(r.Key, title, description, r.IsSupported, r.Reason, r.Alternative);
    }

    /// <summary>Keeps capabilities in catalog order so the UI list is stable.</summary>
    public static IReadOnlyList<CapabilityDto> OrderCapabilities(IEnumerable<CapabilityDto> items)
    {
        var order = CapabilityKeys.Catalog.Keys.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);
        return items.OrderBy(c => order.TryGetValue(c.Key, out var i) ? i : int.MaxValue).ToList();
    }

    public static IReadOnlyList<string> ParsePermissions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static TestConnectionResultDto ToTestDto(
        ProbeResult result,
        PlatformType declared,
        PlatformType? detected,
        IReadOnlyList<CapabilityResult> capabilities) => new(
        result.Success,
        result.Status,
        result.Error,
        result.ElapsedMs,
        detected,
        detected is null ? null : PlatformDetector.MismatchWarning(declared, detected.Value),
        result.Info is null ? null : VersionNames.Describe(result.Info.EngineEdition, result.Info.MajorVersion, result.Info.ProductLevel, result.Info.ProductUpdateLevel),
        result.Info?.Edition,
        result.Info?.IsSysadmin,
        result.Permissions,
        OrderCapabilities(capabilities.Select(ToDto)),
        result.AdminTested,
        result.AdminFacts is not null,
        result.AdminError);
}
