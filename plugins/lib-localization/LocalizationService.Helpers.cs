using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using System.Text;

namespace BeyondImmersion.BannouService.Localization;

// =============================================================================
// LocalizationService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by LocalizationService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (LocalizationService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ILocalizationService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (LocalizationService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for LocalizationService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class LocalizationService
{
    // Move private/internal helper methods here from LocalizationService.cs
    /// <summary>
    /// Seeds schema-defined categories from LocalizationCategoryDefinitions on startup.
    /// Idempotent — existing categories are skipped silently.
    /// </summary>
    public async Task OnStartAsync(CancellationToken token)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.localization", "LocalizationService.OnStartAsync");

        _logger.LogInformation("Seeding schema-defined localization categories");

        foreach (var (code, metadata) in LocalizationCategoryDefinitions.Metadata)
        {
            try
            {
                var existingCodeId = await _categoryCodeStore.GetAsync(BuildCategoryCodeKey(code), token);
                if (existingCodeId != null)
                    continue; // Already seeded — idempotent

                var categoryId = Guid.NewGuid();
                var now = DateTimeOffset.UtcNow;

                var model = new LocalizationCategoryModel
                {
                    CategoryId = categoryId,
                    Code = code,
                    Description = metadata.Description,
                    IsSchemaDefinition = true,
                    ValidationMode = metadata.ValidationMode,
                    DefaultLanguage = _configuration.DefaultLanguage,
                    EntryCount = 0,
                    CreatedAt = now,
                };

                await _categoryStore.SaveAsync(BuildCategoryKey(categoryId), model, cancellationToken: token);
                await _categoryCodeStore.SaveAsync(BuildCategoryCodeKey(code), categoryId.ToString(), cancellationToken: token);

                await _messageBus.PublishLocalizationCategoryCreatedAsync(new LocalizationCategoryCreatedEvent
                {
                    CategoryId = categoryId,
                    Code = code,
                    Description = metadata.Description,
                    IsSchemaDefinition = true,
                    ValidationMode = metadata.ValidationMode,
                    DefaultLanguage = _configuration.DefaultLanguage,
                    EntryCount = 0,
                    CreatedAt = now,
                    UpdatedAt = now,
                }, token);

                _logger.LogInformation("Seeded schema-defined category {Code} with ID {CategoryId}", code, categoryId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed schema-defined category {Code}", code);
            }
        }

        _logger.LogInformation("Schema-defined category seeding complete");
    }
}
