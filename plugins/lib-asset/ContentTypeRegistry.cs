namespace BeyondImmersion.BannouService.Asset;

/// <summary>
/// Central registry for content type (MIME type) management.
/// Consolidates all MIME type logic for the Asset service, with configuration-based extensibility.
/// </summary>
public sealed class ContentTypeRegistry : IContentTypeRegistry
{
    // Default audio content types
    private static readonly List<string> DefaultAudioContentTypes = new()
    {
        "audio/mpeg",
        "audio/mp3",
        "audio/wav",
        "audio/x-wav",
        "audio/ogg",
        "audio/opus",
        "audio/flac",
        "audio/aac",
        "audio/webm"
    };

    // Default lossless audio content types
    private static readonly HashSet<string> DefaultLosslessContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/wav",
        "audio/x-wav",
        "audio/flac"
    };

    // Default texture/image content types
    private static readonly List<string> DefaultTextureContentTypes = new()
    {
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp",
        "image/tiff",
        "image/bmp",
        "image/gif"
    };

    // Default 3D model content types
    private static readonly List<string> DefaultModelContentTypes = new()
    {
        "model/gltf+json",
        "model/gltf-binary",
        "application/octet-stream", // Common for .glb files
        "model/obj",
        "model/fbx"
    };

    // Default forbidden content types
    private static readonly HashSet<string> DefaultForbiddenContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/x-executable",
        "application/x-msdos-program",
        "application/x-msdownload",
        "application/x-sharedlib",
        "application/x-shellscript"
    };

    // Default extension-to-content-type mappings
    private static readonly Dictionary<string, string> DefaultExtensionMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        { ".png", "image/png" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".gif", "image/gif" },
        { ".webp", "image/webp" },
        { ".bmp", "image/bmp" },
        { ".tiff", "image/tiff" },
        { ".tif", "image/tiff" },

        // 3D Models
        { ".gltf", "model/gltf+json" },
        { ".glb", "model/gltf-binary" },
        { ".obj", "model/obj" },
        { ".fbx", "model/fbx" },

        // Audio
        { ".mp3", "audio/mpeg" },
        { ".wav", "audio/wav" },
        { ".ogg", "audio/ogg" },
        { ".opus", "audio/opus" },
        { ".flac", "audio/flac" },
        { ".aac", "audio/aac" },
        { ".webm", "audio/webm" },

        // Video
        { ".mp4", "video/mp4" },
        { ".mkv", "video/x-matroska" },
        { ".avi", "video/x-msvideo" },

        // Text/Data
        { ".json", "application/json" },
        { ".xml", "application/xml" },
        { ".txt", "text/plain" },
        { ".csv", "text/csv" },
        { ".yaml", "application/x-yaml" },
        { ".yml", "application/x-yaml" }
    };

    // Runtime collections (populated from defaults + config)
    private readonly HashSet<string> _processableTypes;
    private readonly HashSet<string> _forbiddenTypes;
    private readonly HashSet<string> _losslessAudioTypes;
    private readonly List<string> _audioTypes;
    private readonly List<string> _textureTypes;
    private readonly List<string> _modelTypes;
    private readonly Dictionary<string, string> _extensionMappings;

    /// <summary>
    /// Creates a new ContentTypeRegistry with optional configuration-based extensions.
    /// </summary>
    /// <param name="configuration">Optional configuration for extending defaults.</param>
    public ContentTypeRegistry(AssetServiceConfiguration? configuration = null)
    {
        // Initialize from defaults
        _audioTypes = new List<string>(DefaultAudioContentTypes);
        _textureTypes = new List<string>(DefaultTextureContentTypes);
        _modelTypes = new List<string>(DefaultModelContentTypes);
        _losslessAudioTypes = new HashSet<string>(DefaultLosslessContentTypes, StringComparer.OrdinalIgnoreCase);
        _forbiddenTypes = new HashSet<string>(DefaultForbiddenContentTypes, StringComparer.OrdinalIgnoreCase);
        _extensionMappings = new Dictionary<string, string>(DefaultExtensionMappings, StringComparer.OrdinalIgnoreCase);

        // Build processable types from all processor types
        _processableTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in _audioTypes) _processableTypes.Add(type);
        foreach (var type in _textureTypes) _processableTypes.Add(type);
        foreach (var type in _modelTypes) _processableTypes.Add(type);

        // Apply configuration extensions if provided
        if (configuration != null)
        {
            ApplyConfigurationExtensions(configuration);
        }
    }

    private void ApplyConfigurationExtensions(AssetServiceConfiguration configuration)
    {
        // Add additional processable content types
        if (!string.IsNullOrWhiteSpace(configuration.AdditionalProcessableContentTypes))
        {
            var additionalTypes = configuration.AdditionalProcessableContentTypes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var type in additionalTypes)
            {
                _processableTypes.Add(type);
            }
        }

        // Add additional extension mappings
        if (!string.IsNullOrWhiteSpace(configuration.AdditionalExtensionMappings))
        {
            var mappings = configuration.AdditionalExtensionMappings
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var mapping in mappings)
            {
                var parts = mapping.Split('=', 2);
                if (parts.Length == 2)
                {
                    var ext = parts[0].Trim();
                    var contentType = parts[1].Trim();
                    // Ensure extension has leading dot
                    if (!ext.StartsWith('.'))
                    {
                        ext = "." + ext;
                    }
                    _extensionMappings[ext] = contentType;
                }
            }
        }

        // Add additional forbidden content types
        if (!string.IsNullOrWhiteSpace(configuration.AdditionalForbiddenContentTypes))
        {
            var additionalForbidden = configuration.AdditionalForbiddenContentTypes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var type in additionalForbidden)
            {
                _forbiddenTypes.Add(type);
            }
        }
    }

    /// <inheritdoc />
    public bool IsProcessable(string contentType)
    {
        return _processableTypes.Contains(contentType);
    }

    /// <inheritdoc />
    public bool IsForbidden(string contentType)
    {
        return _forbiddenTypes.Contains(contentType);
    }

    /// <inheritdoc />
    public bool IsAudio(string contentType)
    {
        return _audioTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsTexture(string contentType)
    {
        return _textureTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsModel(string contentType)
    {
        return _modelTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsLosslessAudio(string contentType)
    {
        return _losslessAudioTypes.Contains(contentType);
    }

    /// <inheritdoc />
    public string GetContentTypeFromExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "application/octet-stream";
        }

        // Normalize extension to have leading dot
        var normalizedExt = extension.StartsWith('.') ? extension : "." + extension;

        return _extensionMappings.TryGetValue(normalizedExt.ToLowerInvariant(), out var contentType)
            ? contentType
            : "application/octet-stream";
    }

    /// <inheritdoc />
    public IReadOnlySet<string> ProcessableContentTypes => _processableTypes;

    /// <inheritdoc />
    public IReadOnlyList<string> AudioContentTypes => _audioTypes.AsReadOnly();

    /// <inheritdoc />
    public IReadOnlyList<string> TextureContentTypes => _textureTypes.AsReadOnly();

    /// <inheritdoc />
    public IReadOnlyList<string> ModelContentTypes => _modelTypes.AsReadOnly();

    /// <inheritdoc />
    public IReadOnlySet<string> ForbiddenContentTypes => _forbiddenTypes;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> ExtensionMappings => _extensionMappings;
}
