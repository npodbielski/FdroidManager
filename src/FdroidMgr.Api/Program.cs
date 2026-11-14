using FdroidMgr.Api.Endpoints;
using FdroidMgr.Api.Endpoints.Apps;
using FdroidMgr.Api.Endpoints.Auth;
using FdroidMgr.Api.Endpoints.FdroidClientKey;
using FdroidMgr.Api.Endpoints.Keys;
using FdroidMgr.Api.Endpoints.Releases;
using FdroidMgr.Api.Endpoints.Repo;
using FdroidMgr.Api.Endpoints.Stats;
using FdroidMgr.Api;
using FluentValidation;
using FdroidMgr.Api.Data;
using FdroidMgr.Api.Models;
using FdroidMgr.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using HamsterWheel.HLinq.AspNet;
using Microsoft.EntityFrameworkCore;
using FdroidMgr.Api.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.AddOidcConfigFile();

var environment = new EnvironmentService();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? environment.GetEnvironmentVariable("FDROID_DB")
    ?? "Data Source=fdroidmgr.db";

builder.Services.AddDbContext<FdroidDb>(o => o.UseSqlite(connectionString));

var dataProtectionDir = builder.Configuration["FDROID_DATAPROTECTION_PATH"]
    ?? environment.GetEnvironmentVariable("FDROID_DATAPROTECTION_PATH");
if (!string.IsNullOrWhiteSpace(dataProtectionDir))
{
    Directory.CreateDirectory(dataProtectionDir);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionDir))
        .SetApplicationName("fdroidmgr");
}

var oauthIssuer = builder.Configuration["Auth:OAuthIssuer"];
var oauthClientId = builder.Configuration["Auth:OAuthClientId"];
var oauthConfigured = !string.IsNullOrWhiteSpace(oauthIssuer)
                      && !string.IsNullOrWhiteSpace(oauthClientId);
if (!string.IsNullOrWhiteSpace(oauthIssuer) && !oauthConfigured)
{
    Console.Error.WriteLine("WARN: Auth:OAuthIssuer is set but Auth:OAuthClientId is missing — OIDC sign-in disabled");
}

