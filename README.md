HopMop — ASP.NET Core MVC site (SQLite)

Run:
1. dotnet restore
2. dotnet run

Admin default credentials are seeded from appsettings.json (change immediately).

Files created:
- Program.cs, appsettings.json
- Data/AppDbContext.cs
- Models: AdminUser, PhotoPair, Inquiry
- Controllers: HomeController, AccountController, AdminController
- Views (Home pages + Admin)
- wwwroot assets (css, js, uploads)

Notes:
- Update SMTP settings in appsettings.json to enable contact emails.
- Change default admin password in appsettings or by updating the DB.
