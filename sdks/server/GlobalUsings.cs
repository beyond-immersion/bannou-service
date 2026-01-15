// =============================================================================
// Global Using Aliases for SDK
// =============================================================================
// These aliases map the core namespace types to the legacy namespaces expected
// by generated NSwag code included from bannou-service.
// =============================================================================

// Event base types - expected by generated event models
global using IBannouEvent = BeyondImmersion.Bannou.Core.IBannouEvent;
global using BaseServiceEvent = BeyondImmersion.Bannou.Core.BaseServiceEvent;
global using BaseClientEvent = BeyondImmersion.Bannou.Core.BaseClientEvent;

// JSON utilities
global using BannouJson = BeyondImmersion.Bannou.Core.BannouJson;
global using BannouJsonExtensions = BeyondImmersion.Bannou.Core.BannouJsonExtensions;

// API exceptions
global using ApiException = BeyondImmersion.Bannou.Core.ApiException;
