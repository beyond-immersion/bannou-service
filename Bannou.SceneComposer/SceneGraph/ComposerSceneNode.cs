using BeyondImmersion.Bannou.SceneComposer.Abstractions;
using BeyondImmersion.Bannou.SceneComposer.Math;

namespace BeyondImmersion.Bannou.SceneComposer.SceneGraph;

/// <summary>
/// A node in the scene composition hierarchy.
/// Represents an object that can be positioned, have children, and optionally display an asset.
/// </summary>
public class ComposerSceneNode
{
    private readonly List<ComposerSceneNode> _children = new();
    private readonly Dictionary<string, object> _annotations = new();
    private readonly List<AttachmentPoint> _attachmentPoints = new();
    private readonly List<Affordance> _affordances = new();
    private Transform _localTransform = Transform.Identity;

    /// <summary>
    /// Unique identifier for this node.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Display name of the node.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Type of this node.
    /// </summary>
    public NodeType NodeType { get; }

    /// <summary>
    /// Parent node, or null if this is a root node.
    /// </summary>
    public ComposerSceneNode? Parent { get; internal set; }

    /// <summary>
    /// Child nodes.
    /// </summary>
    public IReadOnlyList<ComposerSceneNode> Children => _children;

    /// <summary>
    /// Local transform relative to parent.
    /// </summary>
    public Transform LocalTransform
    {
        get => _localTransform;
        set
        {
            _localTransform = value;
            InvalidateWorldTransform();
        }
    }

    /// <summary>
    /// Asset bound to this node (for mesh nodes).
    /// </summary>
    public AssetReference Asset { get; internal set; }

    /// <summary>
    /// Reference to another scene (for reference nodes).
    /// </summary>
    public string? ReferenceSceneId { get; set; }

    /// <summary>
    /// Whether this node is visible in the editor.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Whether this node is locked (cannot be selected/modified).
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// Whether this node is expanded in the hierarchy view.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// Attachment points defined on this node.
    /// </summary>
    public IReadOnlyList<AttachmentPoint> AttachmentPoints => _attachmentPoints;

    /// <summary>
    /// Affordances (interaction capabilities) of this node.
    /// </summary>
    public IReadOnlyList<Affordance> Affordances => _affordances;

    /// <summary>
    /// Asset slot definition for procedural swapping.
    /// </summary>
    public AssetSlot? AssetSlot { get; set; }

    /// <summary>
    /// Consumer-specific annotations (namespaced key-value pairs).
    /// </summary>
    public IReadOnlyDictionary<string, object> Annotations => _annotations;

    /// <summary>
    /// Marker type (for marker nodes).
    /// </summary>
    public MarkerType? MarkerType { get; set; }

    /// <summary>
    /// Volume shape (for volume nodes).
    /// </summary>
    public VolumeShape? VolumeShape { get; set; }

    /// <summary>
    /// Custom type identifier (for custom nodes).
    /// </summary>
    public string? CustomType { get; set; }

    // Cached world transform
    private Transform? _cachedWorldTransform;

    /// <summary>
    /// Create a new scene node.
    /// </summary>
    /// <param name="nodeType">Type of node.</param>
    /// <param name="name">Display name.</param>
    /// <param name="id">Optional specific ID (new GUID if not specified).</param>
    public ComposerSceneNode(NodeType nodeType, string name, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Name = name ?? throw new ArgumentNullException(nameof(name));
        NodeType = nodeType;
    }

    /// <summary>
    /// Get the world transform (computed from hierarchy).
    /// </summary>
    public Transform GetWorldTransform()
    {
        if (_cachedWorldTransform.HasValue)
            return _cachedWorldTransform.Value;

        _cachedWorldTransform = Parent != null
            ? Parent.GetWorldTransform().Combine(LocalTransform)
            : LocalTransform;

        return _cachedWorldTransform.Value;
    }

    /// <summary>
    /// Invalidate cached world transform (called when local transform or parent changes).
    /// </summary>
    internal void InvalidateWorldTransform()
    {
        _cachedWorldTransform = null;
        foreach (var child in _children)
        {
            child.InvalidateWorldTransform();
        }
    }

    /// <summary>
    /// Get the sibling index (position among siblings).
    /// </summary>
    public int GetSiblingIndex()
    {
        if (Parent == null) return -1; // Would need scene reference for root nodes
        return Parent._children.IndexOf(this);
    }

    /// <summary>
    /// Get the depth in the hierarchy (0 = root).
    /// </summary>
    public int GetDepth()
    {
        var depth = 0;
        var current = Parent;
        while (current != null)
        {
            depth++;
            current = current.Parent;
        }
        return depth;
    }

