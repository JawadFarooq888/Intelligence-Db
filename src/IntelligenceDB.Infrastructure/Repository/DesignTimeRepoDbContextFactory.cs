using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IntelligenceDB.Infrastructure.Repository;

/// <summary>
/// Used only by "dotnet ef" (migrations add / script). Creating a migration does not connect to a database.
/// Set IDB_REPO_CONNECTION to use "dotnet ef database update" against a specific server.
/// </summary>
public sealed class DesignTimeRepoDbContextFactory : IDesignTimeDbContextFactory<RepoDbContext>
{
    public RepoDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("IDB_REPO_CONNECTION")
            ?? "Server=localhost;Database=IntelligenceDB_Repo;Integrated Security=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<RepoDbContext>()
            .UseSqlServer(connectionString, RepoDbOptions.ConfigureSqlServer)
            .Options;

        return new RepoDbContext(options);
    }
}
