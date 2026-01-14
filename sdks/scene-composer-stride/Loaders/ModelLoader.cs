using Stride.Core;
using Stride.Core.Reflection;
using Stride.Core.Serialization;
using Stride.Core.Serialization.Contents;
using Stride.Graphics;
using Stride.Rendering;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.Bannou.SceneComposer.Stride.Loaders;

/// <summary>
/// Delegate for resolving model dependencies from bundles.
/// </summary>
/// <param name="assetUrl">The asset URL (e.g., "Model/gen/Buffer_1").</param>
/// <returns>The raw asset data, or null if not found.</returns>
public delegate byte[]? DependencyResolver(string assetUrl);

/// <summary>
/// Loads compiled Stride models (.sdmodel) from bundle data.
/// </summary>
/// <remarks>
/// <para>
/// Uses reflection to access Stride's internal serialization APIs.
/// This is necessary because ContentSerializerContext has an internal constructor.
/// </para>
/// <para>
/// <strong>FRAGILITY WARNING:</strong> This loader uses reflection to access internal
/// Stride APIs. It may break with Stride engine updates. The following internals are accessed:
/// <list type="bullet">
/// <item>ContentSerializerContext constructor</item>
/// <item>ContentSerializerContext.SerializeReferences</item>
/// <item>ContentSerializerContext.SerializeContent</item>
/// <item>ContentSerializer.GetSerializer</item>
/// <item>ContentManager.RegisterDeserializedObject</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ModelLoader : IStrideAssetLoader<Model>
{
    private readonly IServiceRegistry _services;
    private readonly Action<string>? _debugLog;

    // Cached reflection info for performance
    private static readonly Type ContentSerializerContextType = typeof(ContentSerializerContext);
    private static readonly Type ContentSerializerType = typeof(ContentSerializer);
    private static readonly Type ContentManagerType = typeof(ContentManager);
    private static readonly ConstructorInfo? ContextConstructor;
    private static readonly MethodInfo? SerializeReferencesMethod;
    private static readonly MethodInfo? SerializeContentMethod;
    private static readonly MethodInfo? GetSerializerMethod;
    private static readonly PropertyInfo? LoadContentReferencesProperty;
    private static readonly MethodInfo? RegisterDeserializedObjectMethod;

    static ModelLoader()
    {
        // Cache reflection lookups
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

        // RegisterDeserializedObject is a generic method, get the open version
        RegisterDeserializedObjectMethod = ContentManagerType.GetMethod(
            "RegisterDeserializedObject",
            BindingFlags.Instance | BindingFlags.NonPublic);
    }

    /// <summary>
    /// Creates a new model loader.
    /// </summary>
    /// <param name="services">Stride service registry for accessing serialization services.</param>
    /// <param name="debugLog">Optional callback for debug logging.</param>
    public ModelLoader(IServiceRegistry services, Action<string>? debugLog = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _debugLog = debugLog;
    }

    /// <inheritdoc/>
    public async Task<Model> LoadAsync(byte[] data, string assetId, CancellationToken cancellationToken = default)
    {
        return await LoadAsync(data, assetId, dependencyResolver: null, cancellationToken);
    }

    /// <summary>
    /// Loads a model with dependency resolution support.
    /// </summary>
    /// <param name="data">The model data.</param>
    /// <param name="assetId">The asset ID.</param>
    /// <param name="dependencyResolver">Optional resolver for loading model dependencies from bundles.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded model.</returns>
    public async Task<Model> LoadAsync(
        byte[] data,
        string assetId,
        DependencyResolver? dependencyResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        return await Task.Run(() => LoadModel(data, assetId, dependencyResolver), cancellationToken);
    }

    private Model LoadModel(byte[] data, string assetId, DependencyResolver? dependencyResolver = null)
    {
        // Get the content manager from services
        var contentManager = _services.GetService<ContentManager>() ?? throw new InvalidOperationException(
                "ContentManager not available. Ensure Stride is properly initialized.");
        using var stream = new MemoryStream(data);
        var streamReader = new BinarySerializationReader(stream);

        // Read the ChunkHeader (magic "CHNK", version, type, offsets)
        var chunkHeader = ChunkHeader.Read(streamReader) ?? throw new InvalidOperationException(
                $"Invalid asset data for '{assetId}' - missing CHNK header");

        // Get the object type from the header
        var headerObjType = AssemblyRegistry.GetType(chunkHeader.Type) ?? throw new InvalidOperationException(
                $"Cannot resolve type '{chunkHeader.Type}' for asset '{assetId}'");

        // Get the serializer via reflection (GetSerializer is internal)
        var serializer = GetContentSerializer(contentManager.Serializer, headerObjType, typeof(Model)) ?? throw new InvalidOperationException(
                $"No content serializer found for type '{headerObjType}' (asset '{assetId}')");

        // Read chunk references first (if present) and pre-load dependencies
        List<ChunkReference>? chunkReferences = null;
        if (chunkHeader.OffsetToReferences != -1)
        {
            streamReader.UnderlyingStream.Seek(chunkHeader.OffsetToReferences, SeekOrigin.Begin);
            chunkReferences = new List<ChunkReference>();
            streamReader.Serialize(ref chunkReferences, ArchiveMode.Deserialize);

            Log($"Asset '{assetId}' has {chunkReferences?.Count ?? 0} chunk references");
            if (chunkReferences != null)
            {
                foreach (var chunkRef in chunkReferences)
                {
                    Log($"  - Location: {chunkRef.Location}, ObjectType: {chunkRef.ObjectType?.Name ?? "null"}");
                }

                // Pre-load dependencies if resolver is provided
                if (dependencyResolver != null)
                {
                    PreLoadDependencies(contentManager, chunkReferences, dependencyResolver);
                }
            }
        }
        else
        {
            Log($"Asset '{assetId}' has NO chunk references (OffsetToReferences = -1)");
        }

        // Create ContentSerializerContext via reflection
        // Enable LoadContentReferences if we have dependencies pre-loaded
        var context = CreateSerializerContext(assetId, contentManager, loadReferences: dependencyResolver != null);

        // Re-read chunk references into context (if present)
        if (chunkHeader.OffsetToReferences != -1)
        {
            streamReader.UnderlyingStream.Seek(chunkHeader.OffsetToReferences, SeekOrigin.Begin);
            InvokeSerializeReferences(context, streamReader);
            streamReader.UnderlyingStream.Seek(chunkHeader.OffsetToObject, SeekOrigin.Begin);
        }

        // Construct the Model instance
        var model = (Model)serializer.Construct(context);

        // Prepare serializer context
        streamReader.Context.SerializerSelector = contentManager.Serializer.LowLevelSerializerSelector;
        streamReader.Context.Set(ContentSerializerContext.ContentSerializerContextProperty, context);
        streamReader.Context.Tags.Set(ServiceRegistry.ServiceRegistryKey, _services);

        // Deserialize the content
        InvokeSerializeContent(context, streamReader, serializer, model);

        return model;
    }

    private static ContentSerializerContext CreateSerializerContext(
        string url,
        ContentManager contentManager,
        bool loadReferences = false)
    {
        if (ContextConstructor == null)
        {
            throw new InvalidOperationException(
                "Cannot find ContentSerializerContext constructor via reflection");
        }

        var context = (ContentSerializerContext)ContextConstructor.Invoke(
            [url, ArchiveMode.Deserialize, contentManager]);

        // Set LoadContentReferences based on whether we pre-loaded dependencies
        LoadContentReferencesProperty?.SetValue(context, loadReferences);

        return context;
    }

    /// <summary>
    /// Pre-loads model dependencies (buffers) and registers them with the ContentManager.
    /// </summary>
    private void PreLoadDependencies(
        ContentManager contentManager,
        List<ChunkReference> chunkReferences,
        DependencyResolver dependencyResolver)
    {
        foreach (var chunkRef in chunkReferences)
        {
            if (string.IsNullOrEmpty(chunkRef.Location))
                continue;

            var depData = dependencyResolver(chunkRef.Location);
            if (depData == null)
            {
                Log($"  Dependency not found: {chunkRef.Location}");
                continue;
            }

            Log($"  Pre-loading dependency: {chunkRef.Location} ({depData.Length} bytes)");

            try
            {
                // Deserialize the dependency (Buffer object)
                var depObj = DeserializeDependency(contentManager, depData, chunkRef.Location, chunkRef.ObjectType);
                if (depObj != null)
                {
                    // Register with ContentManager using reflection
                    RegisterDependency(contentManager, chunkRef.Location, depObj, chunkRef.ObjectType);
                    Log($"  Registered dependency: {chunkRef.Location}");
                }
            }
            catch (Exception ex)
            {
                Log($"  Error loading dependency {chunkRef.Location}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Deserializes a dependency object (typically a Buffer).
    /// </summary>
    private object? DeserializeDependency(
        ContentManager contentManager,
        byte[] data,
        string url,
        Type? objectType)
    {
        if (objectType == null)
            return null;

        using var stream = new MemoryStream(data);
        var streamReader = new BinarySerializationReader(stream);

        // Read chunk header
        var chunkHeader = ChunkHeader.Read(streamReader);
        if (chunkHeader == null)
        {
            Log($"  Dependency {url} has no CHNK header");
            return null;
        }

        var headerObjType = AssemblyRegistry.GetType(chunkHeader.Type);
        if (headerObjType == null)
        {
            Log($"  Cannot resolve type {chunkHeader.Type} for {url}");
            return null;
        }

        // Get serializer for the dependency type
        var serializer = GetContentSerializer(contentManager.Serializer, headerObjType, objectType);
        if (serializer == null)
        {
            Log($"  No serializer for dependency type {headerObjType} ({url})");
            return null;
        }

        // Create context for dependency
        var context = CreateSerializerContext(url, contentManager, loadReferences: false);

        // Handle references if present
        if (chunkHeader.OffsetToReferences != -1)
        {
            streamReader.UnderlyingStream.Seek(chunkHeader.OffsetToReferences, SeekOrigin.Begin);
            InvokeSerializeReferences(context, streamReader);
            streamReader.UnderlyingStream.Seek(chunkHeader.OffsetToObject, SeekOrigin.Begin);
        }

        // Construct and deserialize
        var obj = serializer.Construct(context);

        streamReader.Context.SerializerSelector = contentManager.Serializer.LowLevelSerializerSelector;
        streamReader.Context.Set(ContentSerializerContext.ContentSerializerContextProperty, context);
        streamReader.Context.Tags.Set(ServiceRegistry.ServiceRegistryKey, _services);

        InvokeSerializeContent(context, streamReader, serializer, obj);

        return obj;
    }

    /// <summary>
    /// Registers a pre-loaded dependency with the ContentManager.
    /// </summary>
    private static void RegisterDependency(
        ContentManager contentManager,
        string url,
        object obj,
        Type? objectType)
    {
        if (RegisterDeserializedObjectMethod == null || objectType == null)
            return;

        // Make the generic method for the specific type
        var genericMethod = RegisterDeserializedObjectMethod.MakeGenericMethod(objectType);
        genericMethod.Invoke(contentManager, [url, obj]);
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
        Debug.WriteLine($"[ModelLoader] {message}");
    }

    /// <inheritdoc/>
    public long EstimateSize(Model model)
    {
        if (model == null)
            return 0;

        long size = 0;

        if (model.Meshes != null)
        {
            foreach (var mesh in model.Meshes)
            {
                if (mesh.Draw != null)
                {
                    foreach (var buffer in mesh.Draw.VertexBuffers)
                    {
                        size += buffer.Count * buffer.Stride;
                    }

                    if (mesh.Draw.IndexBuffer != null)
                    {
                        size += mesh.Draw.IndexBuffer.Count * (mesh.Draw.IndexBuffer.Is32Bit ? 4 : 2);
                    }
                }
            }
        }

        return size + 1024;
    }
}
