using IntelligenceDB.Application.Common;
using IntelligenceDB.Infrastructure.Identity;
using IntelligenceDB.Infrastructure.Repository;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IntelligenceDB.Api.Infrastructure;

/// <summary>Applies EF migrations, creates the four roles, and creates the first admin user.</summary>
public static class RepositoryInitializer
{
    public static async Task InitializeRepositoryAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("RepositoryInitializer");
        var config = services.GetRequiredService<IConfiguration>();

        if (config.GetValue("Repository:AutoMigrate", true))
        {
            await MigrateWithRetryAsync(services.GetRequiredService<RepoDbContext>(), logger, app.Lifetime.ApplicationStopping);
        }

        var roleManager = services.GetRequiredService<RoleManager<AppRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new AppRole(role));
            }
        }

        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        if (await userManager.Users.AnyAsync())
        {
            return;
        }

        var email = config["Bootstrap:AdminEmail"];
        var password = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "No portal users exist yet. Set Bootstrap__AdminEmail and Bootstrap__AdminPassword and restart the API to create the first admin.");
            return;
        }

        var admin = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Administrator",
            CreatedUtc = DateTime.UtcNow,
            AllServerGroups = true,
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            logger.LogError("Could not create the first admin user: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Created the first admin user {Email}. Change the password after the first login.", email);
    }

    /// <summary>The repository SQL Server may still be starting (Docker); retry for about a minute.</summary>
    private static async Task MigrateWithRetryAsync(RepoDbContext db, ILogger logger, CancellationToken ct)
    {
        const int maxAttempts = 12;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                logger.LogInformation("Repository database is up to date");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransient(ex))
            {
                logger.LogWarning("Repository database not reachable yet (attempt {Attempt}/{Max}): {Message}", attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is SqlException || ex.InnerException is SqlException || ex is Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException;
}
