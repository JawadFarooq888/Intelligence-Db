using IntelligenceDB.Agent;
using IntelligenceDB.Targets;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Runs as the Windows service "IntelligenceDB Agent" when installed with install-agent.ps1;
// can also run in a console (or on Linux) for testing.
builder.Services.AddWindowsService(o => o.ServiceName = "IntelligenceDB Agent");

// Settings are validated in AgentWorker so a mistake produces one clear log line instead of a crash.
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));

builder.Services.AddTargets();
builder.Services.AddHttpClient<PortalClient>((sp, http) =>
{
    var o = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    http.BaseAddress = Uri.TryCreate(o.PortalUrl.TrimEnd('/') + "/", UriKind.Absolute, out var portal)
        ? portal
        : new Uri("https://portal-not-configured.invalid/");
    http.Timeout = TimeSpan.FromSeconds(60);
    http.DefaultRequestHeaders.TryAddWithoutValidation(PortalClient.AgentIdHeader, o.AgentId);
    http.DefaultRequestHeaders.TryAddWithoutValidation(PortalClient.AgentKeyHeader, o.AgentKey);
});
builder.Services.AddHostedService<AgentWorker>();

await builder.Build().RunAsync();
