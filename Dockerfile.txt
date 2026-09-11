# SCRUM-56 + SCRUM-74 - Dockerfile builds and runs from clean checkout
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/ProcessingService/ProcessingService.csproj", "src/ProcessingService/"]
RUN dotnet restore "src/ProcessingService/ProcessingService.csproj"
COPY . .
WORKDIR "/src/src/ProcessingService"
RUN dotnet build "ProcessingService.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ProcessingService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ProcessingService.dll"]
