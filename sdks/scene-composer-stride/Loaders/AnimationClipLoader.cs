using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Stride.Animations;
using Stride.Core;
using Stride.Core.Reflection;
using Stride.Core.Serialization;
using Stride.Core.Serialization.Contents;

namespace BeyondImmersion.Bannou.Stride.SceneComposer.Loaders;

/// <summary>
/// Loads compiled Stride animation clips from bundle data.
/// </summary>
/// <remarks>
/// <para>
/// <strong>THEORETICAL IMPLEMENTATION:</strong> This loader is a theoretical implementation
/// based on the patterns used in <see cref="ModelLoader"/> and <see cref="TextureLoader"/>.
/// It has NOT been tested with actual animation data and will likely require adjustments
/// once real .sdanim files are available for testing.
/// </para>
/// <para>
/// Animation clips in Stride contain keyframe data for transformations (position, rotation, scale)
/// and other animated properties. Since keyframe data is CPU-based (not GPU-dependent),
/// animation loading should work without a GraphicsDevice, unlike textures.
/// </para>
/// <para>
/// <strong>FRAGILITY WARNING:</strong> This loader uses reflection to access internal
/// Stride APIs. It may break with Stride engine updates. See <see cref="ModelLoader"/>
/// for the list of internal APIs accessed.
/// </para>
/// <para>
/// <strong>TODO - NEEDS TESTING:</strong>
/// <list type="bullet">
/// <item>Verify CHNK header format for animation clips matches other assets</item>
/// <item>Confirm AnimationClip serialization follows same pattern as Model/Texture</item>
/// <item>Test with actual compiled .sdanim files from Stride Game Studio</item>
/// <item>Verify size estimation logic is reasonable for keyframe data</item>
/// <item>Test reference resolution for animations that reference other assets</item>
/// </list>
/// </para>
/// </remarks>
public sealed class AnimationClipLoader : IStrideAssetLoader<AnimationClip>
{
    private readonly IServiceRegistry _services;
    private readonly Action<string>? _debugLog;

    // Cached reflection info (shared pattern with ModelLoader/TextureLoader)
    private static readonly Type ContentSerializerContextType = typeof(ContentSerializerContext);
    private static readonly Type ContentSerializerType = typeof(ContentSerializer);
    private static readonly ConstructorInfo? ContextConstructor;
    private static readonly MethodInfo? SerializeReferencesMethod;
    private static readonly MethodInfo? SerializeContentMethod;
    private static readonly MethodInfo? GetSerializerMethod;
    private static readonly PropertyInfo? LoadContentReferencesProperty;

