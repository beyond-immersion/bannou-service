namespace BeyondImmersion.BannouService.Localization;

/// <summary>
/// Internal storage model for a localization category.
/// Stored in MySQL via localization-category-store with key pattern category:{categoryId}.
/// </summary>
public class LocalizationCategoryModel
{
    /// <summary>Unique identifier for the category.</summary>
    public Guid CategoryId { get; set; }

    /// <summary>Unique human-readable code for the category (e.g., "items", "quests").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable description of the category's purpose.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Whether this category was seeded from localization-categories.yaml (immutable, cannot be deleted).</summary>
    public bool IsSchemaDefinition { get; set; }

    /// <summary>Key validation behavior for this category.</summary>
    public ValidationMode ValidationMode { get; set; }

    /// <summary>Default language for this category (BCP 47 tag).</summary>
    public string DefaultLanguage { get; set; } = string.Empty;

    /// <summary>Current count of translation entries in this category.</summary>
    public int EntryCount { get; set; }

    /// <summary>Language of the most recently modified entry (null if no entries).</summary>
    public string? LastEntryUpdateLanguage { get; set; }

    /// <summary>When this category was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this category was last updated (null if never updated).</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal storage model for a localization translation entry.
/// Stored in MySQL via localization-entry-store with key pattern entry:{categoryId}:{language}:{key}.
/// </summary>
public class LocalizationEntryModel
{
    /// <summary>Unique identifier for this entry.</summary>
    public Guid EntryId { get; set; }

    /// <summary>Category this entry belongs to.</summary>
    public Guid CategoryId { get; set; }

    /// <summary>Translation key within the category.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>BCP 47 language tag.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Translated text content.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>IPA pronunciation for TTS/PLS export (null if not applicable).</summary>
    public string? Pronunciation { get; set; }

    /// <summary>Ruby annotations for CJK text (null if not applicable).</summary>
    public RubyAnnotation[]? Ruby { get; set; }

    /// <summary>When this entry was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Partial class for state store key building helpers.
/// </summary>
public partial class LocalizationService
{
    private const string CATEGORY_KEY_PREFIX = "category:";
    private const string CATEGORY_CODE_KEY_PREFIX = "category-code:";
    private const string ENTRY_KEY_PREFIX = "entry:";
    private const string COMPILED_CACHE_KEY_PREFIX = "compiled:";
    private const string COMPILED_ALL_CACHE_KEY_PREFIX = "compiled:all:";

    #region Key Building Helpers

    /// <summary>Builds a state store key for a category by its ID.</summary>
    internal static string BuildCategoryKey(Guid categoryId)
        => $"{CATEGORY_KEY_PREFIX}{categoryId}";

    /// <summary>Builds a reverse-index key for category code uniqueness lookup.</summary>
    internal static string BuildCategoryCodeKey(string code)
        => $"{CATEGORY_CODE_KEY_PREFIX}{code}";

    /// <summary>Builds a state store key for a translation entry.</summary>
    internal static string BuildEntryKey(Guid categoryId, string language, string key)
        => $"{ENTRY_KEY_PREFIX}{categoryId}:{language}:{key}";

    /// <summary>Builds a compiled cache key for a specific category and language.</summary>
    internal static string BuildCompiledCacheKey(Guid categoryId, string language)
        => $"{COMPILED_CACHE_KEY_PREFIX}{categoryId}:{language}";

    /// <summary>Builds a compiled cache key for all categories in a language.</summary>
    internal static string BuildAllCompiledCacheKey(string language)
        => $"{COMPILED_ALL_CACHE_KEY_PREFIX}{language}";

    #endregion
}
