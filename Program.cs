using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using HopMop.Data;
using HopMop.Models;

var builder = WebApplication.CreateBuilder(args);

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

    // Seed default admin
    if (!db.AdminUsers.Any())
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminUser>>();
        var defaultEmail = builder.Configuration["Admin:DefaultEmail"] ?? "admin@hopmop.local";
        var defaultPassword = builder.Configuration["Admin:DefaultPassword"] ?? "ChangeMe123!";

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