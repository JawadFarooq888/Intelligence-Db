using System.Globalization;
using IntelligenceDB.Application.Servers;
using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.Application.Monitoring;

/// <summary>Turns a server row (with credentials and capabilities loaded) into a collection target.</summary>
public sealed class CollectionTargetFactory(TargetSpecBuilder specs)
{
    public CollectionTarget Create(Server s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var monitoring = specs.FromServer(s, s.MonitoringCredential);
        var admin = s.AdminCredential is null ? null : specs.FromServer(s, s.AdminCredential);

        return new CollectionTarget
        {
            ServerId = s.Id,
            DisplayName = s.DisplayName,
            Monitoring = monitoring,
            Admin = admin,
            Platform = s.EffectivePlatform,
            SupportedCapabilities = s.Capabilities.Where(c => c.IsSupported).Select(c => c.CapabilityKey).ToHashSet(StringComparer.Ordinal),
            MetricIntervalSeconds = s.CollectionIntervalSeconds,
            Fingerprint = Fingerprint(s),
        };
    }

    /// <summary>Anything that changes how we connect or what we collect changes the fingerprint.</summary>
    private static string Fingerprint(Server s) => string.Join('|',
        s.Host, s.Port?.ToString(CultureInfo.InvariantCulture), s.InstanceName, s.DatabaseName, s.Encrypt, s.TrustServerCertificate,
        s.MonitoringCredential.AuthType, s.MonitoringCredential.UserName, s.MonitoringCredential.RotatedUtc?.Ticks,
        s.AdminCredential?.AuthType, s.AdminCredential?.UserName, s.AdminCredential?.RotatedUtc?.Ticks,
        s.CollectionIntervalSeconds, s.EffectivePlatform,
        string.Join(',', s.Capabilities.Where(c => c.IsSupported).Select(c => c.CapabilityKey).Order(StringComparer.Ordinal)));
}
