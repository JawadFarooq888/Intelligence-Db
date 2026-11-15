namespace IntelligenceDB.Application.Servers;

/// <summary>Turns raw SERVERPROPERTY values into a friendly label such as "SQL Server 2022 RTM CU14".</summary>
public static class VersionNames
{
    public static string Describe(int engineEdition, int majorVersion, string? productLevel, string? productUpdateLevel)
    {
        if (engineEdition == 5) return "Azure SQL Database";
        if (engineEdition == 8) return "Azure SQL Managed Instance";

        string name = majorVersion switch
        {
            13 => "SQL Server 2016",
            14 => "SQL Server 2017",
            15 => "SQL Server 2019",
            16 => "SQL Server 2022",
            17 => "SQL Server 2025",
            _ => $"SQL Server (version {majorVersion})",
        };

        var parts = new List<string> { name };
        if (!string.IsNullOrWhiteSpace(productLevel)) parts.Add(productLevel);
        if (!string.IsNullOrWhiteSpace(productUpdateLevel)) parts.Add(productUpdateLevel);
        return string.Join(' ', parts);
    }

    /// <summary>Reads the major version from a string such as "16.0.4135.4".</summary>
    public static int ParseMajor(string? productVersion)
    {
        if (string.IsNullOrWhiteSpace(productVersion)) return 0;
        int dot = productVersion.IndexOf('.', StringComparison.Ordinal);
        var head = dot < 0 ? productVersion : productVersion[..dot];
        return int.TryParse(head, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var major)
            ? major
            : 0;
    }
}
