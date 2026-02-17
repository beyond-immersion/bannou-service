# BeyondImmersion.Bannou.Core

Core types for Bannou SDKs including JSON serialization settings, event infrastructure, and shared exceptions.

## Overview

This is a foundation package used by all other Bannou SDK packages. It provides:

- **BannouJson**: Standardized JSON serialization with case-insensitive property matching and proper enum handling
- **DiscriminatedRecordConverter**: Generic polymorphic JSON converter for discriminated record hierarchies
- **Event Infrastructure**: Base types for the event-driven architecture
- **Shared Exceptions**: Common exception types used across SDKs

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.Core
```

> **Note**: This package is typically installed automatically as a dependency of higher-level packages like `BeyondImmersion.Bannou.Client` or `BeyondImmersion.Bannou.Server`.

## Usage

### JSON Serialization

```csharp
using BeyondImmersion.Bannou.Core;

// Serialize with Bannou conventions
var json = BannouJson.Serialize(myObject);

// Deserialize
var obj = BannouJson.Deserialize<MyType>(json);
```

### Discriminated Record Converter

For abstract record hierarchies that need polymorphic JSON round-trip, subclass `DiscriminatedRecordConverter<T>` with a type map:

```csharp
using System.Text.Json.Serialization;
using BeyondImmersion.Bannou.Core;

// 1. Define the record hierarchy with a [JsonConverter] attribute
[JsonConverter(typeof(ShapeConverter))]
public abstract record Shape(string Type);
public sealed record Circle(float Radius) : Shape("circle");
public sealed record Square(float Side) : Shape("square");

// 2. Subclass the converter with the discriminator property name and type map
public class ShapeConverter() : DiscriminatedRecordConverter<Shape>("type",
    new Dictionary<string, Type>
    {
        ["circle"] = typeof(Circle),
        ["square"] = typeof(Square),
    });

// 3. Serialize and deserialize via BannouJson -- converters activate automatically
Shape shape = new Circle(5.0f);
var json = BannouJson.Serialize(shape);       // {"Type":"circle","Radius":5}
var back = BannouJson.Deserialize<Shape>(json); // Circle { Radius = 5 }
```

The base converter handles discriminator property lookup (case-insensitive), concrete type resolution, recursion-safe (de)serialization, and options caching. Discriminator values are matched exactly (case-sensitive) since they are machine identifiers.

## License

MIT
