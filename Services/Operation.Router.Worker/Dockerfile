FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# ── Restore layer (cached until .csproj files change) ────────────────────────
COPY ["Directory.Build.props", "."]
COPY ["Services/Operation.Router.Worker/Operation.Router.Worker.csproj", "Services/Operation.Router.Worker/"]
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

RUN dotnet restore "Services/Operation.Router.Worker/Operation.Router.Worker.csproj"

# ── Build ─────────────────────────────────────────────────────────────────────
COPY . .
RUN dotnet publish "Services/Operation.Router.Worker/Operation.Router.Worker.csproj" \
    -c Release -o /app/publish --no-restore

# ── Runtime image ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ReportingPlatform.Operation.Router.Worker.dll"]
