# IntelligenceDB Worker (collector for servers the portal reaches directly).
# Build from the repository root:  docker build -f deploy/worker.Dockerfile -t intelligencedb-worker .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props IntelligenceDB.slnx ./
COPY .config/ .config/
COPY sql/ sql/
COPY src/ src/
RUN dotnet publish src/IntelligenceDB.Worker/IntelligenceDB.Worker.csproj -c Release -o /out

# The Worker uses ASP.NET Core Data Protection, so it needs the ASP.NET runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out ./
RUN mkdir -p /app/logs && chown -R $APP_UID /app/logs
ENV DOTNET_ENVIRONMENT=Production \
    Logging__FilePath=/app/logs/worker-.log
USER $APP_UID
ENTRYPOINT ["dotnet", "IntelligenceDB.Worker.dll"]
