using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.RateLimiting;
using HopMop.Data;
using HopMop.Models;

// Local development convenience: pull key=value pairs out of a .env file next to
// the project. Values already present in the real environment always win, so a
// stale .env that ends up on a server can never override what the host sets
// (most importantly ASPNETCORE_ENVIRONMENT and the connection string).
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

        var separatorIndex = trimmed.IndexOf('=');
        if (separatorIndex <= 0) continue;

        var key = trimmed[..separatorIndex].Trim();
        var value = trimmed[(separatorIndex + 1)..].Trim();
        if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
        {
            value = value[1..^1];
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}

var builder = WebApplication.CreateBuilder(args);

var isProduction = builder.Environment.IsProduction();

// Whether this deployment is served over HTTPS. It drives the redirect *and*
// the "secure" flag on both cookies, and the three have to agree: a secure
// antiforgery cookie on a plain-HTTP request throws outright, so deciding it
// once here is what stops the opt-out below from 500-ing every page with a form.
var requireHttps = builder.Configuration.GetValue("Security:RequireHttps", isProduction);

builder.Services.AddControllersWithViews();

// Behind a reverse proxy (nginx, Cloudflare, any PaaS router) the app only sees
// the proxy's own connection. Without this, Request.Scheme is "http" even on an
// HTTPS site, which breaks the HTTPS redirect and stops "secure" cookies from
// ever being sent. KnownNetworks/Proxies are cleared because the proxy address
// is not knowable ahead of time in a container.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Data protection keys sign the auth and antiforgery cookies. By default they
// live in a temp folder, so every restart (and every container redeploy) throws
// them away and logs everyone out. Persisting them to the content root — which
// deployments are expected to back with a volume — keeps sessions alive.
var configuredKeyRing = builder.Configuration["DataProtection:KeyRingPath"];
var keyRingPath = string.IsNullOrWhiteSpace(configuredKeyRing)
    ? Path.Combine(builder.Environment.ContentRootPath, "keys")
    : configuredKeyRing;
Directory.CreateDirectory(keyRingPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
    .SetApplicationName("HopMop");

// Resolved once here rather than inline so a missing or unparseable value fails
// with an explanation at startup, instead of surfacing as an Npgsql error on the
// first request.
var database = NeonConnection.Resolve(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(database.ConnectionString, npgsql =>
        // Neon suspends an idle compute and only wakes it when something connects
        // again, so the first query after a quiet period can time out or be
        // dropped through no fault of the query. Without this the visitor who
        // happens to arrive first gets the error page. The retries are what turn
        // a wake-up into a slow request rather than a failed one.
        npgsql.EnableRetryOnFailure(
            maxRetryCount: 6,
            maxRetryDelay: TimeSpan.FromSeconds(8),
            errorCodesToAdd: null)));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromDays(1);
        options.SlidingExpiration = true;

        options.Cookie.Name = "HopMop.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Over plain HTTP a "secure" cookie is simply never sent, so nobody
        // could log in at all on a deployment that has opted out of HTTPS.
        options.Cookie.SecurePolicy = requireHttps
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;

        // A cookie alone is not proof of access: re-check the account on every
        // request. Without this, a cookie keeps working after its user is
        // deleted (or the DB is dropped), and a demoted admin keeps admin
        // rights until the cookie expires.
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async ctx =>
            {
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

                User? user = null;
                if (int.TryParse(ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                {
                    user = await db.Users.FindAsync(userId);
                }

                var isAdminClaim = ctx.Principal?.FindFirstValue("IsAdmin");
                if (user is null || !string.Equals(user.IsAdmin.ToString(), isAdminClaim, StringComparison.Ordinal))
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(c => c.Type == "IsAdmin" && c.Value == "True")));

    // Deny by default: any endpoint that does not opt out with [AllowAnonymous]
    // requires a logged-in user, so a new controller cannot be left unprotected
    // by forgetting [Authorize].
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "HopMop.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    // Always here would throw on a plain-HTTP request rather than degrade, so
    // it follows requireHttps exactly like the auth cookie above.
    options.Cookie.SecurePolicy = requireHttps
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});

// Two public endpoints are worth throttling per client IP: the login form (so a
// stolen email cannot be brute-forced) and the contact form (so the inquiry
// table cannot be flooded). Both reject with 429 rather than queueing.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimitPolicies.Login, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));

    options.AddPolicy(RateLimitPolicies.ContactForm, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));

    static string ClientKey(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
});

// The upload action rejects anything over 10 MB per image, but model binding
// happens first — without a ceiling here the server would buffer an arbitrarily
// large body before the check ever runs.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 25 * 1024 * 1024;
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

// Without an explicit port the redirect middleware tries to infer one from the
// server's own addresses. A deployment that listens on plain HTTP behind a TLS
// proxy has no HTTPS address to infer from, so the middleware would silently do
// nothing and the "redirect to HTTPS" setting would be a no-op.
builder.Services.AddHttpsRedirection(options =>
{
    options.HttpsPort = builder.Configuration.GetValue("Security:HttpsPort", 443);
});

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

var app = builder.Build();

