namespace IntelligenceDB.Application.Monitoring;

/// <summary>appsettings section "Monitoring".</summary>
public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    public MonitoringThresholds Thresholds { get; set; } = new();

    /// <summary>
    /// Optional list of the newest cumulative update per major version, used to flag outdated servers.
    /// Example: { "Major": 16, "Build": "16.0.4215.2", "Label": "SQL Server 2022 CU21" }.
    /// Keep it up to date from Microsoft's "latest updates for SQL Server" page.
    /// </summary>
    public List<LatestBuildSetting> LatestBuilds { get; set; } = [];

    /// <summary>How long history is kept (days).</summary>
    public RetentionSettings Retention { get; set; } = new();

    public LatestBuild? LatestFor(int major)
    {
        var s = LatestBuilds.FirstOrDefault(b => b.Major == major);
        return s is null || string.IsNullOrWhiteSpace(s.Build) ? null : new LatestBuild(s.Major, s.Build, s.Label ?? s.Build);
    }
}

public sealed class LatestBuildSetting
{
    public int Major { get; set; }
    public string Build { get; set; } = string.Empty;
    public string? Label { get; set; }
}

public sealed class RetentionSettings
{
    public int MetricsDays { get; set; } = 14;
    public int WaitsDays { get; set; } = 30;
    public int FileIoDays { get; set; } = 14;
    public int VolumesDays { get; set; } = 90;
    public int DatabaseSizeDays { get; set; } = 400;
    public int BackupsDays { get; set; } = 90;
    public int CollectionRunsDays { get; set; } = 7;
}
