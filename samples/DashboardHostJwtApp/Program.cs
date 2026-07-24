using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities.Base;

// Host-authentication test app with REAL database users:
//   ASP.NET Core Identity (UserManager + roles, SQLite store)
//     → normal login form validates credentials against the DB
//     → app issues its own JWT with the user's Identity roles as claims
//     → dashboard delegates to this pipeline via WithHostAuthentication()
//
// Seeded users (created on first run in dashboard-hostjwt-identity.db):
//   admin  / Admin123$   → role "ops"  → FULL dashboard access
//   viewer / Viewer123$  → no role     → READ-ONLY dashboard (can browse
//                          everything; mutation UI hidden, writes 403)
//
// Test flow (browser):
//   1. http://localhost:5212/account/login  → login form → dashboard
//   2. http://localhost:5212/logout         → back to the login form
//
// Test flow (API client):
//   TOKEN=$(curl -s -X POST localhost:5212/auth/token -H 'Content-Type: application/json' \
//           -d '{"username":"admin","password":"Admin123$"}' | jq -r .accessToken)
//   curl -H "Authorization: Bearer $TOKEN" localhost:5212/tickerq/dashboard/api/dashboard/host/status

var builder = WebApplication.CreateBuilder(args);

// Dev-only symmetric key. Real setups get keys from the identity provider.
var signingKey = new SymmetricSecurityKey(
    Encoding.UTF8.GetBytes("dashboard-hostjwt-dev-signing-key-32b!"));
const string Issuer = "host-app";
const string Audience = "host-app-clients";

// ── Identity: users + roles live in SQLite ──
builder.Services.AddDbContext<AppIdentityDbContext>(o =>
    o.UseSqlite("Data Source=dashboard-hostjwt-identity.db"));

// AddIdentityCore (not AddIdentity) on purpose: it registers UserManager /
// RoleManager without hijacking the authentication schemes — JWT Bearer
// stays the app's only scheme.
builder.Services
    .AddIdentityCore<IdentityUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppIdentityDbContext>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // Browsers cannot attach an Authorization header to page navigations
        // or WebSocket upgrades. Accept the token from an HTTP-only cookie
        // (set by the login form below) and from ?access_token — the standard
        // bridges between JWT auth and a browser-rendered dashboard.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (string.IsNullOrEmpty(ctx.Token))
                {
                    ctx.Token = ctx.Request.Cookies["host_jwt"]
                                ?? ctx.Request.Query["access_token"].FirstOrDefault();
                }
                return Task.CompletedTask;
            },
        };
    });

// Entry rule: any authenticated user may OPEN the dashboard. What they can
// DO inside is decided per-user by SetReadOnly(...) below.
builder.Services.AddAuthorization(options =>
    options.AddPolicy("DashboardAccess", p => p
        .RequireAuthenticatedUser()));

builder.Services.AddTickerQ(options =>
{
    options.AddOperationalStore(efOptions =>
    {
        efOptions.UseTickerQDbContext<TickerQDbContext>(dbOptions =>
            dbOptions.UseSqlite("Data Source=dashboard-hostjwt.db"));
        // Assistant chat history in the TickerQ store — conversations are keyed
        // per user, so admin and viewer each see only their own threads.
        efOptions.AddAssistantHistory();
    });

    options.AddDashboard(dash =>
    {
        dash.SetTitle("Acme Jobs — Host JWT");
        // Second arg: unauthenticated browser navigations to the dashboard
        // bounce to this login page with ?returnUrl=… and come back after
        // sign-in. API clients still get plain 401s.
        dash.WithHostAuthentication("DashboardAccess", "/account/login");
        // Role-based access level: "ops" members get full control, everyone
        // else is a read-only viewer (mutation UI hidden + writes 403).
        dash.SetReadOnly(ctx => !ctx.User.IsInRole("ops"));

        // AI assistant — enabled only when the operator supplies a key.
        // Read-only users can chat but never get the propose_* draft tools.
        var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(openAiApiKey))
        {
            var openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
            dash.AddAssistant(a => a
                .UseChatClient(new OpenAI.OpenAIClient(openAiApiKey)
                    .GetChatClient(openAiModel)
                    .AsIChatClient())
                .WithModelName(openAiModel));
        }
    });
});

var app = builder.Build();

// Create schemas + seed the Identity users on first run.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<TickerQDbContext>().Database.EnsureCreated();
    scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Database.EnsureCreated();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    if (!await roleManager.RoleExistsAsync("ops"))
        await roleManager.CreateAsync(new IdentityRole("ops"));

    if (await userManager.FindByNameAsync("admin") is null)
    {
        var admin = new IdentityUser("admin") { Email = "admin@local.test" };
        await userManager.CreateAsync(admin, "Admin123$");
        await userManager.AddToRoleAsync(admin, "ops");
    }

    if (await userManager.FindByNameAsync("viewer") is null)
    {
        var viewer = new IdentityUser("viewer") { Email = "viewer@local.test" };
        await userManager.CreateAsync(viewer, "Viewer123$");
        // no "ops" role on purpose — gets the read-only dashboard
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.UseTickerQ();

// ── Login UI (normal form flow against the Identity DB) ──

app.MapGet("/account/login", (string? error, string? returnUrl) =>
    Results.Content(LoginPage(error != null, returnUrl), "text/html"));

app.MapPost("/account/login", async (HttpContext ctx, UserManager<IdentityUser> users, string? returnUrl) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();

    var token = await TryIssueTokenAsync(users, username, password);
    if (token is null)
        return Results.Redirect($"/account/login?error=1{ReturnUrlQuery(returnUrl)}");

    ctx.Response.Cookies.Append("host_jwt", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Expires = DateTimeOffset.UtcNow.AddHours(8),
    });

    // Only follow local return urls — never absolute ones (open-redirect guard).
    var target = returnUrl is ['/', not '/', ..] ? returnUrl : "/tickerq/dashboard";
    return Results.Redirect(target);
});

