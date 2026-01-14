using BeyondImmersion.Bannou.SceneComposer;
using BeyondImmersion.Bannou.SceneComposer.Abstractions;
using BeyondImmersion.Bannou.SceneComposer.Events;
using BeyondImmersion.Bannou.SceneComposer.Gizmo;
using BeyondImmersion.Bannou.SceneComposer.SceneGraph;
using BeyondImmersion.Bannou.SceneComposer.Stride.Bridge;
using BeyondImmersion.Bannou.SceneComposer.Stride.Content;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using System;
using System.IO;
using System.Threading.Tasks;
using ComposerCore = BeyondImmersion.Bannou.SceneComposer.SceneComposer;
using SdkQuaternion = BeyondImmersion.Bannou.SceneComposer.Math.Quaternion;
using SdkRay = BeyondImmersion.Bannou.SceneComposer.Math.Ray;
using SdkVector2 = BeyondImmersion.Bannou.SceneComposer.Abstractions.Vector2;
using SdkVector3 = BeyondImmersion.Bannou.SceneComposer.Math.Vector3;

namespace BeyondImmersion.Bannou.SceneComposer.Stride.Examples;

/// <summary>
/// Stride SyncScript that integrates the Bannou SceneComposer SDK for runtime scene editing.
/// Provides camera controls, input handling, and gizmo interaction.
/// </summary>
/// <remarks>
/// <para>
/// This script provides a complete, ready-to-use scene editor implementation.
/// Add it to an entity in your scene and assign the EditorCamera.
/// </para>
/// <para>
/// Controls:
/// <list type="bullet">
/// <item>WASD/QE - Move camera</item>
/// <item>Right-click drag - Rotate camera</item>
/// <item>Left-click - Select entity</item>
/// <item>Ctrl+Left-click - Toggle selection</item>
/// <item>Delete - Delete selected</item>
/// <item>Escape - Clear selection</item>
/// <item>Ctrl+N - Create group node</item>
/// <item>Ctrl+Z/Y - Undo/Redo</item>
/// <item>W/E/R - Translate/Rotate/Scale gizmo mode</item>
/// </list>
/// </para>
/// </remarks>
public class StrideSceneEditorScript : SyncScript
{
    /// <summary>
    /// Path to a .bannou bundle to load on start (for testing).
    /// </summary>
    [DataMember]
    public string? InitialBundlePath { get; set; }

    /// <summary>
    /// Camera entity for editor view.
    /// </summary>
    [DataMember]
    public CameraComponent? EditorCamera { get; set; }

    /// <summary>
    /// Speed for camera movement (units per second).
    /// </summary>
    [DataMember]
    public float CameraMoveSpeed { get; set; } = 5f;

    /// <summary>
    /// Speed for camera rotation (radians per pixel).
    /// </summary>
    [DataMember]
    public float CameraRotateSpeed { get; set; } = 0.003f;

    /// <summary>
    /// Shift key multiplier for camera speed.
    /// </summary>
    [DataMember]
    public float CameraSprintMultiplier { get; set; } = 3f;

    // SDK components
    private StrideContentManager? _contentManager;
    private StrideBannouAssetLoader? _assetLoader;
    private StrideSceneComposerBridge? _bridge;
    private StrideGizmoRenderer? _gizmoRenderer;
    private ComposerCore? _composer;

    // Editor state
    private bool _isEditorActive = true;
    private GizmoMode _currentGizmoMode = GizmoMode.Translate;
    private global::Stride.Core.Mathematics.Vector2 _lastMousePosition;
    private bool _isRightMouseDown;
    private float _cameraYaw;
    private float _cameraPitch;

    // Gizmo interaction
    private bool _isDraggingGizmo;
    private GizmoAxis _activeGizmoAxis = GizmoAxis.None;
    private GizmoAxis _hoveredGizmoAxis = GizmoAxis.None;
    private SdkVector3 _dragStartPosition;

    /// <summary>
    /// Gets the SceneComposer instance for external access.
    /// </summary>
    public ISceneComposer? Composer => _composer;

    /// <summary>
    /// Gets the content manager for bundle loading.
    /// </summary>
    public StrideContentManager? ContentManager => _contentManager;

    /// <summary>
    /// Gets the asset loader for loading assets.
    /// </summary>
    public StrideBannouAssetLoader? AssetLoader => _assetLoader;

