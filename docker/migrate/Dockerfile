# One-off migration job (ADR-0009): an EF Core migration bundle for the links database. It runs before the
# services start (compose: service_completed_successfully). Services never migrate at startup.
# Build from the repository root:  docker build -f docker/migrate/Dockerfile .
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
# The bundle is self-contained, so it must match the platform it RUNS on: x64 on CI runners, arm64 on ARM
# machines (Apple Silicon, Windows on ARM). TARGETARCH is set by BuildKit. Found in TSK-06 on an arm64 host.
ARG TARGETARCH
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY .config/ .config/
COPY src/ src/
RUN dotnet tool restore
RUN case "$TARGETARCH" in \
      amd64) rid=linux-musl-x64 ;; \
      arm64) rid=linux-musl-arm64 ;; \
      *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet ef migrations bundle \
      --project src/UrlShortener.Core --startup-project src/UrlShortener.Core \
      --context LinksDbContext --self-contained -r "$rid" -o /out/efbundle-links

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine AS runtime
WORKDIR /app
COPY --from=build /out/efbundle-links ./
USER app
# No --connection argument: the design-time factory reads LINKS_MIGRATOR_CONNECTION, so the password
# never appears on a command line.
ENTRYPOINT ["./efbundle-links"]
