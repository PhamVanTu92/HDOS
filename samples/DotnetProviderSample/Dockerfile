FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["samples/DotnetProviderSample/DotnetProviderSample.csproj", "samples/DotnetProviderSample/"]
COPY ["Shared/ProviderSdk/ProviderSdk.csproj", "Shared/ProviderSdk/"]
COPY ["proto/", "proto/"]
RUN dotnet restore "samples/DotnetProviderSample/DotnetProviderSample.csproj"
COPY . .
RUN dotnet publish "samples/DotnetProviderSample/DotnetProviderSample.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DotnetProviderSample.dll"]
