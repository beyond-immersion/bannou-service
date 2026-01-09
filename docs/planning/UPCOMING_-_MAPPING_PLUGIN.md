# Mapping Plugin - Status Summary

> **Status**: IMPLEMENTED (Core features complete)
> **Updated**: 2026-01-08

## Implementation Status

The Mapping System is substantially implemented. Core functionality documented in the guide is working.

| Feature | Status |
|---------|--------|
| Schema definitions | COMPLETE |
| Authority model | COMPLETE |
| Channel lifecycle | COMPLETE |
| RPC publishing | COMPLETE |
| Event ingest | COMPLETE |
| Spatial queries | COMPLETE |
| Affordance queries | COMPLETE |
| Authoring APIs | COMPLETE |
| Large payload handling | COMPLETE |
| Event aggregation | COMPLETE |

---

## Reference Documentation

- **Mapping System**: [`docs/guides/MAPPING_SYSTEM.md`](../guides/MAPPING_SYSTEM.md) - Complete guide covering:
  - Authority model and channel lifecycle
  - Map kinds (static_geometry, hazards, navigation, etc.)
  - Event architecture and subscription patterns
  - Affordance system for spatial intelligence
  - Actor integration (NPC perception vs Event actor subscription)
  - ABML integration for spatial behaviors
  - API reference and best practices

---

## Code Locations

| Component | Location |
|-----------|----------|
| MappingService | `lib-mapping/MappingService.cs` (3072 lines) |
| Event Handlers | `lib-mapping/MappingServiceEvents.cs` |
| API Schema | `schemas/mapping-api.yaml` |
| Event Schema | `schemas/mapping-events.yaml` |
| Configuration | `schemas/mapping-configuration.yaml` |

---

## Future Enhancements (Not Started)

These were identified as "Phase 7" advanced features but are not blocking:

- [ ] Cross-region queries (queries spanning multiple regions)
- [ ] Additional affordance types beyond well-known set
- [ ] Map derivative caching optimizations

---

*Original ~29K-token planning document condensed 2026-01-08. Design details now in MAPPING_SYSTEM.md guide and implementation.*