    /// <summary>
    /// Check if this node is a descendant of another node.
    /// </summary>
    public bool IsDescendantOf(ComposerSceneNode ancestor)
    {
        var current = Parent;
        while (current != null)
        {
            if (current == ancestor) return true;
            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// Check if this node is an ancestor of another node.
    /// </summary>
    public bool IsAncestorOf(ComposerSceneNode descendant)
    {
        return descendant.IsDescendantOf(this);
    }

    #region Child Management (internal)

    /// <summary>
    /// Add a child node.
    /// </summary>
    internal void AddChild(ComposerSceneNode child, int? insertIndex = null)
    {
        if (child.Parent != null)
            throw new InvalidOperationException("Node already has a parent. Remove from parent first.");

        child.Parent = this;

        if (insertIndex.HasValue && insertIndex.Value >= 0 && insertIndex.Value < _children.Count)
            _children.Insert(insertIndex.Value, child);
        else
            _children.Add(child);

        child.InvalidateWorldTransform();
    }

    /// <summary>
    /// Remove a child node.
    /// </summary>
    internal void RemoveChild(ComposerSceneNode child)
    {
        if (child.Parent != this)
            throw new InvalidOperationException("Node is not a child of this node.");

        _children.Remove(child);
        child.Parent = null;
        child.InvalidateWorldTransform();
    }

    /// <summary>
    /// Get the index of a child node.
    /// </summary>
    internal int GetChildIndex(ComposerSceneNode child)
    {
        return _children.IndexOf(child);
    }

    #endregion

    #region Attachment Points

    /// <summary>
    /// Add an attachment point.
    /// </summary>
    public void AddAttachmentPoint(AttachmentPoint point)
    {
        if (_attachmentPoints.Any(p => p.Name == point.Name))
            throw new InvalidOperationException($"Attachment point '{point.Name}' already exists.");
        _attachmentPoints.Add(point);
    }

    /// <summary>
    /// Remove an attachment point.
    /// </summary>
    public bool RemoveAttachmentPoint(string name)
    {
        var point = _attachmentPoints.FirstOrDefault(p => p.Name == name);
        if (point == null) return false;
        _attachmentPoints.Remove(point);
        return true;
    }

    /// <summary>
    /// Get an attachment point by name.
    /// </summary>
    public AttachmentPoint? GetAttachmentPoint(string name)
    {
        return _attachmentPoints.FirstOrDefault(p => p.Name == name);
    }

    #endregion

    #region Affordances

    /// <summary>
    /// Add an affordance.
    /// </summary>
    public void AddAffordance(Affordance affordance)
    {
        _affordances.Add(affordance);
    }

    /// <summary>
    /// Remove affordances of a specific type.
    /// </summary>
    public int RemoveAffordances(AffordanceType type)
    {
        return _affordances.RemoveAll(a => a.Type == type);
    }

    /// <summary>
    /// Check if node has an affordance type.
    /// </summary>
    public bool HasAffordance(AffordanceType type)
    {
        return _affordances.Any(a => a.Type == type);
    }

    #endregion

    #region Annotations

    /// <summary>
    /// Set an annotation value.
    /// </summary>
    public void SetAnnotation(string key, object value)
    {
        _annotations[key] = value;
    }

    /// <summary>
    /// Get an annotation value.
    /// </summary>
    public T? GetAnnotation<T>(string key)
    {
        if (_annotations.TryGetValue(key, out var value) && value is T typedValue)
            return typedValue;
        return default;
    }

    /// <summary>
    /// Remove an annotation.
    /// </summary>
    public bool RemoveAnnotation(string key)
    {
        return _annotations.Remove(key);
    }

    #endregion

    /// <summary>
    /// Create a deep clone of this node (and optionally children).
    /// </summary>
    /// <param name="deepClone">Whether to clone children.</param>
    /// <param name="newId">Whether to generate new IDs.</param>
    public ComposerSceneNode Clone(bool deepClone = true, bool newId = true)
    {
        var clone = new ComposerSceneNode(NodeType, Name, newId ? null : Id)
        {
            LocalTransform = LocalTransform,
            Asset = Asset,
            ReferenceSceneId = ReferenceSceneId,
            IsVisible = IsVisible,
            IsLocked = IsLocked,
            IsExpanded = IsExpanded,
            AssetSlot = AssetSlot?.Clone(),
            MarkerType = MarkerType,
            VolumeShape = VolumeShape?.Clone(),
            CustomType = CustomType
        };

        foreach (var ap in _attachmentPoints)
            clone._attachmentPoints.Add(ap.Clone());

        foreach (var aff in _affordances)
            clone._affordances.Add(aff.Clone());

        foreach (var kvp in _annotations)
            clone._annotations[kvp.Key] = kvp.Value; // Note: shallow copy of values

        if (deepClone)
        {
            foreach (var child in _children)
            {
                var childClone = child.Clone(deepClone: true, newId: newId);
                clone.AddChild(childClone);
            }
        }

        return clone;
    }

    public override string ToString() =>
        $"Node({Id:N}, {Name}, {NodeType})";
}

/// <summary>
/// Attachment point for connecting child objects at predefined locations.
/// </summary>
public class AttachmentPoint
{
    /// <summary>
    /// Unique name for this attachment point.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Local transform relative to the owning node.
    /// </summary>
    public Transform LocalTransform { get; set; } = Transform.Identity;

    /// <summary>
    /// Tags of assets that can attach here.
    /// </summary>
    public List<string> AcceptsTags { get; } = new();

    /// <summary>
    /// Default asset to show if nothing attached.
    /// </summary>
    public AssetReference? DefaultAsset { get; set; }

    /// <summary>
    /// Currently attached node (runtime, not persisted).
    /// </summary>
    public Guid? AttachedNodeId { get; set; }

    public AttachmentPoint(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public AttachmentPoint Clone() => new(Name)
    {
        LocalTransform = LocalTransform,
        DefaultAsset = DefaultAsset
    };
}

/// <summary>
/// Affordance describing what an object can do or how it can be interacted with.
/// </summary>
public class Affordance
{
    /// <summary>
    /// Type of affordance.
    /// </summary>
    public AffordanceType Type { get; set; }

    /// <summary>
    /// Type-specific parameters.
    /// </summary>
    public Dictionary<string, object> Parameters { get; } = new();

    public Affordance(AffordanceType type)
    {
        Type = type;
    }

    public Affordance Clone()
    {
        var clone = new Affordance(Type);
        foreach (var kvp in Parameters)
            clone.Parameters[kvp.Key] = kvp.Value;
        return clone;
    }
}

/// <summary>
/// Types of affordances.
/// </summary>
public enum AffordanceType
{
    /// <summary>Can be walked on.</summary>
    Walkable,
    /// <summary>Can be climbed.</summary>
    Climbable,
    /// <summary>Can be sat on.</summary>
    Sittable,
    /// <summary>Can be interacted with (generic).</summary>
    Interactive,
    /// <summary>Can be collected/picked up.</summary>
    Collectible,
    /// <summary>Can be destroyed.</summary>
    Destructible,
    /// <summary>Is a container (inventory).</summary>
    Container,
    /// <summary>Is a door/portal.</summary>
    Door,
    /// <summary>Is a teleport destination.</summary>
    Teleport
}

/// <summary>
/// Asset slot for procedural asset swapping.
/// </summary>
public class AssetSlot
{
    /// <summary>
    /// Category of acceptable assets.
    /// </summary>
    public string SlotType { get; set; }

    /// <summary>
    /// Tags of acceptable assets.
    /// </summary>
    public List<string> AcceptsTags { get; } = new();

    /// <summary>
    /// Default asset for this slot.
    /// </summary>
    public AssetReference? DefaultAsset { get; set; }

    /// <summary>
    /// Pre-approved variation assets.
    /// </summary>
    public List<AssetReference> Variations { get; } = new();

    public AssetSlot(string slotType)
    {
        SlotType = slotType ?? throw new ArgumentNullException(nameof(slotType));
    }

    public AssetSlot Clone()
    {
        var clone = new AssetSlot(SlotType)
        {
            DefaultAsset = DefaultAsset
        };
        clone.AcceptsTags.AddRange(AcceptsTags);
        clone.Variations.AddRange(Variations);
        return clone;
    }
}

/// <summary>
/// Types of marker nodes.
/// </summary>
public enum MarkerType
{
    /// <summary>Generic marker.</summary>
    Generic,
    /// <summary>Player spawn point.</summary>
    SpawnPoint,
    /// <summary>NPC spawn point.</summary>
    NpcSpawn,
    /// <summary>Navigation waypoint.</summary>
    Waypoint,
    /// <summary>Camera position.</summary>
    CameraPoint,
    /// <summary>Light source position.</summary>
    LightPoint,
    /// <summary>Audio source position.</summary>
    AudioPoint,
    /// <summary>Trigger point.</summary>
    TriggerPoint
}

/// <summary>
/// Volume shape definition.
/// </summary>
public class VolumeShape
{
    /// <summary>
    /// Type of volume shape.
    /// </summary>
    public VolumeShapeType ShapeType { get; set; }

    /// <summary>
    /// Size of the volume (interpretation depends on shape type).
    /// </summary>
    public Vector3 Size { get; set; } = Vector3.One;

    /// <summary>
    /// Radius for sphere/cylinder shapes.
    /// </summary>
    public double Radius { get; set; } = 1.0;

    /// <summary>
    /// Height for cylinder/capsule shapes.
    /// </summary>
    public double Height { get; set; } = 1.0;

    public VolumeShape(VolumeShapeType shapeType)
    {
        ShapeType = shapeType;
    }

    public VolumeShape Clone() => new(ShapeType)
    {
        Size = Size,
        Radius = Radius,
        Height = Height
    };
}

/// <summary>
/// Types of volume shapes.
/// </summary>
public enum VolumeShapeType
{
    /// <summary>Axis-aligned box.</summary>
    Box,
    /// <summary>Sphere.</summary>
    Sphere,
    /// <summary>Cylinder.</summary>
    Cylinder,
    /// <summary>Capsule.</summary>
    Capsule,
    /// <summary>Convex mesh.</summary>
    ConvexMesh
}