// Ensure DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    log.LogInformation("Using PostgreSQL at {Target}.", database.Target);

    // Both branches exist so the TLS decision is never invisible: either the
    // supplied string was weaker than it should be and was raised, or the opt-out
    // is on and the connection really is unverified.
    if (database.UpgradedTls)
    {
        log.LogInformation(
            "Raised the database connection to SSL Mode=VerifyFull. Set " +
            "Database__AllowUnverifiedTls=true only if certificate validation " +
            "cannot work in this environment.");
    }

    if (!database.VerifiesServerIdentity)
    {
        log.LogWarning(
            "The database connection does not verify the server's certificate, so " +
            "the traffic is encrypted but the server's identity is unchecked.");
    }

    // A redeploy can easily land while the Neon compute is suspended, and every
    // statement below is the app's first contact with it. Running them through
    // the execution strategy means the wake-up delay is retried rather than
    // crashing the container on boot — which, on a host that restarts failed
    // deploys, otherwise turns into a loop.
    var strategy = db.Database.CreateExecutionStrategy();
    strategy.Execute(() =>
    {
        // A retry re-enters this delegate from the top, so anything the previous
        // attempt left tracked has to go or the seed below would be queued twice.
        db.ChangeTracker.Clear();

        db.Database.EnsureCreated();

        // EnsureCreated() only ever creates missing tables — it never alters one that
        // already exists. Columns added to a model afterwards therefore have to be
        // patched into existing databases by hand, or every query against them fails.
        EnsureInquiryStatusColumns(db);

        // Seed default admin only if credentials are explicitly provided.
        var defaultEmail = builder.Configuration["Admin:DefaultEmail"];
        var defaultPassword = builder.Configuration["Admin:DefaultPassword"];

        if (!db.Users.Any())
        {
            if (!string.IsNullOrWhiteSpace(defaultEmail) && !string.IsNullOrWhiteSpace(defaultPassword))
            {
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

                var admin = new User { Email = defaultEmail.Trim().ToLowerInvariant(), IsAdmin = true };
                admin.PasswordHash = hasher.HashPassword(admin, defaultPassword);

                db.Users.Add(admin);
                db.SaveChanges();

                log.LogInformation("Seeded the first admin account ({Email}).", admin.Email);
            }
            else
            {
                // Without this warning a fresh deployment looks fine but has no way
                // in — the login page simply rejects every attempt.
                log.LogWarning(
                    "No users exist and Admin:DefaultEmail / Admin:DefaultPassword are not set, " +
                    "so no admin account was created and nobody can sign in. " +
                    "Set both values and restart.");
            }
        }
    });
}

// First in the pipeline on purpose. Everything after it that looks at the
// scheme or the client IP — HSTS, the HTTPS redirect, the per-IP rate limiters —
// sees the proxy's own connection unless the forwarded headers are applied
// first. HSTS in particular checks Request.IsHttps and would skip its header on
// every proxied request if this ran after it.
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Turns a bare 404/403 into the styled error page instead of a blank response.
app.UseStatusCodePagesWithReExecute("/Home/Error", "?code={0}");

// Opt-out escape hatch: a host that terminates TLS but does not send
// X-Forwarded-Proto would otherwise send the browser into a redirect loop.
if (requireHttps)
{
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "object-src 'none'; " +
        // blob: covers the client-side preview of a not-yet-uploaded image.
        "img-src 'self' data: blob:; " +
        "style-src 'self' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "script-src 'self'; " +
        "connect-src 'self'";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// The deny-by-default fallback policy also applies to requests that match no
// route at all, so without this a simple typo in the URL bounced anonymous
// visitors to the admin login page instead of showing them a 404. This gives
// unmatched URLs a real anonymous endpoint that returns 404, which the status
// code pages middleware above then renders as the error view.
app.MapFallback(context =>
{
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    return Task.CompletedTask;
}).AllowAnonymous();

app.Run();

// Adds the inquiry status columns to an existing database if they are not there
// yet. Both statements are additive, so no data is lost and re-running the app
// after the columns exist is a no-op.
static void EnsureInquiryStatusColumns(AppDbContext db)
{
    var conn = db.Database.GetDbConnection();
    var wasClosed = conn.State != System.Data.ConnectionState.Open;
    if (wasClosed) conn.Open();

    try
    {
        // EF Core quotes the identifiers it generates, so the table and columns
        // really are named "Inquiries"/"IsResolved" in mixed case rather than
        // folded to lower case — which is why every reference below is quoted,
        // and why the catalog is matched against the mixed-case spelling.
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var read = conn.CreateCommand())
        {
            read.CommandText =
                "SELECT column_name FROM information_schema.columns " +
                "WHERE table_schema = current_schema() AND table_name = 'Inquiries';";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
            }
        }

        // No rows means there is no Inquiries table at all — EnsureCreated() will
        // have built it from the current model, so there is nothing to patch.
        if (columns.Count == 0) return;

        if (!columns.Contains("IsResolved"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText =
                "ALTER TABLE \"Inquiries\" ADD COLUMN \"IsResolved\" boolean NOT NULL DEFAULT false;";
            alter.ExecuteNonQuery();
        }

        if (!columns.Contains("ResolvedAt"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText =
                "ALTER TABLE \"Inquiries\" ADD COLUMN \"ResolvedAt\" timestamp with time zone NULL;";
            alter.ExecuteNonQuery();
        }
    }
    finally
    {
        if (wasClosed) conn.Close();
    }
}

// Names shared between the rate limiter registration above and the
// [EnableRateLimiting] attributes on the actions, so a typo cannot silently
// leave an endpoint unthrottled.
public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string ContactForm = "contact-form";
}
