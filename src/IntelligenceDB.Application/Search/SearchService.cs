using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Servers;
using Microsoft.EntityFrameworkCore;

namespace IntelligenceDB.Application.Search;

/// <param name="Kind">server | database | group (jobs and indexes are added in later phases).</param>
public sealed record SearchHitDto(string Kind, int Id, string Title, string? Subtitle, string Url);

/// <summary>Global search box. Uses LIKE with parameters; wildcard characters typed by the user are escaped.</summary>
public sealed class SearchService(IRepoDbContext db, ICurrentUser user)
{
    private const int MaxPerKind = 10;

    public async Task<IReadOnlyList<SearchHitDto>> SearchAsync(string? term, CancellationToken ct)
    {
        term = term?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length < 2) return [];
        if (term.Length > 100) term = term[..100];

        var pattern = "%" + EscapeLike(term) + "%";
        var scope = await user.GetScopeAsync(ct);

        var servers = await db.Servers.AsNoTracking()
            .VisibleTo(scope)
            .Where(s => EF.Functions.Like(s.DisplayName, pattern, "\\")
                        || EF.Functions.Like(s.Host, pattern, "\\")
                        || s.Tags.Any(t => EF.Functions.Like(t.Tag, pattern, "\\")))
            .OrderBy(s => s.DisplayName)
            .Take(MaxPerKind)
            .Select(s => new { s.Id, s.DisplayName, s.Host })
            .ToListAsync(ct);

        var allowed = scope.GroupIds.ToList();
        var groups = await db.ServerGroups.AsNoTracking()
            .Where(g => scope.AllGroups || allowed.Contains(g.Id))
            .Where(g => EF.Functions.Like(g.Name, pattern, "\\"))
            .OrderBy(g => g.Name)
            .Take(MaxPerKind)
            .Select(g => new { g.Id, g.Name, Count = g.Servers.Count })
            .ToListAsync(ct);

        var serverIds = await db.Servers.AsNoTracking().VisibleTo(scope).Select(s => s.Id).ToListAsync(ct);
        var databases = await db.Databases.AsNoTracking()
            .Where(d => !d.IsDropped && serverIds.Contains(d.ServerId) && EF.Functions.Like(d.Name, pattern, "\\"))
            .OrderBy(d => d.Name)
            .Take(MaxPerKind)
            .Join(db.Servers, d => d.ServerId, s => s.Id, (d, s) => new { d.Id, d.Name, Server = s.DisplayName })
            .ToListAsync(ct);

        return servers.Select(s => new SearchHitDto("server", s.Id, s.DisplayName, s.Host, $"/servers/{s.Id}"))
            .Concat(databases.Select(d => new SearchHitDto("database", d.Id, d.Name, d.Server, $"/databases/{d.Id}")))
            .Concat(groups.Select(g => new SearchHitDto("group", g.Id, g.Name, $"{g.Count} server(s)", $"/servers?group={g.Id}")))
            .ToList();
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal)
             .Replace("[", "\\[", StringComparison.Ordinal);
}
