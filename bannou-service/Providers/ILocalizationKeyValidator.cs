// =============================================================================
// Localization Key Validator Interface
// Enables dependency inversion for cross-layer localization key validation.
// Localization (L1) implements this; L2+ services discover via DI.
// =============================================================================

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Validator interface for checking localization key existence at entity creation time.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables the dependency inversion pattern for localization key validation:
/// </para>
/// <list type="bullet">
///   <item>Interface lives in <c>bannou-service/Providers/</c> (shared project)</item>
///   <item>lib-localization (L1) implements the validator and registers as Scoped</item>
///   <item>L2+ services discover via <c>IEnumerable&lt;ILocalizationKeyValidator&gt;</c> constructor injection</item>
///   <item>When lib-localization is not loaded, <c>IEnumerable</c> is empty — validation is silently skipped</item>
/// </list>
/// <para>
/// <b>DISTRIBUTED SAFETY — ALWAYS SAFE:</b> This is a pull-based Provider pattern.
/// The validator reads from distributed state (MySQL/Redis) so all nodes see
/// consistent data. Safe in all deployment modes.
/// </para>
/// <para>
/// <b>Category Argument</b>: Callers MUST pass a <c>LocalizationCategoryDefinitions</c>
/// constant as the <c>categoryCode</c> argument. A structural test enforces this —
/// hardcoded category strings are forbidden.
/// </para>
/// <para>
/// <b>Validation Modes</b> (configured per category in <c>schemas/localization-categories.yaml</c>):
/// </para>
/// <list type="bullet">
///   <item><b>None</b>: Always returns true (no checking)</item>
///   <item><b>WarnOnMissing</b>: Returns true but logs a warning when key doesn't exist</item>
///   <item><b>RejectOnMissing</b>: Returns false when key doesn't exist (caller returns BadRequest)</item>
/// </list>
/// <para>
/// <b>Example Consumer</b>:
/// </para>
/// <code>
/// public class ItemService : IItemService
/// {
///     private readonly IEnumerable&lt;ILocalizationKeyValidator&gt; _localizationValidators;
///
///     public async Task&lt;(int, CreateItemTemplateResponse?)&gt; CreateTemplateAsync(
///         CreateItemTemplateRequest request, CancellationToken ct)
///     {
///         // Validate localization key if validator is available
///         foreach (var validator in _localizationValidators)
///         {
///             if (!await validator.ValidateLocalizationKeyAsync(
///                 LocalizationCategoryDefinitions.Items,
///                 request.LocalizationKeyPrefix, null, ct))
///             {
///                 return (StatusCodes.Status400BadRequest, null);
///             }
///         }
///         // ... proceed with creation
///     }
/// }
/// </code>
/// </remarks>
public interface ILocalizationKeyValidator
{
    /// <summary>
    /// Validates that a localization key (or key prefix) exists in the specified category.
    /// </summary>
    /// <param name="categoryCode">
    /// The localization category code. MUST be a <c>LocalizationCategoryDefinitions</c> constant.
    /// Structural tests enforce this — hardcoded strings are forbidden.
    /// </param>
    /// <param name="keyPrefix">
    /// The localization key prefix stored on the entity (e.g., "direwolf").
    /// Combined with <paramref name="keyId"/> to form the full key: <c>{keyPrefix}.{keyId}</c>.
    /// </param>
    /// <param name="keyId">
    /// Optional specific suffix (e.g., "name", "description").
    /// When null, validates that the prefix has at least one entry in the default language.
    /// When provided, validates the exact entry <c>{keyPrefix}.{keyId}</c> exists.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the key exists or the category's validation mode is <c>None</c>.
    /// <c>false</c> if the key is missing and the category's validation mode is <c>RejectOnMissing</c>.
    /// </returns>
    ValueTask<bool> ValidateLocalizationKeyAsync(
        string categoryCode,
        string keyPrefix,
        string? keyId,
        CancellationToken ct);
}
