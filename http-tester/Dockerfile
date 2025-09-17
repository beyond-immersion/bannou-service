# HTTP integration testing container based on existing bannou:latest image
# This gives us access to all built service libraries and dependencies
FROM bannou:latest AS bannou-base

# Switch to build image to compile the http-tester application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Copy source code and solution
WORKDIR /app
COPY . .

# Build the http-tester application in Release configuration
RUN dotnet build http-tester --configuration Release

# Runtime image based on ASP.NET Core runtime (needed for bannou-service dependencies)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Copy the built http-tester application
WORKDIR /app
COPY --from=build /app/http-tester/bin/Release/net9.0/ .

# Copy any service libraries that http-tester might need from the bannou base image
COPY --from=bannou-base /app/plugins/ ./plugins/

# Set the entrypoint to run http-tester in daemon mode
ENTRYPOINT ["dotnet", "http-tester.dll"]
