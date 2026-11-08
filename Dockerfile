# syntax=docker/dockerfile:1
# The server image (design D4): one container serving the web UI, the read API and the online MCP endpoint.
#   docker build -t panopticon .
# Tags are pinned to match global.json (SDK 10.0.403), the ASP.NET runtime the packages target (10.0.14), and
# the Node version the web build is developed with.

# 1. Web UI: Vite builds web/ into src/Panopticon.Server/wwwroot (design D10).
FROM node:24.13.1-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# 2. Server: restore from the project files first so the layer caches, then publish with the built UI.
FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Panopticon.Contracts/Panopticon.Contracts.csproj src/Panopticon.Contracts/
COPY src/Panopticon.Server/Panopticon.Server.csproj src/Panopticon.Server/
RUN dotnet restore src/Panopticon.Server/Panopticon.Server.csproj
COPY src/Panopticon.Contracts/ src/Panopticon.Contracts/
COPY src/Panopticon.Server/ src/Panopticon.Server/
COPY --from=web /src/src/Panopticon.Server/wwwroot src/Panopticon.Server/wwwroot/
RUN dotnet publish src/Panopticon.Server/Panopticon.Server.csproj -c Release --no-restore -o /app

# 3. Runtime: the non-root `app` user, listening on 8080 (the image default).
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS runtime
# Npgsql probes for GSSAPI (Kerberos) at start-up and logs "Error: libgssapi_krb5.so.2" without it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
EXPOSE 8080
# Configuration comes from the environment, e.g. ConnectionStrings__Panopticon, Panopticon__BootstrapToken and
# Panopticon__S3__*. Run once with --migrate (applies migrations, then exits) before starting a new version.
ENTRYPOINT ["dotnet", "Panopticon.Server.dll"]
