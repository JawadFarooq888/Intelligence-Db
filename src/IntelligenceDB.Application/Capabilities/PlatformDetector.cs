using IntelligenceDB.Domain.Servers;

namespace IntelligenceDB.Application.Capabilities;

/// <summary>Works out which platform a server runs on from the probe facts.</summary>
public static class PlatformDetector
{
    // SERVERPROPERTY('EngineEdition') values (see Microsoft docs):
    // 1 Personal/Desktop, 2 Standard, 3 Enterprise/Developer/Evaluation, 4 Express,
    // 5 Azure SQL Database, 6 Azure Synapse, 8 Azure SQL Managed Instance, 9 Azure SQL Edge, 11 Synapse serverless.
    private const int AzureSqlDatabaseEdition = 5;
    private const int ManagedInstanceEdition = 8;

    public static PlatformType Detect(ProbeFacts facts, PlatformType declared)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.EngineEdition == AzureSqlDatabaseEdition) return PlatformType.AzureSqlDatabase;
        if (facts.EngineEdition == ManagedInstanceEdition) return PlatformType.AzureSqlManagedInstance;

        // Box product (engine editions 1-4). Cloud vendors that host the box product leave markers.
        if (facts.HasRdsAdminDatabase) return PlatformType.AwsRds;
        if (facts.HasCloudSqlMarker) return PlatformType.GoogleCloudSql;

        if (facts.EngineEdition is >= 1 and <= 4)
        {
            // Markers can be hidden from a low-privilege login. If the user said RDS / Cloud SQL,
            // trust that rather than wrongly claiming "on-premises" (which would enable features the
            // vendor blocks, e.g. sp_configure).
            return declared is PlatformType.AwsRds or PlatformType.GoogleCloudSql
                ? declared
                : PlatformType.OnPremises;
        }

        // Synapse, Edge, Fabric, ... are not supported targets.
        return PlatformType.Unknown;
    }

    public static string? MismatchWarning(PlatformType declared, PlatformType detected)
    {
        if (declared == PlatformType.Unknown || detected == PlatformType.Unknown || declared == detected)
        {
            return null;
        }

        return $"You selected '{PlatformNames.Get(declared)}' but the server reports '{PlatformNames.Get(detected)}'. " +
               "IntelligenceDB uses the detected platform.";
    }
}

public static class PlatformNames
{
    public static string Get(PlatformType platform) => platform switch
    {
        PlatformType.OnPremises => "SQL Server (on-premises / VM)",
        PlatformType.AzureSqlManagedInstance => "Azure SQL Managed Instance",
        PlatformType.AzureSqlDatabase => "Azure SQL Database",
        PlatformType.AwsRds => "Amazon RDS for SQL Server",
        PlatformType.GoogleCloudSql => "Google Cloud SQL for SQL Server",
        _ => "Unknown",
    };
}
