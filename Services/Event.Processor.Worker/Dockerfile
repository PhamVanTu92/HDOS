FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# ── Restore layer (cached until .csproj files change) ────────────────────────
COPY ["Directory.Build.props", "."]
COPY ["Services/Event.Processor.Worker/Event.Processor.Worker.csproj", "Services/Event.Processor.Worker/"]
COPY ["Shared/Contracts/Contracts.csproj",                           "Shared/Contracts/"]
COPY ["Shared/HubContracts/HubContracts.csproj",                     "Shared/HubContracts/"]
COPY ["Shared/Operations/Operations.csproj",                         "Shared/Operations/"]
COPY ["Shared/Metadata/Metadata.csproj",                             "Shared/Metadata/"]
COPY ["Shared/Providers/Providers.csproj",                           "Shared/Providers/"]
COPY ["Shared/Adapters/Adapters.csproj",                             "Shared/Adapters/"]
COPY ["Shared/Transformers/Transformers.csproj",                     "Shared/Transformers/"]
COPY ["Shared/Resolver/Resolver.csproj",                             "Shared/Resolver/"]
COPY ["Shared/QueryBuilder/QueryBuilder.csproj",                     "Shared/QueryBuilder/"]
COPY ["Shared/Caching/Caching.csproj",                               "Shared/Caching/"]
COPY ["Shared/Telemetry/Telemetry.csproj",                           "Shared/Telemetry/"]

RUN dotnet restore "Services/Event.Processor.Worker/Event.Processor.Worker.csproj"

# ── Build ─────────────────────────────────────────────────────────────────────
COPY . .
RUN dotnet publish "Services/Event.Processor.Worker/Event.Processor.Worker.csproj" \
    -c Release -o /app/publish --no-restore

# ── Runtime image ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ReportingPlatform.Event.Processor.Worker.dll"]
