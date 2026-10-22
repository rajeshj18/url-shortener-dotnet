# Api service image (ADR-0009). Build from the repository root:
#   docker build -f src/UrlShortener.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet restore src/UrlShortener.Api/UrlShortener.Api.csproj
RUN dotnet publish src/UrlShortener.Api/UrlShortener.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app ./
# Non-root user provided by the .NET images (ADR-0011).
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=15s --retries=3 \
  CMD wget -q -O /dev/null http://localhost:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "UrlShortener.Api.dll"]
