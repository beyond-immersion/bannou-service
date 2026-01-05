FROM mcr.microsoft.com/dotnet/sdk:9.0 AS intermediate
COPY ./ /app/

RUN apt-get update && apt-get install -y libxml2-utils && apt-get install -y rsync

# Support build-time service selection
# Usage: docker build --build-arg BANNOU_SERVICES="auth account connect" .
# Default: builds all services if not specified
ARG BANNOU_SERVICES=""

WORKDIR /app

# Build plugins based on BANNOU_SERVICES argument
RUN if [ -z "$BANNOU_SERVICES" ]; then \
        echo "🔧 Building all available services (no BANNOU_SERVICES specified)"; \
        bash scripts/build-service-libs.sh; \
    else \
        echo "🔧 Building specific services: $BANNOU_SERVICES"; \
        bash scripts/build-service-libs.sh $BANNOU_SERVICES; \
    fi

WORKDIR /

RUN dotnet dev-certs https
RUN dotnet publish -o /app/bannou-service/publish -c Release /app/bannou-service/

FROM mcr.microsoft.com/dotnet/aspnet:9.0
COPY --from=intermediate /app/bannou-service/publish /app
COPY --from=intermediate /app/plugins /app/plugins
COPY --from=intermediate /app/plugins/PLUGIN_MANIFEST.txt /PLUGIN_MANIFEST.txt
COPY --from=intermediate /root/.dotnet/corefx/cryptography/x509stores/my/* /root/.dotnet/corefx/cryptography/x509stores/my/
COPY --from=intermediate /root/.dotnet/corefx/cryptography/x509stores/my/* /certificates/

WORKDIR /app
EXPOSE 80
EXPOSE 443

ENV ASPNETCORE_URLS=http://+:80;https://+:443

RUN apt-get update && apt-get install -y curl git libgit2-1.5 ffmpeg

# Configure git to trust all directories (needed for LibGit2Sharp with mounted volumes)
RUN git config --global --add safe.directory '*'

HEALTHCHECK --interval=30s --timeout=3s --retries=5 CMD curl -k --fail https://localhost/health || exit 1

CMD ["dotnet", "bannou-service.dll"]
