# ---------------------------------------------------------------------------
# Build stage — full SDK, discarded once the app is published.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# The project file is copied on its own first so that `dotnet restore` becomes
# its own cached layer. Editing a .cs or .cshtml file then rebuilds without
# hitting NuGet again; only a change to HopMop.csproj invalidates the restore.
COPY HopMop.csproj ./
RUN dotnet restore HopMop.csproj

COPY . .
RUN dotnet publish HopMop.csproj -c Release -o /app/publish --no-restore

# ---------------------------------------------------------------------------
# Runtime stage — ASP.NET runtime only, no SDK and no source code in the image.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

# Mount point for the Render disk. Created here so the app still starts (with
# throwaway storage) when no disk is attached, instead of failing on boot.
RUN mkdir -p /data

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

# The database is a hosted Neon PostgreSQL instance, so no connection string is
# baked in here — it is a secret and must be supplied by the host as
# ConnectionStrings__DefaultConnection (or DATABASE_URL). Either the
# postgresql://... URI or the Host=...;Database=... form is accepted.

# The data protection key ring stays on the mounted disk: it signs the auth cookies, so keeping it
# in the container would sign every admin out on each deploy.
ENV DataProtection__KeyRingPath="/data/keys"

EXPOSE 8080

ENTRYPOINT ["dotnet", "HopMop.dll"]