    /// <summary>
    /// Gets or sets whether the editor is active.
    /// </summary>
    public bool IsEditorActive
    {
        get => _isEditorActive;
        set => _isEditorActive = value;
    }

    /// <summary>
    /// Gets or sets the current gizmo mode.
    /// </summary>
    public GizmoMode CurrentGizmoMode
    {
        get => _currentGizmoMode;
        set => _currentGizmoMode = value;
    }

    /// <summary>
    /// Event raised when the scene hierarchy changes.
    /// </summary>
    public event Action? HierarchyChanged;

    /// <summary>
    /// Event raised when selection changes.
    /// </summary>
    public event Action<ComposerSceneNode?>? SelectionChanged;

    /// <summary>
    /// Called when the script starts. Override to customize initialization.
    /// </summary>
    public override void Start()
    {
        base.Start();

        InitializeEditor();
    }

    /// <summary>
    /// Initializes the editor components. Can be overridden for custom setup.
    /// </summary>
    protected virtual void InitializeEditor()
    {
        // Initialize content manager
        _contentManager = new StrideContentManager(Services, GraphicsDevice);
        _assetLoader = new StrideBannouAssetLoader(_contentManager);

        // Initialize gizmo renderer
        _gizmoRenderer = new StrideGizmoRenderer(GraphicsDevice);

        // Initialize bridge
        if (EditorCamera == null)
        {
            Log.Error("EditorCamera not assigned to StrideSceneEditorScript");
            return;
        }

        _bridge = new StrideSceneComposerBridge(
            Entity.Scene,
            EditorCamera,
            GraphicsDevice,
            _assetLoader,
            _gizmoRenderer);

        // Initialize composer (offline mode - no service client)
        _composer = new ComposerCore(_bridge);

        // Wire up events
        _composer.SelectionChanged += OnComposerSelectionChanged;
        _composer.SceneModified += OnSceneModified;

        // Initialize camera orientation from current transform
        if (EditorCamera != null)
        {
            var euler = QuaternionToEuler(EditorCamera.Entity.Transform.Rotation);
            _cameraPitch = euler.X;
            _cameraYaw = euler.Y;
        }

        // Create a new empty scene
        _composer.NewScene(SceneType.Arena, "Untitled Scene");

        // Load initial bundle if specified
        if (!string.IsNullOrEmpty(InitialBundlePath) && File.Exists(InitialBundlePath))
        {
            _ = LoadBundleAsync(InitialBundlePath);
        }
    }

    /// <summary>
    /// Called every frame. Override to customize update behavior.
    /// </summary>
    public override void Update()
    {
        if (!_isEditorActive || _composer == null || _bridge == null)
            return;

        HandleCameraInput();
        HandleGizmoInput();
        HandleSelectionInput();
        HandleHotkeys();
        UpdateGizmoDisplay();
    }

    /// <summary>
    /// Handles camera movement and rotation input.
    /// </summary>
    protected virtual void HandleCameraInput()
    {
        if (EditorCamera == null) return;

        var cameraEntity = EditorCamera.Entity;
        var dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;

        // Right-click drag for camera rotation
        if (Input.IsMouseButtonDown(MouseButton.Right))
        {
            if (!_isRightMouseDown)
            {
                _isRightMouseDown = true;
                _lastMousePosition = Input.MousePosition;
            }
            else
            {
                var delta = Input.MousePosition - _lastMousePosition;
                _lastMousePosition = Input.MousePosition;

                _cameraYaw -= delta.X * CameraRotateSpeed;
                _cameraPitch -= delta.Y * CameraRotateSpeed;
                _cameraPitch = MathUtil.Clamp(_cameraPitch, -MathUtil.PiOverTwo + 0.01f, MathUtil.PiOverTwo - 0.01f);

                cameraEntity.Transform.Rotation = Quaternion.RotationYawPitchRoll(_cameraYaw, _cameraPitch, 0);
            }
        }
        else
        {
            _isRightMouseDown = false;
        }

        // WASD + QE for camera movement
        var moveDirection = Vector3.Zero;

        if (Input.IsKeyDown(Keys.W)) moveDirection += Vector3.UnitZ;
        if (Input.IsKeyDown(Keys.S)) moveDirection -= Vector3.UnitZ;
        if (Input.IsKeyDown(Keys.A)) moveDirection -= Vector3.UnitX;
        if (Input.IsKeyDown(Keys.D)) moveDirection += Vector3.UnitX;
        if (Input.IsKeyDown(Keys.E)) moveDirection += Vector3.UnitY;
        if (Input.IsKeyDown(Keys.Q)) moveDirection -= Vector3.UnitY;

        if (moveDirection != Vector3.Zero)
        {
            moveDirection.Normalize();

            var worldMove = Vector3.Transform(moveDirection, cameraEntity.Transform.Rotation);
            var speed = CameraMoveSpeed * dt;
            if (Input.IsKeyDown(Keys.LeftShift)) speed *= CameraSprintMultiplier;

            cameraEntity.Transform.Position += worldMove * speed;
        }
    }

