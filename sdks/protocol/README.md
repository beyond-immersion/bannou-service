# BeyondImmersion.Bannou.Protocol

Game protocol types for Bannou including MessagePack serialization for high-performance binary messaging.

## Overview

This package provides the binary protocol layer for Bannou game communication:

- **Message Types**: Envelope structures for game protocol messages
- **MessagePack Integration**: High-performance binary serialization
- **Protocol Constants**: Shared protocol version and format identifiers

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.Protocol
```

> **Note**: This package is typically installed automatically as a dependency of `BeyondImmersion.Bannou.Transport`.

## Dependencies

- [MessagePack](https://github.com/MessagePack-CSharp/MessagePack-CSharp) - High-performance binary serialization

## License

MIT
