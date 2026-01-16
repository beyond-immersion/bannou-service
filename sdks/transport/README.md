# BeyondImmersion.Bannou.Transport

Transport layer for Bannou including UDP transport via LiteNetLib for low-latency game communication.

## Overview

This package provides the network transport layer for Bannou:

- **UDP Transport**: Low-latency unreliable and reliable UDP via LiteNetLib
- **Connection Management**: Client and server transport abstractions
- **Protocol Integration**: Built on top of `BeyondImmersion.Bannou.Protocol`

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.Transport
```

> **Note**: This package is typically installed automatically as a dependency of `BeyondImmersion.Bannou.Client` or `BeyondImmersion.Bannou.Server`.

## Dependencies

- [LiteNetLib](https://github.com/RevenantX/LiteNetLib) - Reliable UDP networking library
- `BeyondImmersion.Bannou.Protocol` - Protocol message types

## License

MIT
