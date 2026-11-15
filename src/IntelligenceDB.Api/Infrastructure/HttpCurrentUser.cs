using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Common;
using IntelligenceDB.Infrastructure.Repository;
using Microsoft.EntityFrameworkCore;

namespace IntelligenceDB.Api.Infrastructure;

/// <summary>Current user from the JWT, plus their server-group scope (loaded once per request).</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor, RepoDbContext db) : ICurrentUser
{
    private AccessScope? _scope;

    private System.Security.Claims.ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirst("sub")?.Value;

    public string? UserName => Principal?.FindFirst("email")?.Value ?? Principal?.FindFirst("name")?.Value;

    public string? ClientIp => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    public async Task<AccessScope> GetScopeAsync(CancellationToken cancellationToken)
    {
        if (_scope is not null) return _scope;

        if (!Guid.TryParse(UserId, out var userId))
        {
            return _scope = new AccessScope(false, new HashSet<int>());
        }

        if (IsInRole(Roles.Admin))
        {
            return _scope = AccessScope.Everything;
        }

        var allGroups = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (bool?)u.AllServerGroups)
            .FirstOrDefaultAsync(cancellationToken);

        if (allGroups == true)
        {
            return _scope = AccessScope.Everything;
        }

        var groups = await db.UserServerGroups.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.ServerGroupId)
            .ToListAsync(cancellationToken);

        return _scope = new AccessScope(false, groups.ToHashSet());
    }
}
