#!/bin/bash

set -e

echo "🔧 Generating Bannou SDK packages (Server + Client)..."

# =============================================================================
# DISCOVERY: Find all generated files
# =============================================================================

# Clients and models are in bannou-service/Generated/
CLIENT_FILES=($(find ./bannou-service/Generated/Clients -name "*Client.cs" 2>/dev/null || true))
MODEL_FILES=($(find ./bannou-service/Generated/Models -name "*Models.cs" 2>/dev/null || true))
EVENT_FILES=($(find ./bannou-service/Generated/Events -name "*Events*.cs" 2>/dev/null || true))

# Include common events from bannou-service/Generated/
COMMON_EVENT_FILES=($(find ./bannou-service/Generated -maxdepth 1 -name "*Events*.cs" 2>/dev/null || true))
EVENT_FILES=("${EVENT_FILES[@]}" "${COMMON_EVENT_FILES[@]}")

# Include client events from lib-* service plugins (e.g., VoiceClientEventsModels.cs, GameSessionClientEventsModels.cs)
# These are WebSocket push events that clients need to receive and handle
LIB_CLIENT_EVENT_FILES=($(find ./lib-*/Generated -name "*ClientEventsModels.cs" 2>/dev/null || true))
EVENT_FILES=("${EVENT_FILES[@]}" "${LIB_CLIENT_EVENT_FILES[@]}")

echo "Found ${#CLIENT_FILES[@]} client files, ${#MODEL_FILES[@]} model files, ${#EVENT_FILES[@]} event files"

# =============================================================================
# SERVER SDK: Bannou.SDK (Full SDK with ServiceClients)
# For game servers and internal services that need Dapr service-to-service calls
# =============================================================================

SERVER_SDK_DIR="Bannou.SDK"
SERVER_SDK_PROJECT="$SERVER_SDK_DIR/Bannou.SDK.csproj"

mkdir -p "$SERVER_SDK_DIR"

cat > "$SERVER_SDK_PROJECT" << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net6.0;net8.0;net9.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>BeyondImmersion.Bannou.SDK</RootNamespace>

    <!-- NuGet Package Metadata -->
    <PackageId>BeyondImmersion.Bannou.SDK</PackageId>
    <Authors>BeyondImmersion</Authors>
    <Description>Server SDK for Bannou service platform with Dapr service clients, models, events, and WebSocket protocol support. Use this for game servers and internal services.</Description>
    <PackageTags>bannou;microservices;websocket;dapr;server;sdk;service-client</PackageTags>
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
    <PackageReference Include="System.Net.WebSockets.Client" Version="4.3.2" />
    <PackageReference Include="Dapr.Client" Version="1.15.1" />
  </ItemGroup>

EOF

# Add generated files using MSBuild targets
echo '  <!-- Generated Files via MSBuild Target (VS-Compatible) -->' >> "$SERVER_SDK_PROJECT"
echo '  <Target Name="IncludeGeneratedFiles" BeforeTargets="BeforeBuild">' >> "$SERVER_SDK_PROJECT"
echo '    <ItemGroup>' >> "$SERVER_SDK_PROJECT"

# Track included files to avoid duplicates
declare -A SERVER_INCLUDED_FILES

# Add CommonClientEventsModels.cs first (contains BaseClientEvent base class)
if [ -f "./bannou-service/Generated/CommonClientEventsModels.cs" ]; then
    echo "      <Compile Include=\"../bannou-service/Generated/CommonClientEventsModels.cs\" />" >> "$SERVER_SDK_PROJECT"
    SERVER_INCLUDED_FILES["./bannou-service/Generated/CommonClientEventsModels.cs"]=1
fi

# Add ALL client files (ServiceClients) - SERVER SDK INCLUDES THESE
for file in "${CLIENT_FILES[@]}"; do
    if [ -z "${SERVER_INCLUDED_FILES[$file]}" ]; then
        rel_path="../${file#./}"
        echo "      <Compile Include=\"$rel_path\" />" >> "$SERVER_SDK_PROJECT"
        SERVER_INCLUDED_FILES[$file]=1
    fi
