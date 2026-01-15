// =============================================================================
// Global Using Aliases for Backwards Compatibility
// =============================================================================
// These aliases map the new BeyondImmersion.Bannou.Core namespace to the legacy
// BeyondImmersion.BannouService namespaces used by generated NSwag code.
//
// The canonical types now live in sdks/core/ with namespace BeyondImmersion.Bannou.Core.
// These aliases ensure generated code that uses the old namespace continues to work.
// =============================================================================

// Event base types - legacy namespace: BeyondImmersion.BannouService.Events
// API exceptions - legacy namespace: BeyondImmersion.BannouService
global using ApiException = BeyondImmersion.Bannou.Core.ApiException;
// JSON utilities - legacy namespace: BeyondImmersion.BannouService.Configuration
global using BannouJson = BeyondImmersion.Bannou.Core.BannouJson;
global using BannouJsonExtensions = BeyondImmersion.Bannou.Core.BannouJsonExtensions;
// Client event base - legacy namespace: BeyondImmersion.BannouService.ClientEvents
global using BaseClientEvent = BeyondImmersion.Bannou.Core.BaseClientEvent;
global using BaseServiceEvent = BeyondImmersion.Bannou.Core.BaseServiceEvent;
global using IBannouEvent = BeyondImmersion.Bannou.Core.IBannouEvent;
// Note: ApiException<T> cannot be aliased directly due to C# limitations,
// but since it inherits from ApiException, most usage will work.
