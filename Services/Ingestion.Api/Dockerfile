FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# ── Restore layer (cached until .csproj files change) ────────────────────────
COPY ["Directory.Build.props", "."]
COPY ["Services/Ingestion.Api/Ingestion.Api.csproj",                 "Services/Ingestion.Api/"]
COPY ["Shared/Contracts/Contracts.csproj",                           "Shared/Contracts/"]
COPY ["Shared/Metadata/Metadata.csproj",                             "Shared/Metadata/"]
COPY ["Shared/Auth/Auth.csproj",                                     "Shared/Auth/"]
COPY ["Shared/Telemetry/Telemetry.csproj",                           "Shared/Telemetry/"]

RUN dotnet restore "Services/Ingestion.Api/Ingestion.Api.csproj"

# ── Build ─────────────────────────────────────────────────────────────────────
COPY . .
RUN dotnet publish "Services/Ingestion.Api/Ingestion.Api.csproj" \
    -c Release -o /app/publish --no-restore

# ── Runtime image ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ReportingPlatform.Ingestion.Api.dll"]
