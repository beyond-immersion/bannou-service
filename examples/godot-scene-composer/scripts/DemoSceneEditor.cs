using System.Collections.Generic;
using System.Linq;
using Godot;
using BeyondImmersion.Bannou.SceneComposer.Godot.Bridge;
using BeyondImmersion.Bannou.SceneComposer.Abstractions;
using BeyondImmersion.Bannou.SceneComposer.Events;
using BeyondImmersion.Bannou.SceneComposer.Gizmo;
using BeyondImmersion.Bannou.SceneComposer.SceneGraph;
using BeyondImmersion.Bannou.SceneComposer.Selection;
using SdkVec3 = BeyondImmersion.Bannou.SceneComposer.Math.Vector3;
using SdkTransform = BeyondImmersion.Bannou.SceneComposer.Math.Transform;
using ComposerCore = BeyondImmersion.Bannou.SceneComposer.SceneComposer;

namespace BeyondImmersion.Bannou.SceneComposer.Godot.Demo;

/// <summary>
/// Demo scene editor showing basic SDK integration.
/// Provides camera controls, entity selection, and gizmo interaction.
/// </summary>
public partial class DemoSceneEditor : Node3D
{
    #region Exports

    /// <summary>
    /// Camera for editor viewport.
    /// </summary>
    [Export]
    public Camera3D? EditorCamera { get; set; }

    /// <summary>
    /// Root node where entities will be created.
    /// </summary>
    [Export]
    public Node3D? SceneRoot { get; set; }

    /// <summary>
    /// Camera movement speed (units per second).
    /// </summary>
    [Export]
    public float CameraMoveSpeed { get; set; } = 5.0f;

    /// <summary>
    /// Camera rotation speed (radians per pixel).
    /// </summary>
    [Export]
    public float CameraRotateSpeed { get; set; } = 0.003f;

    /// <summary>
    /// Camera zoom speed (units per scroll tick).
    /// </summary>
    [Export]
    public float CameraZoomSpeed { get; set; } = 1.0f;

    #endregion

    #region Private Fields

    private GodotSceneComposerBridge? _bridge;
    private ComposerCore? _composer;
    private GizmoMode _currentGizmoMode = GizmoMode.Translate;
    private bool _isMousePressed;
    private global::Godot.Vector2 _lastMousePosition;

    // Map from entity GUID to scene node for picking
    private readonly Dictionary<System.Guid, ComposerSceneNode> _entityNodeMap = new();

    #endregion

    #region Lifecycle

    public override void _Ready()
    {
        // Auto-assign if not set in editor
        if (EditorCamera == null)
        {
            EditorCamera = GetNode<Camera3D>("Camera3D");
        }

        if (SceneRoot == null)
        {
            SceneRoot = GetNode<Node3D>("SceneRoot");
            if (SceneRoot == null)
            {
                SceneRoot = new Node3D { Name = "SceneRoot" };
                AddChild(SceneRoot);
            }
        }

        InitializeEditor();
        CreateSampleScene();
    }

