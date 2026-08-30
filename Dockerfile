# ── Build stage ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore (layer-cached)
COPY src/ProcessingService/ProcessingService.csproj src/ProcessingService/
RUN dotnet restore src/ProcessingService/ProcessingService.csproj

# Copy everything else and publish
COPY src/ src/
RUN dotnet publish src/ProcessingService/ProcessingService.csproj \
    -c Release -o /app/publish --no-restore

# ── Runtime stage ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ProcessingService.dll"]