done

# Add model files (avoid duplicates)
for file in "${MODEL_FILES[@]}"; do
    if [ -z "${SERVER_INCLUDED_FILES[$file]}" ]; then
        rel_path="../${file#./}"
        echo "      <Compile Include=\"$rel_path\" />" >> "$SERVER_SDK_PROJECT"
        SERVER_INCLUDED_FILES[$file]=1
    fi
done

# Add event files (avoid duplicates)
for file in "${EVENT_FILES[@]}"; do
    if [ -z "${SERVER_INCLUDED_FILES[$file]}" ]; then
        rel_path="../${file#./}"
        echo "      <Compile Include=\"$rel_path\" />" >> "$SERVER_SDK_PROJECT"
        SERVER_INCLUDED_FILES[$file]=1
    fi
done

cat >> "$SERVER_SDK_PROJECT" << 'EOF'
    </ItemGroup>
  </Target>

  <!-- Service Client Infrastructure (for Dapr service-to-service calls) -->
  <ItemGroup>
    <Compile Include="../bannou-service/ServiceClients/IDaprClient.cs" Condition="Exists('../bannou-service/ServiceClients/IDaprClient.cs')" />
    <Compile Include="../bannou-service/ServiceClients/IServiceClient.cs" Condition="Exists('../bannou-service/ServiceClients/IServiceClient.cs')" />
    <Compile Include="../bannou-service/ServiceClients/DaprServiceClientBase.cs" Condition="Exists('../bannou-service/ServiceClients/DaprServiceClientBase.cs')" />
    <Compile Include="../bannou-service/Services/IServiceAppMappingResolver.cs" Condition="Exists('../bannou-service/Services/IServiceAppMappingResolver.cs')" />
    <Compile Include="../bannou-service/ApiException.cs" Condition="Exists('../bannou-service/ApiException.cs')" />
    <Compile Include="../bannou-service/Configuration/BannouJson.cs" Condition="Exists('../bannou-service/Configuration/BannouJson.cs')" />
  </ItemGroup>

  <!-- WebSocket Binary Protocol -->
  <ItemGroup>
    <Compile Include="../lib-connect/Protocol/BinaryMessage.cs" Condition="Exists('../lib-connect/Protocol/BinaryMessage.cs')" />
    <Compile Include="../lib-connect/Protocol/MessageFlags.cs" Condition="Exists('../lib-connect/Protocol/MessageFlags.cs')" />
    <Compile Include="../lib-connect/Protocol/NetworkByteOrder.cs" Condition="Exists('../lib-connect/Protocol/NetworkByteOrder.cs')" />
    <Compile Include="../lib-connect/Protocol/GuidGenerator.cs" Condition="Exists('../lib-connect/Protocol/GuidGenerator.cs')" />
    <Compile Include="../lib-connect/Protocol/ConnectionState.cs" Condition="Exists('../lib-connect/Protocol/ConnectionState.cs')" />
  </ItemGroup>

  <!-- SDK Source Files (WebSocket client, etc.) -->
  <ItemGroup>
    <Compile Include="../sdk-sources/**/*.cs" />
  </ItemGroup>

</Project>
EOF

# Create Server SDK README
cat > "$SERVER_SDK_DIR/README.md" << 'EOF'
# Bannou Server SDK

Server SDK for the Bannou service platform. Use this for **game servers** and **internal services** that need:
- Dapr service-to-service calls via generated ServiceClients
- WebSocket connections for event reception
- Full access to all Bannou APIs

## When to Use This SDK

- **Game Servers** (e.g., Stride3D game server) that need to call Bannou services directly
- **Internal Microservices** that communicate via Dapr service invocation
- **External Servers** that connect via WebSocket for event reception

## Features

