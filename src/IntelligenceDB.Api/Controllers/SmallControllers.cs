using IntelligenceDB.Application.Common;
using IntelligenceDB.Application.Dashboard;
using IntelligenceDB.Application.Groups;
using IntelligenceDB.Application.Search;
using IntelligenceDB.Application.Servers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IntelligenceDB.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(DashboardService dashboard, ServerService servers) : ControllerBase
{
    [HttpGet]
    public Task<DashboardDto> Get(CancellationToken ct) => dashboard.GetAsync(ct);

    /// <summary>Re-tests every visible server now (parallel, with a concurrency limit) and returns the new dashboard.</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("refresh")]
    public async Task<DashboardDto> Refresh(CancellationToken ct)
    {
        await servers.RefreshAllAsync(ct);
        return await dashboard.GetAsync(ct);
    }
}

[ApiController]
[Route("api/server-groups")]
public sealed class ServerGroupsController(ServerGroupService groups) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ServerGroupDto>> List(CancellationToken ct) => groups.ListAsync(ct);

    [HttpPost]
    [Authorize(Policy = Policies.ManageServers)]
    public Task<ServerGroupDto> Create(ServerGroupRequest request, CancellationToken ct) => groups.CreateAsync(request, ct);

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.ManageServers)]
    public Task<ServerGroupDto> Update(int id, ServerGroupRequest request, CancellationToken ct) => groups.UpdateAsync(id, request, ct);

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.ManageServers)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await groups.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/search")]
public sealed class SearchController(SearchService search) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SearchHitDto>> Search([FromQuery] string? q, CancellationToken ct) => search.SearchAsync(q, ct);
}