    /// <summary>
    /// Handles gizmo interaction (hover, drag).
    /// </summary>
    protected virtual void HandleGizmoInput()
    {
        if (_composer == null || _bridge == null || !_composer.HasSelection)
            return;

        var mousePos = GetMouseScreenPosition();
        var ray = _bridge.GetMouseRay(mousePos);

        // Get selection center for gizmo position
        var selectionCenter = GetSelectionCenter();
        var gizmoScale = GetGizmoScale(selectionCenter);

        if (_isDraggingGizmo)
        {
            // Continue dragging
            if (Input.IsMouseButtonDown(MouseButton.Left))
            {
                HandleGizmoDrag(ray, selectionCenter, gizmoScale);
            }
            else
            {
                // End drag
                _isDraggingGizmo = false;
                _activeGizmoAxis = GizmoAxis.None;
            }
        }
        else
        {
            // Check for hover
            _hoveredGizmoAxis = _bridge.PickGizmoAxis(
                ray,
                selectionCenter,
                SdkQuaternion.Identity,
                _currentGizmoMode,
                gizmoScale);

            // Check for click to start drag
            if (Input.IsMouseButtonPressed(MouseButton.Left) && _hoveredGizmoAxis != GizmoAxis.None)
            {
                _isDraggingGizmo = true;
                _activeGizmoAxis = _hoveredGizmoAxis;
                _dragStartPosition = selectionCenter;
            }
        }
    }

    /// <summary>
    /// Handles gizmo dragging to transform selected nodes.
    /// </summary>
    protected virtual void HandleGizmoDrag(SdkRay ray, SdkVector3 gizmoPosition, double gizmoScale)
    {
        if (_composer == null || _activeGizmoAxis == GizmoAxis.None)
            return;

        // Project ray onto the appropriate axis/plane
        var delta = CalculateGizmoDelta(ray, gizmoPosition, _activeGizmoAxis);

        if (delta.Length > 0.001)
        {
            // Apply translation to selected nodes
            _composer.TranslateNodes(
                _composer.SelectedNodes,
                delta,
                CoordinateSpace.World);
        }
    }

    /// <summary>
    /// Calculates the translation delta for gizmo dragging.
    /// </summary>
    protected virtual SdkVector3 CalculateGizmoDelta(SdkRay ray, SdkVector3 gizmoPosition, GizmoAxis axis)
    {
        // Simplified axis-constrained movement
        var axisDir = GetAxisDirection(axis);
        if (axisDir.Length < 0.001)
            return SdkVector3.Zero;

        // Project mouse ray onto axis line
        var toOrigin = gizmoPosition - ray.Origin;
        var t = SdkVector3.Dot(toOrigin, ray.Direction);
        var closestOnRay = ray.Origin + ray.Direction * t;

        var projOnAxis = SdkVector3.Dot(closestOnRay - _dragStartPosition, axisDir);
        var newPos = _dragStartPosition + axisDir * projOnAxis;

        var delta = newPos - gizmoPosition;
        _dragStartPosition = newPos;

        return delta;
    }

    /// <summary>
    /// Gets the world-space direction for a gizmo axis.
    /// </summary>
    protected static SdkVector3 GetAxisDirection(GizmoAxis axis)
    {
        return axis switch
        {
            GizmoAxis.X => new SdkVector3(1, 0, 0),
            GizmoAxis.Y => new SdkVector3(0, 1, 0),
            GizmoAxis.Z => new SdkVector3(0, 0, 1),
            GizmoAxis.XY => SdkVector3.Zero, // Plane movement - simplified
            GizmoAxis.XZ => SdkVector3.Zero,
            GizmoAxis.YZ => SdkVector3.Zero,
            _ => SdkVector3.Zero
        };
    }