    static AnimationClipLoader()
    {
        ContextConstructor = ContentSerializerContextType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(string), typeof(ArchiveMode), typeof(ContentManager)],
            null);

        SerializeReferencesMethod = ContentSerializerContextType.GetMethod(
            "SerializeReferences",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(SerializationStream)],
            null);

        SerializeContentMethod = ContentSerializerContextType.GetMethod(
            "SerializeContent",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(SerializationStream), typeof(IContentSerializer), typeof(object)],
            null);

        LoadContentReferencesProperty = ContentSerializerContextType.GetProperty(
            "LoadContentReferences",
            BindingFlags.Instance | BindingFlags.NonPublic);

        GetSerializerMethod = ContentSerializerType.GetMethod(
            "GetSerializer",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(Type), typeof(Type)],
            null);
    }

    /// <summary>
    /// Creates a new animation clip loader.
    /// </summary>
    /// <param name="services">Stride service registry.</param>
    /// <param name="debugLog">Optional callback for debug logging.</param>
    /// <remarks>
    /// Unlike <see cref="TextureLoader"/>, AnimationClipLoader does not require a GraphicsDevice
    /// since animation data is CPU-based keyframe data, not GPU resources.
    /// </remarks>
    public AnimationClipLoader(IServiceRegistry services, Action<string>? debugLog = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _debugLog = debugLog;
    }

    /// <inheritdoc/>
    public async Task<AnimationClip> LoadAsync(byte[] data, string assetId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        return await Task.Run(() => LoadAnimationClip(data, assetId), cancellationToken);
    }

    private AnimationClip LoadAnimationClip(byte[] data, string assetId)
    {
        using var stream = new MemoryStream(data);

        // Check for CHNK format (compiled Stride asset)
        // THEORETICAL: Assuming animations use same CHNK format as models/textures
        var firstBytes = new byte[4];
        var bytesRead = stream.Read(firstBytes, 0, 4);
        stream.Position = 0;

        if (bytesRead < 4)
        {
            throw new InvalidOperationException(
                $"Animation data too short for asset '{assetId}' - expected at least 4 bytes, got {bytesRead}");
        }

        // CHNK magic: 0x43484E4B ("CHNK" in big-endian, reads as "KNHC" in memory)
        bool isChunkFormat = firstBytes[0] == 0x4B && firstBytes[1] == 0x4E &&
                            firstBytes[2] == 0x48 && firstBytes[3] == 0x43;

        if (!isChunkFormat)
        {
            throw new InvalidOperationException(
                $"Animation asset '{assetId}' is not in CHNK format. " +
                "Raw animation data is not supported - use compiled .sdanim files.");
        }

        return LoadAnimationFromChunk(stream, assetId);
    }

    private AnimationClip LoadAnimationFromChunk(MemoryStream stream, string assetId)
    {
        var contentManager = _services.GetService<ContentManager>();
        if (contentManager == null)
        {
            throw new InvalidOperationException(
                "ContentManager not available. Ensure Stride is properly initialized.");
        }

        var streamReader = new BinarySerializationReader(stream);

        var chunkHeader = ChunkHeader.Read(streamReader);
        if (chunkHeader == null)
        {
            throw new InvalidOperationException(
                $"Invalid animation data for '{assetId}' - missing CHNK header");
        }

        Log($"Loading animation '{assetId}' with type '{chunkHeader.Type}'");

        var headerObjType = AssemblyRegistry.GetType(chunkHeader.Type);
        if (headerObjType == null)
        {
            throw new InvalidOperationException(
                $"Cannot resolve type '{chunkHeader.Type}' for animation '{assetId}'");
        }

        // THEORETICAL: Animations might serialize as AnimationClip or a derived type
        var serializer = GetContentSerializer(contentManager.Serializer, headerObjType, typeof(AnimationClip));
        if (serializer == null)
        {
            throw new InvalidOperationException(
                $"No content serializer found for type '{headerObjType}' (animation '{assetId}'). " +
                "This may indicate the animation format is not supported.");
        }

        var context = CreateSerializerContext(assetId, contentManager);

        if (chunkHeader.OffsetToReferences != -1)
        {
            Log($"Animation '{assetId}' has references at offset {chunkHeader.OffsetToReferences}");
            streamReader.UnderlyingStream.Seek(chunkHeader.OffsetToReferences, SeekOrigin.Begin);
            InvokeSerializeReferences(context, streamReader);
            streamReader.UnderlyingStream.Seek(chunkHeader.OffsetToObject, SeekOrigin.Begin);
        }

        var animationClip = (AnimationClip)serializer.Construct(context);

        streamReader.Context.SerializerSelector = contentManager.Serializer.LowLevelSerializerSelector;
        streamReader.Context.Set(ContentSerializerContext.ContentSerializerContextProperty, context);

        // Set services in context - may be needed for reference resolution
        streamReader.Context.Tags.Set(ServiceRegistry.ServiceRegistryKey, _services);

        InvokeSerializeContent(context, streamReader, serializer, animationClip);

        Log($"Loaded animation '{assetId}' - Duration: {animationClip.Duration}s");

        return animationClip;
    }

    private static ContentSerializerContext CreateSerializerContext(string url, ContentManager contentManager)
    {
        if (ContextConstructor == null)
        {
            throw new InvalidOperationException(
                "Cannot find ContentSerializerContext constructor via reflection");
        }

        var context = (ContentSerializerContext)ContextConstructor.Invoke(
            [url, ArchiveMode.Deserialize, contentManager]);

        LoadContentReferencesProperty?.SetValue(context, false);

        return context;
    }

    private static void InvokeSerializeReferences(ContentSerializerContext context, SerializationStream stream)
    {
        SerializeReferencesMethod?.Invoke(context, [stream]);
    }

    private static void InvokeSerializeContent(
        ContentSerializerContext context,
        SerializationStream stream,
        IContentSerializer serializer,
        object obj)
    {
        if (SerializeContentMethod == null)
        {
            throw new InvalidOperationException(
                "Cannot find SerializeContent method via reflection");
        }

        SerializeContentMethod.Invoke(context, [stream, serializer, obj]);
    }

    private static IContentSerializer? GetContentSerializer(ContentSerializer serializer, Type? storageType, Type objectType)
    {
        if (GetSerializerMethod == null)
        {
            throw new InvalidOperationException(
                "Cannot find GetSerializer method via reflection");
        }

        return (IContentSerializer?)GetSerializerMethod.Invoke(serializer, [storageType, objectType]);
    }

    private void Log(string message)
    {
        _debugLog?.Invoke(message);
        Debug.WriteLine($"[AnimationClipLoader] {message}");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// THEORETICAL size estimation based on typical animation data:
    /// </para>
    /// <list type="bullet">
    /// <item>Each curve stores keyframes with time and value data</item>
    /// <item>Transform animations have position (3 floats), rotation (4 floats), scale (3 floats)</item>
    /// <item>Estimate assumes ~50 bytes per keyframe average</item>
    /// </list>
    /// <para>
    /// This estimate may be inaccurate - actual size depends on:
    /// compression, number of curves, keyframe density, interpolation data.
    /// </para>
    /// </remarks>
    public long EstimateSize(AnimationClip clip)
    {
        if (clip == null)
            return 0;

        // THEORETICAL: Estimate based on duration and typical keyframe density
        // Real estimation would need to iterate through clip.Channels and count keyframes

        // Assumptions:
        // - 30 keyframes per second is typical
        // - Each keyframe is approximately 50 bytes (time + interpolated values)
        // - Base overhead for clip metadata is ~1KB

        const long BaseOverhead = 1024;
        const int KeyframesPerSecond = 30;
        const int BytesPerKeyframe = 50;

        var durationSeconds = clip.Duration.TotalSeconds;
        var estimatedKeyframes = (long)(durationSeconds * KeyframesPerSecond);

        // Assume 3-5 animated channels per clip (position, rotation, scale, etc.)
        const int EstimatedChannels = 4;

        return BaseOverhead + (estimatedKeyframes * BytesPerKeyframe * EstimatedChannels);
    }
}
