using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HopMop.Data;
using HopMop.Models;

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

        Environment.SetEnvironmentVariable(key, value);
    }
}

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromDays(1);
        options.SlidingExpiration = true;

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

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

var app = builder.Build();

// Ensure DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Seed default admin only if credentials are explicitly provided.
    var defaultEmail = builder.Configuration["Admin:DefaultEmail"];
    var defaultPassword = builder.Configuration["Admin:DefaultPassword"];
    if (!db.Users.Any() && !string.IsNullOrWhiteSpace(defaultEmail) && !string.IsNullOrWhiteSpace(defaultPassword))
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var admin = new User { Email = defaultEmail, IsAdmin = true };
        admin.PasswordHash = hasher.HashPassword(admin, defaultPassword);

        db.Users.Add(admin);
        db.SaveChanges();

        System.Diagnostics.Debug.WriteLine($"Admin user created: {defaultEmail}");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();