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
RUN dotnet build edge-tester --configuration Release -p:UsePublishedSDK=${UsePublishedSDK}

# Runtime image based on ASP.NET Core runtime (needed for bannou-service dependencies)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Copy the built edge-tester application
WORKDIR /app
COPY --from=build /app/edge-tester/bin/Release/net9.0/ .

# Copy any service libraries that edge-tester might need from the bannou base image
COPY --from=bannou-base /app/plugins/ ./plugins/

# Set the entrypoint to run edge-tester in daemon mode
ENTRYPOINT ["dotnet", "edge-tester.dll"]
