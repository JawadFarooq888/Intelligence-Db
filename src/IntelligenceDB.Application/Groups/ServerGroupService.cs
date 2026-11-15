using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Common;
using IntelligenceDB.Domain.Servers;
using Microsoft.EntityFrameworkCore;

namespace IntelligenceDB.Application.Groups;

public sealed record ServerGroupDto(int Id, string Name, string? Description, int ServerCount);

public sealed class ServerGroupRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public sealed class ServerGroupService(IRepoDbContext db, ICurrentUser user, IAuditWriter audit)
{
    public async Task<IReadOnlyList<ServerGroupDto>> ListAsync(CancellationToken ct)
    {
        var scope = await user.GetScopeAsync(ct);
        var allowed = scope.GroupIds.ToList();
        return await db.ServerGroups.AsNoTracking()
            .Where(g => scope.AllGroups || allowed.Contains(g.Id))
            .OrderBy(g => g.Name)
            .Select(g => new ServerGroupDto(g.Id, g.Name, g.Description, g.Servers.Count))
            .ToListAsync(ct);
    }

    public async Task<ServerGroupDto> CreateAsync(ServerGroupRequest request, CancellationToken ct)
    {
        var name = Validate(request);
        var scope = await user.GetScopeAsync(ct);
        if (!scope.AllGroups)
        {
            throw new ForbiddenException("Only users with access to all server groups can create groups.");
        }

        await EnsureUniqueAsync(name, null, ct);
        var group = new ServerGroup { Name = name, Description = Clean(request.Description) };
        db.ServerGroups.Add(group);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditRecord("ServerGroup.Create", Details: new { group.Id, group.Name }), ct);
        return new ServerGroupDto(group.Id, group.Name, group.Description, 0);
    }

    public async Task<ServerGroupDto> UpdateAsync(int id, ServerGroupRequest request, CancellationToken ct)
    {
        var name = Validate(request);
        var group = await FindVisibleAsync(id, ct);
        await EnsureUniqueAsync(name, id, ct);
        group.Name = name;
        group.Description = Clean(request.Description);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditRecord("ServerGroup.Update", Details: new { group.Id, group.Name }), ct);
        var count = await db.Servers.CountAsync(s => s.GroupId == id, ct);
        return new ServerGroupDto(group.Id, group.Name, group.Description, count);
    }

    /// <summary>Deletes a group. Its servers are kept and become "ungrouped".</summary>
    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var scope = await user.GetScopeAsync(ct);
        if (!scope.AllGroups)
        {
            throw new ForbiddenException("Only users with access to all server groups can delete groups.");
        }

        var group = await FindVisibleAsync(id, ct);
        db.ServerGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditRecord("ServerGroup.Delete", Details: new { group.Id, group.Name }), ct);
    }

    private async Task<ServerGroup> FindVisibleAsync(int id, CancellationToken ct)
    {
        var scope = await user.GetScopeAsync(ct);
        var group = await db.ServerGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null || !scope.CanSee(group.Id))
        {
            throw new NotFoundException($"Server group {id} was not found.");
        }

        return group;
    }

    private async Task EnsureUniqueAsync(string name, int? exceptId, CancellationToken ct)
    {
        if (await db.ServerGroups.AnyAsync(g => g.Name == name && (exceptId == null || g.Id != exceptId), ct))
        {
            throw new ConflictException($"A group named '{name}' already exists.");
        }
    }

    private static string Validate(ServerGroupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var e = new ValidationErrors();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) e.Add("name", "Group name is required.");
        if (name.Length > 100) e.Add("name", "Group name must be 100 characters or fewer.");
        if (request.Description is { Length: > 500 }) e.Add("description", "Description must be 500 characters or fewer.");
        e.ThrowIfAny();
        return name;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
