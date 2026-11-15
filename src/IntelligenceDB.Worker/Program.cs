using IntelligenceDB.Application;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Infrastructure;
using IntelligenceDB.Infrastructure.Security;
using IntelligenceDB.Worker.Jobs;
using Quartz;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
var config = builder.Configuration;

builder.Services.AddSerilog((_, lc) => lc
    .ReadFrom.Configuration(config)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(config["Logging:FilePath"] ?? "logs/worker-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

// Runs as a Windows service when installed as one; no effect when started from a console or in Docker.
builder.Services.AddWindowsService(o => o.ServiceName = "IntelligenceDB Worker");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);
builder.Services.AddSingleton<ICurrentUser>(new SystemCurrentUser("system:worker"));

builder.Services.AddQuartz(q =>
{
    // Collection tick: every 15 s the engine checks which collectors are due (each has its own interval).
    var collect = new JobKey("collect");
    q.AddJob<CollectionJob>(collect);
    q.AddTrigger(t => t.ForJob(collect).WithIdentity("collect-every-15s")
        .StartAt(DateBuilder.FutureDate(5, IntervalUnit.Second))
        .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromSeconds(15)).RepeatForever()));

    // Retention: hourly clean-up of old history.
    var retention = new JobKey("retention");
    q.AddJob<RetentionJob>(retention);
    q.AddTrigger(t => t.ForJob(retention).WithIdentity("retention-hourly")
        .StartAt(DateBuilder.FutureDate(2, IntervalUnit.Minute))
        .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever()));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

await builder.Build().RunAsync();
