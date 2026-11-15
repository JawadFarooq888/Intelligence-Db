# IntelligenceDB API + web portal in one image.
# The React app is built and served by the API from wwwroot, so the whole portal is one container.
# Build from the repository root:  docker build -f deploy/api.Dockerfile -t intelligencedb-api .

# ---- 1. Web app (React) ----
FROM node:22-alpine AS web
WORKDIR /web
COPY src/IntelligenceDB.Web/package.json src/IntelligenceDB.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/IntelligenceDB.Web/ ./
RUN npm run build

# ---- 2. API (.NET) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props IntelligenceDB.slnx ./
COPY .config/ .config/
COPY sql/ sql/
COPY src/ src/
RUN dotnet publish src/IntelligenceDB.Api/IntelligenceDB.Api.csproj -c Release -o /out

# ---- 3. Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out ./
COPY --from=web /web/dist ./wwwroot
RUN mkdir -p /app/logs && chown -R $APP_UID /app/logs
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Logging__FilePath=/app/logs/api-.log
EXPOSE 8080
# Run as the image's non-root user.
USER $APP_UID
ENTRYPOINT ["dotnet", "IntelligenceDB.Api.dll"]
