using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Documentation;

/// <summary>
/// Configuration class for Documentation service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(DocumentationService))]
public class DocumentationServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? ForceServiceId { get; set; }

    /// <summary>
    /// Whether to rebuild search index on service startup
    /// Environment variable: DOCUMENTATION_SEARCH_INDEX_REBUILD_ON_STARTUP
    /// </summary>
    public bool SearchIndexRebuildOnStartup { get; set; } = true;

    /// <summary>
    /// TTL for informal session tracking (24 hours default)
    /// Environment variable: DOCUMENTATION_SESSION_TTL_SECONDS
    /// </summary>
    public int SessionTtlSeconds { get; set; } = 86400;

    /// <summary>
    /// Maximum document content size in bytes (500KB default)
    /// Environment variable: DOCUMENTATION_MAX_CONTENT_SIZE_BYTES
    /// </summary>
    public int MaxContentSizeBytes { get; set; } = 524288;

    /// <summary>
    /// Days before trashcan items are auto-purged
    /// Environment variable: DOCUMENTATION_TRASHCAN_TTL_DAYS
    /// </summary>
    public int TrashcanTtlDays { get; set; } = 7;

    /// <summary>
    /// Maximum characters for voice summaries
    /// Environment variable: DOCUMENTATION_VOICE_SUMMARY_MAX_LENGTH
    /// </summary>
    public int VoiceSummaryMaxLength { get; set; } = 200;

    /// <summary>
    /// TTL for search result caching
    /// Environment variable: DOCUMENTATION_SEARCH_CACHE_TTL_SECONDS
    /// </summary>
    public int SearchCacheTtlSeconds { get; set; } = 300;

    /// <summary>
    /// Default minimum relevance score for search results
    /// Environment variable: DOCUMENTATION_MIN_RELEVANCE_SCORE
    /// </summary>
    public double MinRelevanceScore { get; set; } = 0.3;

    /// <summary>
    /// Maximum search results to return
    /// Environment variable: DOCUMENTATION_MAX_SEARCH_RESULTS
    /// </summary>
    public int MaxSearchResults { get; set; } = 20;

    /// <summary>
    /// Maximum documents per import (0 = unlimited)
    /// Environment variable: DOCUMENTATION_MAX_IMPORT_DOCUMENTS
    /// </summary>
    public int MaxImportDocuments { get; set; } = 0;

    /// <summary>
    /// Enable AI-powered semantic search (future feature)
    /// Environment variable: DOCUMENTATION_AI_ENHANCEMENTS_ENABLED
    /// </summary>
    public bool AiEnhancementsEnabled { get; set; } = false;

    /// <summary>
    /// Model for generating embeddings (when AI enabled)
    /// Environment variable: DOCUMENTATION_AI_EMBEDDINGS_MODEL
    /// </summary>
    public string AiEmbeddingsModel { get; set; } = "";

}
