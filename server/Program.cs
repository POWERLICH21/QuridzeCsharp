using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Subfuse.Server.Api;
using Subfuse.Server.Data;
using Subfuse.Server.Import;
using Subfuse.Server.Security;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ----- Data -----
builder.Services.AddDbContextPool<AppDb>((services, o) =>
    o.UseSqlite(services.GetRequiredService<IConfiguration>().GetConnectionString("Default")));

// ----- Accounts and sessions -----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<AccountSecurity>();

// ----- Importing subscriptions from receipts -----
builder.Services.AddSingleton(Catalog.Load(Path.Combine(builder.Environment.WebRootPath, "catalog.json")));
builder.Services.AddSingleton<ReceiptReader>();
// Forwarded-email import stays off until a mail provider is connected and both values are set.
builder.Services.AddSingleton(new InboundSettings(config["Inbound:Domain"], config["Inbound:Secret"]));

// These keys encrypt session cookies and authenticator secrets. They must survive restarts and be backed up:
// losing them signs everyone out and breaks authenticator codes (recovery codes keep working).
builder.Services.AddDataProtection()
    .SetApplicationName("Subfuse")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, config["DataProtection:KeysPath"] ?? "keys")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "subfuse.session";
    o.Cookie.HttpOnly = true;
    // Strict: the cookie is never sent on requests started by another site, which rules out CSRF.
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
    o.Events.OnValidatePrincipal = AccountSecurity.ValidateSession;
    // This is an API: answer with status codes instead of redirecting to a login page.
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();

// Slows down password and code guessing: sign-in and other sensitive endpoints are limited per client address.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var perMinute = config.GetValue("RateLimits:AuthPerMinute", 10);
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));
    // All inbound mail arrives from the provider's few addresses, so this one is a single shared budget.
    o.AddPolicy("inbound", _ => RateLimitPartition.GetFixedWindowLimiter("inbound",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimits:InboundPerMinute", 600), Window = TimeSpan.FromMinutes(1) }));
});

// ----- Hosting behind a proxy (Cloudflare, a platform's load balancer) -----
// Off by default. Turn on only when the app is NOT reachable directly from the internet, and set ForwardLimit
// to the number of proxies in front of it; otherwise visitors could fake their address and dodge rate limits.
var behindProxy = config.GetValue("Proxy:TrustForwardedHeaders", false);
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = config.GetValue("Proxy:ForwardLimit", 1);
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

builder.Services.AddResponseCompression(o => o.EnableForHttps = true);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.Migrate();
    // Write-ahead logging lets people read while someone else is saving.
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

if (behindProxy) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) app.UseHsts();

// Browser-side protections, sent with every response. The page loads nothing from other sites, so the
// content policy can forbid everything that is not ours; that blunts script injection.
app.Use((http, next) =>
{
    var h = http.Response.Headers;
    h.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "same-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (http.Request.Path.StartsWithSegments("/api")) h.CacheControl = "no-store";
    return next();
});

app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // Browsers keep the files but ask "has it changed?" each visit: fast repeat loads, never a stale page.
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuth();
app.MapAccount();
app.MapSubscriptions();
app.MapImport();
app.MapGet("/healthz", () => Results.Text("ok"));

app.Run();

public partial class Program; // lets the test project start the app
