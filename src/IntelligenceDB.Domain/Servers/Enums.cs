namespace IntelligenceDB.Domain.Servers;

/// <summary>Where a SQL Server instance runs. Drives which features are available.</summary>
public enum PlatformType
{
    Unknown = 0,
    OnPremises = 1,
    AzureSqlManagedInstance = 2,
    AzureSqlDatabase = 3,
    AwsRds = 4,
    GoogleCloudSql = 5,
}

/// <summary>How IntelligenceDB authenticates to a monitored server.</summary>
public enum AuthType
{
    SqlLogin = 1,
    Windows = 2,
    EntraPassword = 3,
    EntraServicePrincipal = 4,
    EntraManagedIdentity = 5,
    EntraDefault = 6,
}

public enum CredentialPurpose
{
    /// <summary>Read-only login used by the collector.</summary>
    Monitoring = 1,

    /// <summary>Optional login used only for approved control actions.</summary>
    Admin = 2,
}

public enum ServerEnvironment
{
    Production = 1,
    Test = 2,
    Development = 3,
    Other = 4,
}

public enum ConnectionStatus
{
    Unknown = 0,
    Online = 1,
    Unreachable = 2,
    AuthFailed = 3,
}
