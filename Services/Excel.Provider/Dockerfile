FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# ── Restore layer (cached until .csproj files change) ────────────────────────
COPY ["Directory.Build.props", "."]
COPY ["Services/Excel.Provider/Excel.Provider.csproj", "Services/Excel.Provider/"]
COPY ["proto/", "proto/"]

RUN dotnet restore "Services/Excel.Provider/Excel.Provider.csproj"

# ── Build ─────────────────────────────────────────────────────────────────────
COPY . .
RUN dotnet publish "Services/Excel.Provider/Excel.Provider.csproj" \
    -c Release -o /app/publish --no-restore

# ── Runtime image ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl for healthcheck (if needed)
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Create default data directory
RUN mkdir -p /app/ExcelData

COPY --from=build /app/publish .

# ── Environment overrides ─────────────────────────────────────────────────────
# These are overridden by docker-compose environment: section at runtime.
ENV Provider__TokenEndpoint="http://request-api:5000/api/v1/providers/token"
ENV Provider__BridgeGrpcUrl="http://provider-bridge:5400"
ENV Excel__DataPath="/app/ExcelData"
ENV Ingestion__BaseUrl="http://ingestion-api:5100"
ENV Ingestion__TokenEndpoint="http://request-api:5000/api/v1/providers/token"
ENV ASPNETCORE_URLS="http://+:5600"

EXPOSE 5600

VOLUME ["/app/ExcelData"]

ENTRYPOINT ["dotnet", "ReportingPlatform.Excel.Provider.dll"]
