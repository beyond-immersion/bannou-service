using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Stride.Core;
using Stride.Core.Reflection;
using Stride.Core.Serialization;
using Stride.Core.Serialization.Contents;
using Stride.Graphics;

namespace BeyondImmersion.Bannou.Stride.SceneComposer.Loaders;

/// <summary>
/// Loads compiled Stride textures (.sdtex) from bundle data.
/// </summary>
/// <remarks>
/// <para>
/// Uses reflection to access Stride's internal serialization APIs.
/// For textures, we first try Image.Load (for simple formats), then fall back
/// to full deserialization for complex compiled textures.
/// </para>
/// <para>
/// <strong>FRAGILITY WARNING:</strong> This loader uses reflection to access internal
/// Stride APIs. It may break with Stride engine updates. See <see cref="ModelLoader"/>
/// for the list of internal APIs accessed.
/// </para>
/// </remarks>
public sealed class TextureLoader : IStrideAssetLoader<Texture>
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly IServiceRegistry _services;
    private readonly Action<string>? _debugLog;

    // Cached reflection info (shared with ModelLoader pattern)
    private static readonly Type ContentSerializerContextType = typeof(ContentSerializerContext);
    private static readonly Type ContentSerializerType = typeof(ContentSerializer);
    private static readonly ConstructorInfo? ContextConstructor;
    private static readonly MethodInfo? SerializeReferencesMethod;
    private static readonly MethodInfo? SerializeContentMethod;
    private static readonly MethodInfo? GetSerializerMethod;
    private static readonly PropertyInfo? LoadContentReferencesProperty;

    static TextureLoader()
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
    /// Creates a new texture loader.
    /// </summary>
    /// <param name="services">Stride service registry.</param>
    /// <param name="graphicsDevice">Stride graphics device for creating GPU resources.</param>
    /// <param name="debugLog">Optional callback for debug logging.</param>
    public TextureLoader(IServiceRegistry services, GraphicsDevice graphicsDevice, Action<string>? debugLog = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _debugLog = debugLog;
    }

    /// <inheritdoc/>
    public async Task<Texture> LoadAsync(byte[] data, string assetId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        return await Task.Run(() => LoadTexture(data, assetId), cancellationToken);
    }

    private Texture LoadTexture(byte[] data, string assetId)
    {
        using var stream = new MemoryStream(data);

        // Check if this is a CHNK format (compiled Stride asset) or raw image
        var firstBytes = new byte[4];
        stream.Read(firstBytes, 0, 4);
        stream.Position = 0;

        // CHNK magic: 0x43484E4B ("CHNK" in big-endian, reads as "KNHC" in memory)
        bool isChunkFormat = firstBytes[0] == 0x4B && firstBytes[1] == 0x4E &&
                            firstBytes[2] == 0x48 && firstBytes[3] == 0x43;

        if (isChunkFormat)
        {
            return LoadTextureFromChunk(stream, assetId);
        }
        else
        {
            return LoadTextureFromImage(stream, assetId);
        }
    }

    private Texture LoadTextureFromChunk(MemoryStream stream, string assetId)
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
                $"Invalid asset data for '{assetId}' - missing CHNK header");
        }

        var headerObjType = AssemblyRegistry.GetType(chunkHeader.Type);
        if (headerObjType == null)
        {
            throw new InvalidOperationException(
                $"Cannot resolve type '{chunkHeader.Type}' for asset '{assetId}'");
        }

        var serializer = GetContentSerializer(contentManager.Serializer, headerObjType, typeof(Texture));
        if (serializer == null)
        {
            throw new InvalidOperationException(
                $"No content serializer found for type '{headerObjType}' (asset '{assetId}')");
        }

        var context = CreateSerializerContext(assetId, contentManager);

        if (chunkHeader.OffsetToReferences != -1)
        {
            streamReader.UnderlyingStream.Seek(chunkHeader.OffsetToReferences, SeekOrigin.Begin);
            InvokeSerializeReferences(context, streamReader);
            streamReader.UnderlyingStream.Seek(chunkHeader.OffsetToObject, SeekOrigin.Begin);
        }

        var texture = (Texture)serializer.Construct(context);

        streamReader.Context.SerializerSelector = contentManager.Serializer.LowLevelSerializerSelector;
        streamReader.Context.Set(ContentSerializerContext.ContentSerializerContextProperty, context);

        // Set services in context - TextureContentSerializer gets GraphicsDevice via IGraphicsDeviceService
        streamReader.Context.Tags.Set(ServiceRegistry.ServiceRegistryKey, _services);

        InvokeSerializeContent(context, streamReader, serializer, texture);

        return texture;
    }

    private Texture LoadTextureFromImage(MemoryStream stream, string assetId)
    {
        // Try loading as raw image (DDS, PNG, etc.)
        try
        {
            var image = Image.Load(stream);
            if (image != null)
            {
                var texture = Texture.New(_graphicsDevice, image, TextureFlags.ShaderResource);
                image.Dispose();
                return texture;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to load texture '{assetId}' as image. Error: {ex.Message}", ex);
        }

        throw new InvalidOperationException(
            $"Failed to load texture '{assetId}' - unknown format");
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
        Debug.WriteLine($"[TextureLoader] {message}");
    }

    /// <inheritdoc/>
    public long EstimateSize(Texture texture)
    {
        if (texture == null)
            return 0;

        long size = texture.Width * texture.Height;

        if (texture.MipLevels > 1)
        {
            size = (long)(size * 1.33);
        }

        size *= texture.ArraySize;
        size *= GetFormatBytesPerPixel(texture.Format);

        return size;
    }

    private static int GetFormatBytesPerPixel(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.R8G8B8A8_UNorm => 4,
            PixelFormat.R8G8B8A8_UNorm_SRgb => 4,
            PixelFormat.B8G8R8A8_UNorm => 4,
            PixelFormat.B8G8R8A8_UNorm_SRgb => 4,
            PixelFormat.R8G8B8A8_SNorm => 4,
            PixelFormat.R16G16B16A16_Float => 8,
            PixelFormat.R32G32B32A32_Float => 16,
            PixelFormat.BC1_UNorm => 1,
            PixelFormat.BC1_UNorm_SRgb => 1,
            PixelFormat.BC2_UNorm => 1,
            PixelFormat.BC2_UNorm_SRgb => 1,
            PixelFormat.BC3_UNorm => 1,
            PixelFormat.BC3_UNorm_SRgb => 1,
            PixelFormat.BC4_UNorm => 1,
            PixelFormat.BC5_UNorm => 1,
            PixelFormat.BC7_UNorm => 1,
            PixelFormat.BC7_UNorm_SRgb => 1,
            _ => 4
        };
    }
}
