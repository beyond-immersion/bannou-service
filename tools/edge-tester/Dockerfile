# HTTP integration testing container based on existing bannou:latest image
# This gives us access to all built service libraries and dependencies
FROM bannou:latest AS bannou-base

# Switch to build image to compile the edge-tester application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Build argument to control SDK source (current vs published)
# Default to local SDK source - edge-tester/Dockerfile.dockerignore includes sdks/
# Switch to UsePublishedSDK=true once SDK is published to NuGet
ARG UsePublishedSDK=false

# Copy source code and solution
WORKDIR /app
COPY . .

# Build the edge-tester application in Release configuration
# UsePublishedSDK=true uses NuGet package, false uses project reference
RUN dotnet build tools/edge-tester --configuration Release -p:UsePublishedSDK=${UsePublishedSDK}

# Build TypeScript test harness
FROM node:22-slim AS ts-build

WORKDIR /app

# Copy entire TypeScript SDK workspace (includes package.json, node_modules structure)
# This is needed because the SDK uses a monorepo structure with hoisted dependencies
COPY sdks/typescript ./sdks/typescript

# Copy harness source
COPY tools/edge-tester/TypeScript ./tools/edge-tester/TypeScript

# Install dependencies and build the harness
# Use --install-links to copy file: dependencies instead of symlinking them
# This ensures the packages are self-contained after Docker COPY
WORKDIR /app/tools/edge-tester/TypeScript
RUN npm install --install-links && npm run build

# Runtime image based on ASP.NET Core runtime (needed for bannou-service dependencies)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Copy Node.js from the ts-build stage (more reliable than apt-get for version consistency)
COPY --from=ts-build /usr/local/bin/node /usr/local/bin/node
COPY --from=ts-build /usr/local/lib/node_modules /usr/local/lib/node_modules

# Copy the built edge-tester application
WORKDIR /app
COPY --from=build /app/tools/edge-tester/bin/Release/net9.0/ .

# Copy any service libraries that edge-tester might need from the bannou base image
COPY --from=bannou-base /app/plugins/ ./plugins/

# Copy the built TypeScript harness (includes node_modules with bundled dependencies)
# Copy to ./TypeScript which is the first path FindHarnessPath() checks: {currentDir}/TypeScript
# Docker COPY dereferences symlinks, so the SDK files are embedded in node_modules
COPY --from=ts-build /app/tools/edge-tester/TypeScript ./TypeScript

# Set the entrypoint to run edge-tester in daemon mode
ENTRYPOINT ["dotnet", "edge-tester.dll"]
