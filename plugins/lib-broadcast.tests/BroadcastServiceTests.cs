using BeyondImmersion.BannouService.Broadcast;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Broadcast.Tests;

/// <summary>
/// Plugin-specific unit tests for BroadcastService.
///
/// NOTE: Constructor validation, configuration instantiation, key builder patterns,
/// hierarchy compliance, and other structural checks are handled centrally by
/// structural-tests/ (auto-discovered via [BannouService] attribute).
/// Only add plugin-specific business logic tests here.
///
/// See: docs/reference/tenets/TESTING-PATTERNS.md
/// </summary>
public class BroadcastServiceTests
{
    // TODO: Add service-specific tests based on schema operations
    // Schema file: ../schemas/broadcast-api.yaml
    //
    // Guidelines:
    // - Use the Capture Pattern for event/state verification (see TESTING-PATTERNS.md)
    // - Verify side effects (saves, events, indices), not just response structure
    // - Keep Arrange < 50% of test code; extract helpers if needed
    // - Use EnumMappingValidator for any SDK boundary enum mappings
}
