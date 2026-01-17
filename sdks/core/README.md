# BeyondImmersion.Bannou.Core

Core types for Bannou SDKs including JSON serialization settings, event infrastructure, and shared exceptions.

## Overview

This is a foundation package used by all other Bannou SDK packages. It provides:

- **BannouJson**: Standardized JSON serialization with camelCase naming and proper enum handling
- **Event Infrastructure**: Base types for the event-driven architecture
- **Shared Exceptions**: Common exception types used across SDKs

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.Core
```

> **Note**: This package is typically installed automatically as a dependency of higher-level packages like `BeyondImmersion.Bannou.Client` or `BeyondImmersion.Bannou.Server`.

## Usage

```csharp
using BeyondImmersion.Bannou.Core;

// Serialize with Bannou conventions
var json = BannouJson.Serialize(myObject);

// Deserialize
var obj = BannouJson.Deserialize<MyType>(json);
```

## License

MIT
