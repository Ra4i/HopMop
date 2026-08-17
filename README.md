# HopMop

The website for HopMop Ltd. (ХопМоп ЕООД), a cleaning company in Sofia. It is a
single ASP.NET Core 8 MVC application that serves the public marketing pages and
a small admin area behind a login.

**Public pages** — home, about, services, a before/after gallery, and a contact
form that files an inquiry.

**Admin area** — sign in at `/Account/Login` to upload before/after photo pairs,
read and resolve incoming inquiries, and manage the accounts that can log in.

The site's content is in Bulgarian.

---

## Requirements

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)
- A PostgreSQL database. The project targets [Neon](https://neon.tech) — see
  [Database (Neon PostgreSQL)](#database-neon-postgresql) below.

## Running locally

```bash
git clone <repository-url>
cd HopMop

cp .env.example .env        # then edit .env — see below
dotnet run
```

Open the URL printed in the console (by default <https://localhost:50327>).

### Creating the first admin

The app seeds one admin account, and only when the `Users` table is completely
empty. Set both of these in `.env` before the first run:

```
Admin__DefaultEmail=you@example.com
Admin__DefaultPassword=a-long-unique-password
```

If they are missing, the app still starts but logs a warning and nobody can sign
in. Once the first admin exists, create further accounts from the **Потребители**
page in the admin area; the seed values are then ignored and can be removed.

Locked out with no admin left? Delete the rows in the `Users` table (from the
Neon SQL editor, or any `psql`/GUI client), set the two values again, and
restart. Inquiries and the gallery are untouched.

---

## Database (Neon PostgreSQL)

The app runs on **Neon**, a hosted PostgreSQL service — it no longer uses a local
SQLite file. There is no database file to keep, back up, or mount a disk for; the
data lives in the Neon project.

The connection string is a secret and is **not** in `appsettings.json`. Supply it
as an environment variable (or in a local `.env` file):

```
ConnectionStrings__DefaultConnection=Host=...;Database=...;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true
```

Copy the host, database, username, and password from the Neon dashboard. Neon
accepts TLS connections only, so `SSL Mode=Require` must stay. Without this
variable set the app fails to start, which is deliberate — it will not silently
fall back to anything.

The schema is still created by `EnsureCreated()` at startup rather than by EF
Core Migrations, so pointing the app at an empty Neon database is all the setup
there is. See [Adding a schema change](#adding-a-schema-change) for the one
consequence of that.

---

## Configuration

Settings are read from `appsettings.json`, then environment variables, then a
local `.env` file. **Values already set in the real environment always win over
`.env`**, so a `.env` that accidentally reaches a server cannot override the
host's settings.

In configuration keys, a double underscore maps to a nested section:
`Admin__DefaultEmail` → `Admin:DefaultEmail`.

| Key | Default | What it does |
| --- | --- | --- |
| `ConnectionStrings__DefaultConnection` | — (required) | Npgsql connection string for the Neon PostgreSQL database. Not stored in `appsettings.json`; the app will not start without it. |
| `Admin__DefaultEmail` | — | Email of the seeded first admin. Only used when no users exist. |
| `Admin__DefaultPassword` | — | Password of the seeded first admin. Only used when no users exist. |
| `Site__Phone` | `+359 88 800 0000` | Phone number shown in the header, footer button, and contact page. |
| `Site__ViberChatUrl` | empty | Viber chat link used by the header, the floating button, the services page, and the contact page. All of them are hidden when empty. |
| `Security__RequireHttps` | `true` in Production | Redirects HTTP to HTTPS. See the proxy note below. |
| `Security__HttpsPort` | `443` | Port the HTTPS redirect points at. Change only if TLS is served on a non-standard port. |
| `DataProtection__KeyRingPath` | `keys/` under the app folder | Where the cookie-signing keys are stored. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Must **not** be `Development` on a public server. |

> **The phone numbers in `appsettings.json` are placeholders.** Set `Site__Phone`
> and `Site__ViberChatUrl` to the real values before going live. The Viber
> buttons stay hidden while their value is empty.

### Secrets

Never commit real credentials. `.gitignore` already excludes `.env`, `*.db`,
`keys/`, and `appsettings.Development.json`. On a server, set the values as
environment variables (or your host's secret manager) rather than deploying a
`.env` file.

---

## Deploying

### With Docker (Render.com and other container hosts)

The [Dockerfile](Dockerfile) does a multi-stage build — SDK image to publish,
ASP.NET runtime image to run — and listens on port **8080**.

```bash
docker build -t hopmop .
docker run -p 8080:8080 -v hopmop-data:/data hopmop
```

It presets these inside the image:

| Variable | Value |
| --- | --- |
| `ASPNETCORE_URLS` | `http://+:8080` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `DataProtection__KeyRingPath` | `/data/keys` |

The connection string is deliberately **not** baked into the image — the host
must set `ConnectionStrings__DefaultConnection` itself.

**On Render**, set `ConnectionStrings__DefaultConnection`, `Admin__DefaultEmail`,
and `Admin__DefaultPassword` in the dashboard, and attach a disk mounted at
**`/data`** for the cookie-signing keys. Render terminates TLS at its edge and
forwards `X-Forwarded-Proto`, which this app honours, so the HTTPS redirect works
without a redirect loop and no extra configuration is needed.

Without a disk mounted at `/data` the app still starts, but the login keys are
thrown away on every deploy. The database itself is unaffected — it lives in
Neon, not in the container.

### Without Docker

```bash
dotnet publish -c Release -o ./publish
```

Copy `./publish` to the server and run `dotnet HopMop.dll` behind a reverse
proxy (nginx, Caddy, IIS) that terminates TLS.

### Checklist

1. **`ASPNETCORE_ENVIRONMENT=Production`.** Anything else exposes the developer
   exception page, which prints stack traces and configuration to visitors.
2. **Set `Admin__DefaultEmail` and `Admin__DefaultPassword`** as environment
   variables for the first boot, then remove them.
3. **Serve over HTTPS.** Auth cookies are marked `Secure` in Production, so the
   admin area cannot be used over plain HTTP at all.
4. **Forward the right headers.** The app trusts `X-Forwarded-For` and
   `X-Forwarded-Proto`. Your proxy must set both, and must not let a client
   spoof them. If your proxy terminates TLS but cannot send `X-Forwarded-Proto`,
   set `Security__RequireHttps=false` — otherwise the app and the proxy will
   loop redirecting each other.
5. **Set `ConnectionStrings__DefaultConnection`** to the Neon connection string.
   The app will not start without it.
6. **Give these two paths persistent storage** — a volume or a real disk, not
   the container's ephemeral filesystem:

   | Path | Loss on redeploy without it |
   | --- | --- |
   | `wwwroot/uploads/` | Every uploaded photo; the gallery shows broken images. |
   | `keys/` | Everyone is signed out on each restart. |

7. **Back up the Neon database and `wwwroot/uploads/` together.** They reference
   each other; restoring only one leaves dangling gallery rows. Neon's own
   branching/restore covers the database half.
8. **Restrict `AllowedHosts`** in `appsettings.json` from `*` to your real
   domain if the app is reachable by IP or by more than one hostname.

### Adding a schema change

The database is created with `EnsureCreated()`, which builds missing tables but
never alters existing ones — the project does not use EF Core Migrations. A new
column on an existing model therefore needs a matching additive `ALTER TABLE` in
`EnsureInquiryStatusColumns` in [Program.cs](Program.cs) — follow the pattern
there — or every query against that table will fail on an already-deployed
database.

Note that EF Core quotes the identifiers it creates, so PostgreSQL keeps them in
mixed case: hand-written SQL has to say `"Inquiries"."IsResolved"`, with the
quotes, or Postgres folds the name to lower case and cannot find it.

---

## Security notes

What the app already does, so it isn't accidentally removed later:

- **Deny by default.** The authorization fallback policy requires a signed-in
  user for every endpoint that does not opt out with `[AllowAnonymous]`, so a
  new controller cannot be left unprotected by forgetting `[Authorize]`.
- **Cookies are re-validated per request** against the database, so deleting or
  demoting an account takes effect immediately instead of when the cookie expires.
- **Passwords** are hashed with ASP.NET Core's `PasswordHasher` (PBKDF2), and are
  transparently re-hashed on login when the parameters change. Minimum 10 characters.
- **Login is rate limited** to 10 attempts per 5 minutes per IP, and the contact
  form to 5 per 10 minutes. Failed logins are logged with the source IP.
- **Login failures are indistinguishable.** One message for every cause, and an
  unknown email is still verified against a dummy hash so response time does not
  reveal which accounts exist.
- **All state-changing forms** post with an antiforgery token.
- **Uploads are checked by content**, not by file extension: the leading magic
  bytes must match JPG, PNG, or WEBP. Files are stored under a generated GUID
  name, so nothing a user typed ever reaches the filesystem path.
- **Security headers** on every response: HSTS, a Content-Security-Policy that
  allows scripts only from this origin, `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, and a restrictive `Referrer-Policy`.
- **The contact form cannot overpost.** The bookkeeping fields on `Inquiry`
  (`Id`, `CreatedAt`, `IsResolved`, `ResolvedAt`) are `[BindNever]`, so a crafted
  POST cannot file an inquiry that is already marked resolved.

Because the CSP sets `script-src 'self'`, inline `onclick`/`onsubmit` attributes
will not run. Add a `data-confirm="..."` attribute to a form instead — the
delegated handler in [wwwroot/js/site.js](wwwroot/js/site.js) picks it up.

### Not included

The app has no email delivery, so inquiries are only visible in the admin area —
someone has to check it. There is also no self-service password reset; an admin
resets a password by deleting and recreating the account.

---

## Project layout

| Path | Contents |
| --- | --- |
| [Program.cs](Program.cs) | Startup, auth, rate limits, security headers, database bootstrap. |
| [Controllers/](Controllers/) | `Home` (public), `Account` (login), `Admin` (gallery + inquiries), `Users`. |
| [Models/](Models/) | `Inquiry`, `PhotoPair`, `User`. |
| [Data/](Data/) | `AppDbContext` and the indexes. |
| [Views/](Views/) | Razor views; shared layout in `Views/Shared/_Layout.cshtml`. |
| [wwwroot/](wwwroot/) | CSS, JavaScript, and uploaded gallery images. |

## License

Proprietary — all rights reserved. See [LICENSE](LICENSE).
