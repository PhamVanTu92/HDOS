FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# ── Restore layer (cached until .csproj files change) ────────────────────────
COPY ["Directory.Build.props", "."]
COPY ["Services/Request.Api/Request.Api.csproj",                     "Services/Request.Api/"]
COPY ["Shared/Contracts/Contracts.csproj",                           "Shared/Contracts/"]
COPY ["Shared/Metadata/Metadata.csproj",                             "Shared/Metadata/"]
COPY ["Shared/Providers/Providers.csproj",                           "Shared/Providers/"]
COPY ["Shared/Adapters/Adapters.csproj",                             "Shared/Adapters/"]
COPY ["Shared/Transformers/Transformers.csproj",                     "Shared/Transformers/"]
COPY ["Shared/Resolver/Resolver.csproj",                             "Shared/Resolver/"]
COPY ["Shared/QueryBuilder/QueryBuilder.csproj",                     "Shared/QueryBuilder/"]
COPY ["Shared/Caching/Caching.csproj",                               "Shared/Caching/"]
COPY ["Shared/Telemetry/Telemetry.csproj",                           "Shared/Telemetry/"]
COPY ["Shared/Operations/Operations.csproj",                         "Shared/Operations/"]
COPY ["Shared/Auth/Auth.csproj",                                     "Shared/Auth/"]
COPY ["proto/", "proto/"]

RUN dotnet restore "Services/Request.Api/Request.Api.csproj"

# ── Build ─────────────────────────────────────────────────────────────────────
COPY . .
RUN dotnet publish "Services/Request.Api/Request.Api.csproj" \
    -c Release -o /app/publish --no-restore

# ── Runtime image ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ReportingPlatform.Request.Api.dll"]