    public override void _Process(double delta)
    {
        if (_composer == null || _bridge == null || EditorCamera == null)
        {
            return;
        }

        HandleCameraInput(delta);
        HandleSelectionInput();
        HandleGizmoModeInput();
        UpdateGizmoDisplay();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                _isMousePressed = mouseButton.Pressed;
                if (mouseButton.Pressed)
                {
                    _lastMousePosition = mouseButton.Position;
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                }
                else
                {
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                }
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                ZoomCamera(-CameraZoomSpeed);
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                ZoomCamera(CameraZoomSpeed);
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion)
        {
            if (_isMousePressed && EditorCamera != null)
            {
                // Rotate camera with right-click drag
                EditorCamera.RotateY(-mouseMotion.Relative.X * CameraRotateSpeed);
                EditorCamera.RotateObjectLocal(Vector3.Right, -mouseMotion.Relative.Y * CameraRotateSpeed);
            }
        }
    }

    #endregion

    #region Initialization

    private void InitializeEditor()
    {
        if (EditorCamera == null || SceneRoot == null)
        {
            GD.PushError("DemoSceneEditor: Camera or SceneRoot not assigned");
            return;
        }

        // Create bridge and composer
        _bridge = new GodotSceneComposerBridge(SceneRoot, EditorCamera, GetViewport());
        _composer = new ComposerCore(_bridge);

        // Subscribe to events
        _composer.SelectionChanged += OnSelectionChanged;
        _composer.SceneModified += OnSceneModified;

        // Create new scene
        _composer.NewScene(SceneType.Arena, "Demo Scene");

        GD.Print("Scene Composer initialized");
    }

    private void CreateSampleScene()
    {
        if (_composer == null)
        {
            return;
        }

        // Create some sample entities
        var cube1 = _composer.CreateNode(NodeType.Mesh, "Cube 1");
        _composer.TranslateNode(cube1, new SdkVec3(0, 0, 0), CoordinateSpace.World);
        _entityNodeMap[cube1.Id] = cube1;

        var cube2 = _composer.CreateNode(NodeType.Mesh, "Cube 2");
        _composer.TranslateNode(cube2, new SdkVec3(3, 0, 0), CoordinateSpace.World);
        _entityNodeMap[cube2.Id] = cube2;

        var cube3 = _composer.CreateNode(NodeType.Mesh, "Cube 3");
        _composer.TranslateNode(cube3, new SdkVec3(-3, 0, 0), CoordinateSpace.World);
        _entityNodeMap[cube3.Id] = cube3;

        // Create a group with children
        var group = _composer.CreateNode(NodeType.Group, "Group 1");
        _composer.TranslateNode(group, new SdkVec3(0, 0, 3), CoordinateSpace.World);
        _entityNodeMap[group.Id] = group;

        var childCube = _composer.CreateNode(NodeType.Mesh, "Child Cube", group);
        _composer.TranslateNode(childCube, new SdkVec3(0, 1, 0), CoordinateSpace.Local);
        _entityNodeMap[childCube.Id] = childCube;

        // Create a marker
        var marker = _composer.CreateNode(NodeType.Marker, "Spawn Point");
        _composer.TranslateNode(marker, new SdkVec3(5, 0, 5), CoordinateSpace.World);
        _entityNodeMap[marker.Id] = marker;

        var nodeCount = _composer.CurrentScene?.GetAllNodes().Count() ?? 0;
        GD.Print($"Created sample scene with {nodeCount} nodes");
    }

    #endregion

    #region Input Handling

    private void HandleCameraInput(double delta)
    {
        if (EditorCamera == null)
        {
            return;
        }

        // WASD camera movement
        var moveDir = Vector3.Zero;

        if (Input.IsKeyPressed(Key.W))
        {
            moveDir -= EditorCamera.GlobalTransform.Basis.Z;
        }
        if (Input.IsKeyPressed(Key.S))
        {
            moveDir += EditorCamera.GlobalTransform.Basis.Z;
        }
        if (Input.IsKeyPressed(Key.A))
        {
            moveDir -= EditorCamera.GlobalTransform.Basis.X;
        }
        if (Input.IsKeyPressed(Key.D))
        {
            moveDir += EditorCamera.GlobalTransform.Basis.X;
        }
        if (Input.IsKeyPressed(Key.Q))
        {
            moveDir -= Vector3.Up;
        }
        if (Input.IsKeyPressed(Key.E))
        {
            moveDir += Vector3.Up;
        }

        if (moveDir != Vector3.Zero)
        {
            var speed = Input.IsKeyPressed(Key.Shift) ? CameraMoveSpeed * 3 : CameraMoveSpeed;
            EditorCamera.GlobalPosition += moveDir.Normalized() * speed * (float)delta;
        }
    }

    private void HandleSelectionInput()
    {
        if (_composer == null || _bridge == null)
        {
            return;
        }

        // Left click to select
        if (Input.IsActionJustPressed("ui_accept") || Input.IsMouseButtonPressed(MouseButton.Left))
        {
            var mousePos = GetViewport().GetMousePosition();
            var ray = _bridge.GetMouseRay(new BeyondImmersion.Bannou.SceneComposer.Abstractions.Vector2(mousePos.X, mousePos.Y));

            var hitId = _bridge.PickEntity(ray);
            if (hitId.HasValue && _entityNodeMap.TryGetValue(hitId.Value, out var hitNode))
            {
                // Check for shift-click (add to selection)
                if (Input.IsKeyPressed(Key.Shift))
                {
                    _composer.Select(hitNode, SelectionMode.Toggle);
                }
                else
                {
                    _composer.Select(hitNode, SelectionMode.Replace);
                }
            }
            else
            {
                // Click on empty space - clear selection
                _composer.ClearSelection();
            }
        }

        // Delete key to remove selected
        if (Input.IsKeyPressed(Key.Delete))
        {
            var nodesToDelete = _composer.SelectedNodes.ToList();
            foreach (var node in nodesToDelete)
            {
                _entityNodeMap.Remove(node.Id);
                _composer.DeleteNode(node);
            }
        }

        // Ctrl+Z to undo
        if (Input.IsKeyPressed(Key.Ctrl) && Input.IsKeyPressed(Key.Z))
        {
            if (!Input.IsKeyPressed(Key.Shift))
            {
                _composer.Undo();
            }
            else
            {
                _composer.Redo();
            }
        }

        // Ctrl+Y to redo
        if (Input.IsKeyPressed(Key.Ctrl) && Input.IsKeyPressed(Key.Y))
        {
            _composer.Redo();
        }
    }

    private void HandleGizmoModeInput()
    {
        // G = Translate (grab)
        if (Input.IsKeyPressed(Key.G))
        {
            _currentGizmoMode = GizmoMode.Translate;
            GD.Print("Gizmo mode: Translate");
        }

        // R = Rotate
        if (Input.IsKeyPressed(Key.R))
        {
            _currentGizmoMode = GizmoMode.Rotate;
            GD.Print("Gizmo mode: Rotate");
        }

        // S = Scale
        if (Input.IsKeyPressed(Key.Key1) && Input.IsKeyPressed(Key.Shift))
        {
            _currentGizmoMode = GizmoMode.Scale;
            GD.Print("Gizmo mode: Scale");
        }

        // Escape = No gizmo
        if (Input.IsKeyPressed(Key.Escape))
        {
            _currentGizmoMode = GizmoMode.None;
            _composer?.ClearSelection();
        }
    }

    private void ZoomCamera(float amount)
    {
        if (EditorCamera == null)
        {
            return;
        }

        // Move camera forward/backward along its look direction
        var forward = -EditorCamera.GlobalTransform.Basis.Z;
        EditorCamera.GlobalPosition += forward * amount;
    }

    #endregion

    #region Gizmo

    private void UpdateGizmoDisplay()
    {
        if (_composer == null || _bridge == null)
        {
            return;
        }

        if (_composer.SelectedNodes.Count == 0 || _currentGizmoMode == GizmoMode.None)
        {
            _bridge.HideGizmo();
            return;
        }

        // Get center of selection
        var center = SdkVec3.Zero;
        var count = 0;

        foreach (var node in _composer.SelectedNodes)
        {
            var worldTransform = node.GetWorldTransform();
            center += worldTransform.Position;
            count++;
        }

        if (count > 0)
        {
            center /= count;

            // Calculate gizmo scale based on camera distance
            var camPos = _bridge.GetCameraPosition();
            var distance = SdkVec3.Distance(camPos, center);
            var scale = distance * 0.15; // Constant screen-space size

            _bridge.RenderGizmo(center, BeyondImmersion.Bannou.SceneComposer.Math.Quaternion.Identity, _currentGizmoMode, GizmoAxis.None, scale);
        }
    }

    #endregion

    #region Event Handlers

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        GD.Print($"Selection changed: {e.SelectedNodes.Count} nodes selected");
    }

    private void OnSceneModified(object? sender, SceneModifiedEventArgs e)
    {
        GD.Print($"Scene modified: {e.ModificationType}");
    }

    #endregion
}
