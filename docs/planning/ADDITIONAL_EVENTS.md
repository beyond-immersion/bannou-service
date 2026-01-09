# Events Gap Analysis - Historical Record

> **Status**: COMPLETE
> **Completed**: 2026-01-07

## Summary

This document tracked the events audit and gap analysis conducted in late 2025 / early 2026.

### Work Completed

1. **Audit of event schemas** - Identified 17 orphaned events (defined but never implemented) across 6 services
2. **Gap analysis** - Identified missing events needed for observability and audit trails
3. **P0/P1 Implementation** - All critical (P0) and high-priority (P1) events implemented:
   - Auth service: 6 audit events (login success/failure, registration, OAuth, Steam, password reset)
   - Asset service: AssetUploadRequestedEvent (processing events deferred until pipeline exists)
   - GameService: Lifecycle events via x-lifecycle pattern
   - Species: SpeciesMergedEvent + SeedSpeciesAsync updates
   - Leaderboard: LeaderboardEntryAddedEvent
   - Achievement: 3 definition lifecycle events

### Reference Documentation

- **Current event inventory**: See `docs/GENERATED-EVENTS.md` (regenerate with `make generate-docs`)
- **Event schema patterns**: See `docs/reference/tenets/FOUNDATION.md` (Tenet 5: Event-Driven Architecture)
- **x-lifecycle pattern**: Documented in generation scripts and schema examples

### Historical Details

The original document contained ~435 lines of detailed tracking tables. This information is now:
- Redundant with generated documentation
- Historical context no longer needed for day-to-day development
- Available in git history if archaeological research is needed

### P2/P3 Events (Deferred)

Lower-priority events were identified but not implemented. If needed in future:
- Permission service: Matrix change events
- Orchestrator: Additional lifecycle events
- Documentation service: Operational events (bulk update/delete, archive restore)
- Actor service: Instance lifecycle events

These can be added when specific observability needs arise.

---

*Original document archived to git history 2026-01-08*
