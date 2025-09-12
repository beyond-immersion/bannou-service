#!/bin/bash

set -e

echo "🔧 Generating Bannou Client SDK project..."

# Define paths
SDK_DIR="Bannou.Client.SDK"
SDK_PROJECT="$SDK_DIR/Bannou.Client.SDK.csproj"

# Create SDK directory if it doesn't exist
mkdir -p "$SDK_DIR"

# Find all generated client files and models
CLIENT_FILES=($(find . -path "./lib-*/Generated/*Client.cs" 2>/dev/null || true))
MODEL_FILES=($(find . -path "./lib-*/Generated/*Models.cs" 2>/dev/null || true))
EVENT_FILES=($(find . -path "./lib-*/Generated/*Events*.cs" 2>/dev/null || true))

echo "Found ${#CLIENT_FILES[@]} client files, ${#MODEL_FILES[@]} model files, ${#EVENT_FILES[@]} event files"

# Generate the project file
cat > "$SDK_PROJECT" << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net6.0;net8.0;net9.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>BeyondImmersion.Bannou.Client.SDK</RootNamespace>

    <!-- NuGet Package Metadata -->
    <PackageId>BeyondImmersion.Bannou.Client.SDK</PackageId>
    <Authors>BeyondImmersion</Authors>
    <Description>Client SDK for Bannou service platform with generated service clients, models, and WebSocket protocol support</Description>
    <PackageTags>bannou;microservices;websocket;dapr;client;sdk</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/BeyondImmersion/bannou</PackageProjectUrl>
    <RepositoryUrl>https://github.com/BeyondImmersion/bannou</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  </PropertyGroup>

  <PropertyGroup>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <DocumentationFile>$(OutputPath)$(AssemblyName).xml</DocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="System.Net.WebSockets.Client" Version="4.3.2" />
    <PackageReference Include="Dapr.Client" Version="1.15.1" />
  </ItemGroup>

EOF

# Add generated files using MSBuild targets (VS-safe approach)
echo '  <!-- Generated Files via MSBuild Target (VS-Compatible) -->' >> "$SDK_PROJECT"
echo '  <Target Name="IncludeGeneratedFiles" BeforeTargets="BeforeBuild">' >> "$SDK_PROJECT"
echo '    <ItemGroup>' >> "$SDK_PROJECT"

# Add client files
for file in "${CLIENT_FILES[@]}"; do
    # Convert to relative path from SDK directory
    rel_path="../${file#./}"
    echo "      <Compile Include=\"$rel_path\" />" >> "$SDK_PROJECT"
done

# Add model files
for file in "${MODEL_FILES[@]}"; do
    rel_path="../${file#./}"
    echo "      <Compile Include=\"$rel_path\" />" >> "$SDK_PROJECT"
done

# Add event files
for file in "${EVENT_FILES[@]}"; do
    rel_path="../${file#./}"
    echo "      <Compile Include=\"$rel_path\" />" >> "$SDK_PROJECT"
done

# Close the target and project
cat >> "$SDK_PROJECT" << 'EOF'
    </ItemGroup>
  </Target>

  <!-- Include only client-safe files from ServiceClients -->
  <ItemGroup>
    <Compile Include="../bannou-service/ServiceClients/DaprServiceClientBase.cs" Condition="Exists('../bannou-service/ServiceClients/DaprServiceClientBase.cs')" />
    <Compile Include="../bannou-service/ServiceClients/IServiceAppMappingResolver.cs" Condition="Exists('../bannou-service/ServiceClients/IServiceAppMappingResolver.cs')" />
    <Compile Include="../bannou-service/ServiceClients/ServiceAppMappingResolver.cs" Condition="Exists('../bannou-service/ServiceClients/ServiceAppMappingResolver.cs')" />
  </ItemGroup>

</Project>
EOF

echo "✅ Generated $SDK_PROJECT with ${#CLIENT_FILES[@]} clients, ${#MODEL_FILES[@]} models, ${#EVENT_FILES[@]} events"

# Create a basic README for the SDK
cat > "$SDK_DIR/README.md" << 'EOF'
# Bannou Client SDK

Auto-generated client SDK for the Bannou service platform.

## Features

- Type-safe service clients generated from OpenAPI schemas
- WebSocket protocol support for real-time communication
- Event models for pub/sub messaging
- Multi-target framework support (.NET 6, 8, 9)

## Usage

```csharp
using BeyondImmersion.Bannou.Client.SDK;

// Use generated service clients
var accountsClient = new AccountsClient();
var authClient = new AuthClient();

// Create requests with generated models
var request = new CreateAccountRequest
{
    Username = "user",
    Password = "password"
};

var response = await accountsClient.CreateAccountAsync(request);
```

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.Client.SDK
```

This package is automatically updated when the Bannou service definitions change.
EOF

echo "✅ Client SDK generation completed successfully!"
