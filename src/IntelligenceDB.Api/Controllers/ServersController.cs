using IntelligenceDB.Application.Common;
using IntelligenceDB.Application.Servers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IntelligenceDB.Api.Controllers;

[ApiController]
[Route("api/servers")]
public sealed class ServersController(ServerService servers) : ControllerBase
{
    private static readonly HashSet<string> ScriptKinds = new(StringComparer.Ordinal) { "monitoring", "admin" };

    private static readonly HashSet<string> ScriptPlatforms = new(StringComparer.Ordinal)
    {
        "onprem", "azure-mi", "azure-sqldb", "aws-rds", "gcp-cloudsql",
    };

    [HttpGet]
    public Task<IReadOnlyList<ServerSummaryDto>> List(CancellationToken ct) => servers.ListAsync(ct);

    [HttpGet("{id:int}")]
    public Task<ServerDetailDto> Get(int id, CancellationToken ct) => servers.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = Policies.ManageServers)]
    public async Task<ActionResult<ServerDetailDto>> Create(ServerUpsertRequest request, CancellationToken ct)
    {
        var created = await servers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Server.Id }, created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.ManageServers)]
    public Task<ServerDetailDto> Update(int id, ServerUpsertRequest request, CancellationToken ct) =>
        servers.UpdateAsync(id, request, ct);

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.ManageServers)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await servers.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Re-tests a saved server (read-only against the target) and stores the result.</summary>
    [HttpPost("{id:int}/test")]
    public Task<TestConnectionResultDto> Test(int id, CancellationToken ct) => servers.TestAsync(id, ct);

    /// <summary>Tests form input without saving. When editing, pass existingId so stored secrets can be reused.</summary>
    [HttpPost("test")]
    [Authorize(Policy = Policies.ManageServers)]
    public Task<TestConnectionResultDto> TestUnsaved(ServerUpsertRequest request, [FromQuery] int? existingId, CancellationToken ct) =>
        servers.TestUnsavedAsync(request, existingId, ct);

    /// <summary>Returns a ready-made T-SQL script to create the monitoring or admin login for a platform.</summary>
    [HttpGet("login-scripts/{kind}/{platform}")]
    [Produces("text/plain")]
    public async Task<IActionResult> LoginScript(string kind, string platform)
    {
        if (!ScriptKinds.Contains(kind) || !ScriptPlatforms.Contains(platform))
        {
            return NotFound();
        }

        // Only whitelisted names reach this point, so the resource name cannot be manipulated.
        var resourceName = $"{kind}-login/{platform}.sql";
        await using var stream = typeof(ServersController).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return NotFound();

        using var reader = new StreamReader(stream);
        return Content(await reader.ReadToEndAsync(), "text/plain; charset=utf-8");
    }
}