- ✅ Type-safe service clients (`AccountsClient`, `AuthClient`, etc.)
- ✅ Request/Response models for all APIs
- ✅ Event models for pub/sub messaging
- ✅ WebSocket binary protocol (31-byte header)
- ✅ `BannouClient` for WebSocket connections
- ✅ Dapr service routing with dynamic app-id resolution

## Usage

### Using Service Clients (Dapr)

```csharp
using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Auth;

// Service clients use Dapr for routing
var accountsClient = new AccountsClient();
var authClient = new AuthClient();

var response = await accountsClient.CreateAccountAsync(new CreateAccountRequest
{
    Username = "user",
    Password = "password"
});
```

### Using WebSocket Connection

```csharp
using BeyondImmersion.Bannou.Client.SDK;

var client = new BannouClient("wss://connect.bannou.example.com/ws");
await client.ConnectAsync();

// Receive events via WebSocket
client.OnEvent += (sender, e) => Console.WriteLine($"Event: {e.EventType}");
```

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.SDK
```

## See Also

- **Bannou.Client.SDK** - For game clients that only use WebSocket (no Dapr dependency)
EOF

echo "✅ Server SDK: $SERVER_SDK_PROJECT"

# =============================================================================
# CLIENT SDK: Bannou.Client.SDK (No ServiceClients - WebSocket only)
# For game clients that only communicate via WebSocket
# =============================================================================

CLIENT_SDK_DIR="Bannou.Client.SDK"
CLIENT_SDK_PROJECT="$CLIENT_SDK_DIR/Bannou.Client.SDK.csproj"

mkdir -p "$CLIENT_SDK_DIR"

cat > "$CLIENT_SDK_PROJECT" << 'EOF'
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
    <Description>Client SDK for Bannou service platform with models, events, and WebSocket protocol support. For game clients - no Dapr dependency.</Description>
    <PackageTags>bannou;microservices;websocket;client;sdk;game-client</PackageTags>
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

  <!-- NO Dapr.Client dependency - this is for game clients only -->
  <ItemGroup>
    <PackageReference Include="System.Net.WebSockets.Client" Version="4.3.2" />
  </ItemGroup>

EOF

# Add generated files using MSBuild targets
echo '  <!-- Generated Files via MSBuild Target (VS-Compatible) -->' >> "$CLIENT_SDK_PROJECT"
echo '  <Target Name="IncludeGeneratedFiles" BeforeTargets="BeforeBuild">' >> "$CLIENT_SDK_PROJECT"
echo '    <ItemGroup>' >> "$CLIENT_SDK_PROJECT"

# NOTE: NO CLIENT FILES - Client SDK excludes ServiceClients
# Only models and events are included

# Track included files to avoid duplicates
declare -A INCLUDED_FILES

# Add CommonClientEventsModels.cs first (contains BaseClientEvent base class)
if [ -f "./bannou-service/Generated/CommonClientEventsModels.cs" ]; then
    echo "      <Compile Include=\"../bannou-service/Generated/CommonClientEventsModels.cs\" />" >> "$CLIENT_SDK_PROJECT"
    INCLUDED_FILES["./bannou-service/Generated/CommonClientEventsModels.cs"]=1
fi

# Add model files
for file in "${MODEL_FILES[@]}"; do
    if [ -z "${INCLUDED_FILES[$file]}" ]; then
        rel_path="../${file#./}"
        echo "      <Compile Include=\"$rel_path\" />" >> "$CLIENT_SDK_PROJECT"
        INCLUDED_FILES[$file]=1
    fi
done

# Add event files (avoid duplicates)
for file in "${EVENT_FILES[@]}"; do
    if [ -z "${INCLUDED_FILES[$file]}" ]; then
        rel_path="../${file#./}"
        echo "      <Compile Include=\"$rel_path\" />" >> "$CLIENT_SDK_PROJECT"
        INCLUDED_FILES[$file]=1
    fi
done

cat >> "$CLIENT_SDK_PROJECT" << 'EOF'
    </ItemGroup>
  </Target>

  <!-- Shared Infrastructure (no Dapr dependency) -->
  <ItemGroup>
    <Compile Include="../bannou-service/ApiException.cs" Condition="Exists('../bannou-service/ApiException.cs')" />
    <Compile Include="../bannou-service/Configuration/BannouJson.cs" Condition="Exists('../bannou-service/Configuration/BannouJson.cs')" />
  </ItemGroup>

  <!-- WebSocket Binary Protocol -->
  <ItemGroup>
    <Compile Include="../lib-connect/Protocol/BinaryMessage.cs" Condition="Exists('../lib-connect/Protocol/BinaryMessage.cs')" />
    <Compile Include="../lib-connect/Protocol/MessageFlags.cs" Condition="Exists('../lib-connect/Protocol/MessageFlags.cs')" />
    <Compile Include="../lib-connect/Protocol/NetworkByteOrder.cs" Condition="Exists('../lib-connect/Protocol/NetworkByteOrder.cs')" />
    <Compile Include="../lib-connect/Protocol/GuidGenerator.cs" Condition="Exists('../lib-connect/Protocol/GuidGenerator.cs')" />
    <Compile Include="../lib-connect/Protocol/ConnectionState.cs" Condition="Exists('../lib-connect/Protocol/ConnectionState.cs')" />
  </ItemGroup>

  <!-- SDK Source Files (WebSocket client, etc.) -->
  <ItemGroup>
    <Compile Include="../sdk-sources/**/*.cs" />
  </ItemGroup>

</Project>
EOF

# Create Client SDK README
cat > "$CLIENT_SDK_DIR/README.md" << 'EOF'
# Bannou Client SDK

Lightweight client SDK for the Bannou service platform. Use this for **game clients** that:
- Connect via WebSocket only
- Don't need Dapr service-to-service calls
- Want minimal dependencies

## When to Use This SDK

- **Game Clients** (e.g., Stride3D client, Unity client) that connect via WebSocket
- **Web Clients** that use WebSocket for real-time communication
- Any client that communicates through the Connect service gateway

## What's NOT Included

This SDK **does not include**:
- ❌ ServiceClients (`AccountsClient`, `AuthClient`, etc.) - use `Bannou.SDK` if you need these
- ❌ `Dapr.Client` dependency
- ❌ Dapr service infrastructure

## Features

- ✅ Request/Response models for all APIs
- ✅ Event models for pub/sub messaging
- ✅ WebSocket binary protocol (31-byte header)
- ✅ `BannouClient` for WebSocket connections
- ✅ Zero Dapr dependencies (smaller package)

## Usage

```csharp
using BeyondImmersion.Bannou.Client.SDK;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Accounts;

