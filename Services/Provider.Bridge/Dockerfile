FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# ── Restore layer (cached until .csproj files change) ────────────────────────
COPY ["Directory.Build.props", "."]
COPY ["Services/Provider.Bridge/Provider.Bridge.csproj",             "Services/Provider.Bridge/"]
COPY ["Shared/Contracts/Contracts.csproj",                           "Shared/Contracts/"]
COPY ["Shared/Providers/Providers.csproj",                           "Shared/Providers/"]
COPY ["Shared/Caching/Caching.csproj",                               "Shared/Caching/"]
COPY ["Shared/Telemetry/Telemetry.csproj",                           "Shared/Telemetry/"]
COPY ["proto/", "proto/"]

RUN dotnet restore "Services/Provider.Bridge/Provider.Bridge.csproj"

# ── Build ─────────────────────────────────────────────────────────────────────
COPY . .
RUN dotnet publish "Services/Provider.Bridge/Provider.Bridge.csproj" \
    -c Release -o /app/publish --no-restore

# ── Runtime image ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ReportingPlatform.Provider.Bridge.dll"]
