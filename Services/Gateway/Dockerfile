FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# ── Restore layer (cached until .csproj files change) ────────────────────────
COPY ["Directory.Build.props", "."]
COPY ["Services/Gateway/Gateway.csproj",                             "Services/Gateway/"]
COPY ["Shared/Telemetry/Telemetry.csproj",                           "Shared/Telemetry/"]
COPY ["Shared/Contracts/Contracts.csproj",                           "Shared/Contracts/"]

RUN dotnet restore "Services/Gateway/Gateway.csproj"

# ── Build ─────────────────────────────────────────────────────────────────────
COPY . .
RUN dotnet publish "Services/Gateway/Gateway.csproj" \
    -c Release -o /app/publish --no-restore

# ── Runtime image ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ReportingPlatform.Gateway.dll"]