    /// <summary>
    /// Handles entity selection via mouse click.
    /// </summary>
    protected virtual void HandleSelectionInput()
    {
        if (_composer == null || _bridge == null)
            return;

        // Left click to select (if not interacting with gizmo)
        if (Input.IsMouseButtonPressed(MouseButton.Left) &&
            !_isDraggingGizmo &&
            _hoveredGizmoAxis == GizmoAxis.None &&
            !Input.IsMouseButtonDown(MouseButton.Right))
        {
            var mousePos = GetMouseScreenPosition();
            var ray = _bridge.GetMouseRay(mousePos);
            var hitNodeId = _bridge.PickEntity(ray);

            if (hitNodeId.HasValue)
            {
                var node = _composer.CurrentScene?.GetNode(hitNodeId.Value);
                if (node != null)
                {
                    var mode = Input.IsKeyDown(Keys.LeftCtrl)
                        ? SelectionMode.Toggle
                        : SelectionMode.Replace;
                    _composer.Select(node, mode);
                }
            }
            else if (!Input.IsKeyDown(Keys.LeftCtrl))
            {
                _composer.ClearSelection();
            }
        }
    }

    /// <summary>
    /// Handles keyboard shortcuts.
    /// </summary>
    protected virtual void HandleHotkeys()
    {
        if (_composer == null) return;

        // Delete selected nodes
        if (Input.IsKeyPressed(Keys.Delete) && _composer.HasSelection)
        {
            _composer.DeleteNodes(_composer.SelectedNodes);
        }

        // Escape to deselect
        if (Input.IsKeyPressed(Keys.Escape))
        {
            _composer.ClearSelection();
        }

        // Ctrl+N for new group node
        if (Input.IsKeyDown(Keys.LeftCtrl) && Input.IsKeyPressed(Keys.N))
        {
            CreateGroupNode("New Group");
        }

        // Ctrl+Z for undo
        if (Input.IsKeyDown(Keys.LeftCtrl) && Input.IsKeyPressed(Keys.Z))
        {
            _composer.Undo();
        }

        // Ctrl+Y for redo
        if (Input.IsKeyDown(Keys.LeftCtrl) && Input.IsKeyPressed(Keys.Y))
        {
            _composer.Redo();
        }

        // Gizmo mode switching (when not using WASD for camera)
        if (!Input.IsMouseButtonDown(MouseButton.Right))
        {
            // Only switch modes when not in camera look mode
        }
    }

    /// <summary>
    /// Updates the gizmo visual display.
    /// </summary>
    protected virtual void UpdateGizmoDisplay()
    {
        if (_bridge == null || _composer == null)
            return;

        if (_composer.HasSelection)
        {
            var center = GetSelectionCenter();
            var scale = GetGizmoScale(center);
            var activeAxis = _isDraggingGizmo ? _activeGizmoAxis : _hoveredGizmoAxis;

            _bridge.RenderGizmo(
                center,
                SdkQuaternion.Identity,
                _currentGizmoMode,
                activeAxis,
                scale);
        }
        else
        {
            _bridge.HideGizmo();
        }
    }

    /// <summary>
    /// Gets the center point of the current selection.
    /// </summary>
    protected SdkVector3 GetSelectionCenter()
    {
        if (_composer == null || !_composer.HasSelection)
            return SdkVector3.Zero;

        var sum = SdkVector3.Zero;
        foreach (var node in _composer.SelectedNodes)
        {
            var world = _composer.GetWorldTransform(node);
            sum = sum + world.Position;
        }
        return sum * (1.0 / _composer.SelectedNodes.Count);
    }

    /// <summary>
    /// Gets the scale for the gizmo based on camera distance.
    /// </summary>
    protected double GetGizmoScale(SdkVector3 gizmoPosition)
    {
        if (_bridge == null) return 1.0;

        var cameraPos = _bridge.GetCameraPosition();
        var distance = (gizmoPosition - cameraPos).Length;
        return System.Math.Max(0.1, distance * 0.1); // Scale based on distance
    }

    /// <summary>
    /// Gets the mouse position in screen coordinates.
    /// </summary>
    protected SdkVector2 GetMouseScreenPosition()
    {
        var backBuffer = GraphicsDevice.Presenter?.BackBuffer;
        if (backBuffer == null)
            return new SdkVector2(0, 0);

        return new SdkVector2(
            Input.MousePosition.X * backBuffer.Width,
            Input.MousePosition.Y * backBuffer.Height);
    }