// JSON token endpoint for API clients — same Identity validation.
app.MapPost("/auth/token", async (TokenRequest body, UserManager<IdentityUser> users) =>
{
    var token = await TryIssueTokenAsync(users, body.Username, body.Password);
    return token is null ? Results.Unauthorized() : Results.Ok(new { accessToken = token });
});

app.MapGet("/logout", (HttpContext ctx) =>
{
    ctx.Response.Cookies.Delete("host_jwt");
    return Results.Redirect("/account/login");
});

static string ReturnUrlQuery(string? returnUrl) =>
    string.IsNullOrEmpty(returnUrl) ? "" : $"&returnUrl={Uri.EscapeDataString(returnUrl)}";

app.MapGet("/", () => Results.Redirect("/tickerq/dashboard"));

app.Run("http://localhost:5212");

// Validates the credentials against the Identity store and, when valid,
// issues the app's JWT carrying the user's Identity roles as claims.
static async Task<string?> TryIssueTokenAsync(UserManager<IdentityUser> users, string username, string password)
{
    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return null;

    var user = await users.FindByNameAsync(username);
    if (user is null || !await users.CheckPasswordAsync(user, password)) return null;

    var roles = await users.GetRolesAsync(user);

    var claims = new List<Claim> { new(ClaimTypes.Name, user.UserName!) };
    claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

    var key = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes("dashboard-hostjwt-dev-signing-key-32b!"));

    var handler = new JsonWebTokenHandler();
    return handler.CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "host-app",
        Audience = "host-app-clients",
        Expires = DateTime.UtcNow.AddHours(8),
        Subject = new ClaimsIdentity(claims),
        SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
    });
}

static string LoginPage(bool error, string? returnUrl) => $$"""
    <!doctype html>
    <html lang="en" style="color-scheme: dark">
    <head>
      <meta charset="utf-8" />
      <meta name="viewport" content="width=device-width, initial-scale=1" />
      <title>Sign in — Acme Jobs</title>
      <style>
        body { margin:0; min-height:100vh; display:grid; place-items:center;
               background:#0b0d10; color:#e6e8ea; font:14px/1.5 system-ui, sans-serif; }
        form { width:300px; padding:28px; border:1px solid #23272e; border-radius:12px; background:#12151a; }
        h1   { margin:0 0 4px; font-size:16px; }
        p    { margin:0 0 18px; color:#8a919c; font-size:12px; }
        label{ display:block; margin:12px 0 4px; font-size:12px; color:#aab1bb; }
        input{ width:100%; box-sizing:border-box; padding:8px 10px; border-radius:8px;
               border:1px solid #2c313a; background:#0b0d10; color:#e6e8ea; }
        button{ width:100%; margin-top:18px; padding:9px; border:0; border-radius:8px;
                background:#4f7cff; color:#fff; font-weight:600; cursor:pointer; }
        .err { margin-top:12px; padding:8px 10px; border-radius:8px; font-size:12px;
               background:#3a1216; color:#ff8a94; border:1px solid #5c1c23; }
        .hint{ margin-top:14px; font-size:11px; color:#667; }
      </style>
    </head>
    <body>
      <form method="post" action="/account/login{{(string.IsNullOrEmpty(returnUrl) ? "" : $"?returnUrl={Uri.EscapeDataString(returnUrl)}")}}">
        <h1>Acme Jobs</h1>
        <p>Sign in with your account (users live in the Identity database).</p>
        <label for="u">Username</label>
        <input id="u" name="username" autocomplete="username" autofocus />
        <label for="p">Password</label>
        <input id="p" name="password" type="password" autocomplete="current-password" />
        {{(error ? """<div class="err">Invalid username or password.</div>""" : "")}}
        <button type="submit">Sign in</button>
        <div class="hint">Seeded users: admin / Admin123$ (full access) · viewer / Viewer123$ (read-only)</div>
      </form>
    </body>
    </html>
    """;

record TokenRequest(string Username, string Password);

// Identity store — separate SQLite file from the TickerQ operational store so
// each context's EnsureCreated() builds its own schema.
class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
    : IdentityDbContext<IdentityUser>(options);

public class HostJwtJobs
{
    private readonly ILogger<HostJwtJobs> _logger;

    public HostJwtJobs(ILogger<HostJwtJobs> logger) => _logger = logger;

    [TickerFunction("Heartbeat", "* * * * *")]
    public Task Heartbeat(TickerFunctionContext context, CancellationToken ct)
    {
        // ILogger on purpose (not Console) — exercises the in-process log capture
        // that feeds the dashboard's per-execution Logs panel.
        _logger.LogInformation("Heartbeat tick at {Now:O}", DateTime.UtcNow);
        return Task.CompletedTask;
    }

    [TickerFunction("NightlyExport", "0 2 * * *")]
    public async Task NightlyExport(TickerFunctionContext context, CancellationToken ct)
    {
        _logger.LogInformation("Nightly export starting…");
        await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        _logger.LogInformation("Nightly export done at {Now:O}", DateTime.UtcNow);
    }
}
