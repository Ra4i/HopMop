# HopMop

HopMop is a simple ASP.NET Core web app for managing inquiries, gallery images, and admin access.

## Configuration

This app reads settings from `appsettings.json`, environment variables, and a local `.env` file if present.

### Local secrets

1. Copy `.env.example` to `.env`.
2. Set values for `Admin__DefaultEmail`, `Admin__DefaultPassword`, and the `Smtp__*` settings.
3. Do not commit `.env` to source control.

The repository already ignores `.env` and other local secret files via `.gitignore`.

### Admin seeding

A default admin user is only seeded when both `Admin__DefaultEmail` and `Admin__DefaultPassword` are provided.
