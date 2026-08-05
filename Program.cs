using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
        options.ExpireTimeSpan = TimeSpan.FromDays(1);
    });

builder.Services.AddSingleton<IPasswordHasher<AdminUser>, PasswordHasher<AdminUser>>();

var app = builder.Build();

// Ensure DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Seed default admin only if credentials are explicitly provided.
    var defaultEmail = builder.Configuration["Admin:DefaultEmail"];
    var defaultPassword = builder.Configuration["Admin:DefaultPassword"];
    if (!db.AdminUsers.Any() && !string.IsNullOrWhiteSpace(defaultEmail) && !string.IsNullOrWhiteSpace(defaultPassword))
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminUser>>();

        var admin = new AdminUser { Email = defaultEmail };
        admin.PasswordHash = hasher.HashPassword(admin, defaultPassword);

        db.AdminUsers.Add(admin);
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