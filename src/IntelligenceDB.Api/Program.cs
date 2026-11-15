using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using IntelligenceDB.Api.Auth;
using IntelligenceDB.Api.Infrastructure;
using IntelligenceDB.Application;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Common;
using IntelligenceDB.Infrastructure;
using IntelligenceDB.Infrastructure.Identity;
using IntelligenceDB.Infrastructure.Repository;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Logging: console + rolling file + repository table (log.AppLog) ----------
builder.Services.AddSerilog((services, lc) =>
{
    lc.ReadFrom.Configuration(config)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(config["Logging:FilePath"] ?? "logs/api-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);

    var repo = config.GetConnectionString("Repository");
    if (!string.IsNullOrWhiteSpace(repo) && config.GetValue("Logging:WriteToRepository", true))
    {
        var columns = new ColumnOptions();
        columns.TimeStamp.ConvertToUtc = true;
        lc.WriteTo.MSSqlServer(
            connectionString: repo,
            sinkOptions: new MSSqlServerSinkOptions { SchemaName = "log", TableName = "AppLog", AutoCreateSqlTable = false },
            restrictedToMinimumLevel: LogEventLevel.Information,
            columnOptions: columns);
    }
});

// ---------- Application + infrastructure ----------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// ---------- Identity (portal users) ----------
builder.Services
    .AddIdentityCore<AppUser>(o =>
    {
        o.Password.RequiredLength = 12;
        o.Password.RequireDigit = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireNonAlphanumeric = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
        o.User.RequireUniqueEmail = true;
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<RepoDbContext>()
    .AddDefaultTokenProviders();

// ---------- JWT authentication ----------
var jwtSection = config.GetSection("Jwt");
builder.Services.Configure<JwtOptions>(jwtSection);
var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
var signingKey = JwtSigningKey.Create(jwtOptions.SigningKey, builder.Environment.IsDevelopment());
builder.Services.AddSingleton(signingKey);
builder.Services.AddScoped<TokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey.Key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role",
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.ManageServers, p => p.RequireRole(Roles.Admin, Roles.Dba))
    .AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin))
    // Everything requires a signed-in user unless marked [AllowAnonymous].
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ---------- Rate limiting ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Sign-in endpoints: 10 per minute per IP (brute-force protection).
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    // "Refresh all servers": 4 per minute per user.
    o.AddPolicy("refresh", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 4, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    // Everything else: 600 per minute per user / IP.
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// ---------- CORS (only needed when the SPA is served from another origin) ----------
var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddPolicy("web", p => p
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
}

builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

builder.Services
    // Validation is explicit ([Required] / ServerValidator); do not treat every non-nullable
    // property of agent payloads (records) as required.
    .AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (signingKey.IsEphemeral)
{
    app.Logger.LogWarning("Jwt:SigningKey is not set; using a random development key. Users are signed out when the API restarts.");
}

await app.InitializeRepositoryAsync();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Basic security headers on every response.
bool isDev = app.Environment.IsDevelopment();
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (!isDev)
    {
        h["Content-Security-Policy"] =
            "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'";
    }

    await next();
});

// In production the built React app is copied to wwwroot and served by the API.
app.UseDefaultFiles();
app.UseStaticFiles();

if (allowedOrigins.Length > 0) app.UseCors("web");

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html")))
{
    // Client-side routes (e.g. /servers/5) fall back to the SPA; unknown /api routes still return 404.
    app.MapFallbackToFile("index.html").AllowAnonymous();
    app.MapFallback("/api/{**path}", () => Results.NotFound()).AllowAnonymous();
}

await app.RunAsync();

/// <summary>Exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program
{
}
