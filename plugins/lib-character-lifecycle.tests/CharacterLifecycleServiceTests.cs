using BeyondImmersion.BannouService.CharacterLifecycle;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.CharacterLifecycle.Tests;

/// <summary>
/// Plugin-specific unit tests for CharacterLifecycleService.
///
/// NOTE: Constructor validation, configuration instantiation, key builder patterns,
/// hierarchy compliance, and other structural checks are handled centrally by
/// structural-tests/ (auto-discovered via [BannouService] attribute).
/// Only add plugin-specific business logic tests here.
///
/// See: docs/reference/tenets/TESTING-PATTERNS.md
/// </summary>
public class CharacterLifecycleServiceTests
{
    // TODO: Add service-specific tests based on schema operations
    // Schema file: ../schemas/character-lifecycle-api.yaml
    //
    // Guidelines:
    // - Use the Capture Pattern for event/state verification (see TESTING-PATTERNS.md)
    // - Verify side effects (saves, events, indices), not just response structure
    // - Keep Arrange < 50% of test code; extract helpers if needed
    // - Use EnumMappingValidator for any SDK boundary enum mappings
}