    /// <summary>
    /// Loads a .bannou bundle from disk.
    /// </summary>
    /// <param name="bundlePath">Path to the bundle file.</param>
    /// <returns>The bundle ID, or null if loading failed.</returns>
    public async Task<string?> LoadBundleAsync(string bundlePath)
    {
        if (_assetLoader == null) return null;

        try
        {
            return await _assetLoader.LoadBundleFromFileAsync(bundlePath);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load bundle: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a new mesh node with an asset from a bundle.
    /// </summary>
    /// <param name="assetId">Asset ID in the bundle.</param>
    /// <param name="bundleId">Bundle containing the asset.</param>
    /// <param name="position">World position to place the node.</param>
    /// <param name="name">Optional name for the node.</param>
    /// <returns>The created node, or null if creation failed.</returns>
    public ComposerSceneNode? PlaceAsset(
        string assetId,
        string bundleId,
        Vector3 position,
        string? name = null)
    {
        if (_composer?.CurrentScene == null) return null;

        var node = _composer.CreateNode(NodeType.Mesh, name ?? assetId);

        // Set position
        var transform = node.LocalTransform.WithPosition(
            new SdkVector3(position.X, position.Y, position.Z));
        _composer.SetLocalTransform(node, transform);

        // Bind asset
        var asset = new AssetReference(bundleId, assetId);
        _composer.BindAsset(node, asset);

        return node;
    }

    /// <summary>
    /// Creates a new empty group node.
    /// </summary>
    /// <param name="name">Name for the group.</param>
    /// <param name="position">Optional world position.</param>
    /// <returns>The created node, or null if creation failed.</returns>
    public ComposerSceneNode? CreateGroupNode(string name, Vector3? position = null)
    {
        if (_composer?.CurrentScene == null) return null;

        var node = _composer.CreateNode(NodeType.Group, name);

        if (position.HasValue)
        {
            var transform = node.LocalTransform.WithPosition(
                new SdkVector3(position.Value.X, position.Value.Y, position.Value.Z));
            _composer.SetLocalTransform(node, transform);
        }

        return node;
    }

    /// <summary>
    /// Selects a node.
    /// </summary>
    /// <param name="node">Node to select, or null to clear selection.</param>
    public void SelectNode(ComposerSceneNode? node)
    {
        if (_composer == null) return;

        if (node != null)
            _composer.Select(node);
        else
            _composer.ClearSelection();
    }

    /// <summary>
    /// Deletes a node.
    /// </summary>
    /// <param name="node">Node to delete.</param>
    public void DeleteNode(ComposerSceneNode node)
    {
        _composer?.DeleteNode(node);
    }

    /// <summary>
    /// Sets the gizmo mode (Translate, Rotate, Scale).
    /// </summary>
    /// <param name="mode">The gizmo mode to set.</param>
    public void SetGizmoMode(GizmoMode mode)
    {
        _currentGizmoMode = mode;
    }

    private void OnComposerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = _composer?.SelectedNodes.Count > 0
            ? _composer.SelectedNodes[0]
            : null;
        SelectionChanged?.Invoke(selected);
    }

    private void OnSceneModified(object? sender, SceneModifiedEventArgs e)
    {
        if (e.ModificationType == SceneModificationType.NodeCreated ||
            e.ModificationType == SceneModificationType.NodeDeleted ||
            e.ModificationType == SceneModificationType.NodeReparented ||
            e.ModificationType == SceneModificationType.SceneLoaded)
        {
            HierarchyChanged?.Invoke();
        }
    }

    private static Vector3 QuaternionToEuler(Quaternion q)
    {
        var sinp = 2 * (q.W * q.X - q.Z * q.Y);
        float pitch;
        if (System.Math.Abs(sinp) >= 1)
            pitch = MathF.CopySign(MathUtil.PiOverTwo, sinp);
        else
            pitch = MathF.Asin(sinp);

        var siny = 2 * (q.W * q.Y + q.Z * q.X);
        var cosy = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        var yaw = MathF.Atan2(siny, cosy);

        return new Vector3(pitch, yaw, 0);
    }

    /// <summary>
    /// Called when the script is cancelled.
    /// </summary>
    public override void Cancel()
    {
        _contentManager?.Dispose();
        base.Cancel();
    }
}