// Connect via WebSocket
var client = new BannouClient("wss://connect.bannou.example.com/ws");
await client.ConnectAsync();

// Use models for requests/responses
var loginRequest = new LoginRequest
{
    Username = "user",
    Password = "password"
};

// Send via WebSocket binary protocol
var response = await client.SendRequestAsync<LoginRequest, LoginResponse>(
    serviceName: "auth",
    request: loginRequest
);
```

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.Client.SDK
```

## See Also

- **Bannou.SDK** - For game servers that need Dapr service clients
EOF

echo "✅ Client SDK: $CLIENT_SDK_PROJECT"

# =============================================================================
# SUMMARY
# =============================================================================

echo ""
echo "✅ SDK generation completed successfully!"
echo ""
echo "   Server SDK (Bannou.SDK):       ${#CLIENT_FILES[@]} clients, ${#MODEL_FILES[@]} models, ${#EVENT_FILES[@]} events"
echo "   Client SDK (Bannou.Client.SDK): 0 clients, ${#MODEL_FILES[@]} models, ${#EVENT_FILES[@]} events"
echo ""
echo "   Server SDK includes: ServiceClients + Models + Events + Protocol + BannouClient + Dapr"
echo "   Client SDK includes: Models + Events + Protocol + BannouClient (NO ServiceClients, NO Dapr)"