var auth = builder.Services
    .AddAuthentication(o =>
    {

        o.DefaultScheme = "SmartCookieOrApiKey";
        o.DefaultAuthenticateScheme = "SmartCookieOrApiKey";
        o.DefaultChallengeScheme = "SmartCookieOrApiKey";
        o.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(o =>
    {
        o.Cookie.Name = ".FdroidMgr.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.LoginPath = "/api/auth/login";

        o.Events.OnRedirectToLogin = ctx =>
        {
            if (IsJsonClientPath(ctx.Request.Path))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsync("""{"error":"unauthorized"}""");
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (IsJsonClientPath(ctx.Request.Path))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsync("""{"error":"forbidden"}""");
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    })
    .AddPolicyScheme("SmartCookieOrApiKey", "Cookie or API key", o =>
        o.ForwardDefaultSelector = SmartScheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });

var publicBaseUrl = builder.Configuration["FDROID_PUBLIC_URL"]?.TrimEnd('/');

auth.AddOidcProvider(publicBaseUrl);

var requireRepoAuth = bool.TryParse(
    builder.Configuration["FDROID_REQUIRE_REPO_AUTH"]
    ?? environment.GetEnvironmentVariable("FDROID_REQUIRE_REPO_AUTH"), out var repoAuthFlag)
    && repoAuthFlag;

builder.Services.AddAuthorization(options =>
{

    options.AddPolicy(Scopes.Read, p => p.RequireClaim(Scopes.ClaimType, Scopes.Read));
    options.AddPolicy(Scopes.AppsWrite, p => p.RequireClaim(Scopes.ClaimType, Scopes.AppsWrite));
    options.AddPolicy(Scopes.ReleasesWrite, p => p.RequireClaim(Scopes.ClaimType, Scopes.ReleasesWrite));

    options.AddPolicy("InteractiveOnly", p => p.RequireAssertion(ctx =>
        ctx.User.Identity is { IsAuthenticated: true } &&
        !string.Equals(ctx.User.Identity.AuthenticationType, ApiKeyAuthenticationHandler.SchemeName, StringComparison.Ordinal)));

    options.AddPolicy("repo:read", p => p.RequireAssertion(ctx =>
        !requireRepoAuth || ctx.User.Identity is { IsAuthenticated: true }));
});

builder.Services.ConfigureHLinq();

builder.Services.AddApiProblemDetails();

builder.Services.AddOpenApi();

builder.Services.AddScoped<IApkInspector, ApkService>();
builder.Services.AddScoped<IKeyService, KeyService>();
builder.Services.AddScoped<IReleaseService, ReleaseService>();
builder.Services.AddSingleton<IRepoVisibility, RepoVisibility>();
builder.Services.AddScoped<IRepoStageService, RepoStageService>();
builder.Services.AddScoped<IRepoIndexService, RepoIndexService>();
builder.Services.AddScoped<IFdroidMetadataService, FdroidMetadataService>();
builder.Services.AddScoped<IRepoIndexSnapshot, RepoIndexSnapshot>();
builder.Services.AddScoped<IFdroidRepoService, FdroidRepoService>();
builder.Services.AddScoped<IRepoInitService, RepoInitService>();
builder.Services.AddSingleton<IDownloadStatsIndexCache, DownloadStatsIndexCache>();
builder.Services.AddSingleton<IDownloadStatsQueue, DownloadStatsQueue>();
builder.Services.AddScoped<IDownloadStatsService, DownloadStatsService>();
builder.Services.AddSingleton<IRepoCommandRunner, ProcessCommandRunner>();
builder.Services.AddSingleton<IRepoLock, RepoLock>();
builder.Services.AddSingleton<IOService, RealIoService>();
builder.Services.AddSingleton<IEnvironmentService, EnvironmentService>();
builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IFdroidRepository, FdroidRepository>();
builder.Services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
builder.Services.AddScoped<IPasswordProtector, PasswordProtector>();
builder.Services.AddSingleton<BootState>();
builder.Services.AddHostedService<BootInitializer>();
builder.Services.AddHostedService<DownloadStatsWriter>();
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
builder.Services.AddSingleton<IOAuthConfigService, OAuthConfigService>();
builder.Services.AddSingleton<ISignInDiagnostics, SignInDiagnostics>();
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
builder.Services.AddScoped<IFdroidClientKeyService, FdroidClientKeyService>();
builder.Services.AddScoped<IClaimsTransformation, ScopeClaimsTransformer>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FdroidDb>();
    var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();

    string Resolve(string key, string fallback)
        => builder.Configuration[key] ?? environment.GetEnvironmentVariable(key) ?? fallback;

    await initializer.InitializeAsync(db, Resolve, environment);

    string? EnvOrConfig(string key)
        => builder.Configuration[key] ?? environment.GetEnvironmentVariable(key);

    await initializer.PinPathsAsync(db, EnvOrConfig);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{

    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
    },
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthApi();
app.MapAppsApi();
app.MapKeysApi();
app.MapReleasesApi();
app.MapApiKeysApi();
app.MapFdroidClientKeyApi();
app.MapStatsApi();
app.MapRepoApi();
app.MapGet("/healthz", (BootState boot) => Results.Ok(new HealthzResponse("ok", boot.BootCompleted)))
    .Produces<HealthzResponse>();
app.MapGet("/", () => Results.Redirect("/ui"));
app.MapGet("/ui", () => Results.Redirect("/ui.html"));

app.Run();

static string SmartScheme(HttpContext ctx)
{
    var authorization = ctx.Request.Headers.Authorization.ToString();
    if (ctx.Request.Headers.ContainsKey("X-API-Key")
        || ctx.Request.Query.ContainsKey("key")
        || authorization.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase)
        || authorization.StartsWith("Basic", StringComparison.OrdinalIgnoreCase))
    {
        return ApiKeyAuthenticationHandler.SchemeName;
    }

    return CookieAuthenticationDefaults.AuthenticationScheme;
}

static bool IsJsonClientPath(PathString path)
    => path.StartsWithSegments("/api") || path.StartsWithSegments("/fdroid");

public partial class Program;
