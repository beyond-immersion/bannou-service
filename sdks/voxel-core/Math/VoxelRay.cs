using BeyondImmersion.Bannou.VoxelCore.Grid;

namespace BeyondImmersion.Bannou.VoxelCore.Math;

/// <summary>
/// Integer raycast through a voxel grid using the DDA (Digital Differential Analyzer) algorithm.
/// Reference: Amanatides &amp; Woo, "A Fast Voxel Traversal Algorithm for Ray Tracing" (Eurographics 1987).
/// Steps through voxels along a ray by comparing distances to the next boundary on each axis.
/// Integer arithmetic only in the hot loop.
/// </summary>
public struct VoxelRay
{
    private VoxelCoord _current;
    private int _stepX, _stepY, _stepZ;
    private double _tMaxX, _tMaxY, _tMaxZ;
    private double _tDeltaX, _tDeltaY, _tDeltaZ;
    private VoxelCoord _lastFace;

    /// <summary>
    /// Current voxel coordinate the ray is at.
    /// </summary>
    public VoxelCoord Current => _current;

    /// <summary>
    /// The face normal of the last step (indicates which face of the current voxel was entered).
    /// </summary>
    public VoxelCoord LastFace => _lastFace;

    /// <summary>
    /// Creates a new DDA ray from an origin voxel coordinate and a floating-point direction.
    /// </summary>
    /// <param name="origin">Starting voxel coordinate.</param>
    /// <param name="directionX">Ray direction X component.</param>
    /// <param name="directionY">Ray direction Y component.</param>
    /// <param name="directionZ">Ray direction Z component.</param>
    /// <returns>An initialized ray ready for stepping.</returns>
    public static VoxelRay Create(VoxelCoord origin, float directionX, float directionY, float directionZ)
    {
        var ray = new VoxelRay
        {
            _current = origin,
            _lastFace = VoxelCoord.Zero
        };

        ray._stepX = directionX > 0 ? 1 : (directionX < 0 ? -1 : 0);
        ray._stepY = directionY > 0 ? 1 : (directionY < 0 ? -1 : 0);
        ray._stepZ = directionZ > 0 ? 1 : (directionZ < 0 ? -1 : 0);

        // Distance between voxel boundaries along each axis
        ray._tDeltaX = directionX != 0 ? System.Math.Abs(1.0 / directionX) : double.MaxValue;
        ray._tDeltaY = directionY != 0 ? System.Math.Abs(1.0 / directionY) : double.MaxValue;
        ray._tDeltaZ = directionZ != 0 ? System.Math.Abs(1.0 / directionZ) : double.MaxValue;

        // Distance to the first voxel boundary from origin center (0.5 offset)
        ray._tMaxX = directionX > 0 ? 0.5 * ray._tDeltaX : (directionX < 0 ? 0.5 * ray._tDeltaX : double.MaxValue);
        ray._tMaxY = directionY > 0 ? 0.5 * ray._tDeltaY : (directionY < 0 ? 0.5 * ray._tDeltaY : double.MaxValue);
        ray._tMaxZ = directionZ > 0 ? 0.5 * ray._tDeltaZ : (directionZ < 0 ? 0.5 * ray._tDeltaZ : double.MaxValue);

        return ray;
    }

    /// <summary>
    /// Advances the ray by one voxel along the axis with the nearest boundary.
    /// </summary>
    /// <returns>The new voxel coordinate after stepping.</returns>
    public VoxelCoord Step()
    {
        if (_tMaxX < _tMaxY && _tMaxX < _tMaxZ)
        {
            _current = new VoxelCoord(_current.X + _stepX, _current.Y, _current.Z);
            _tMaxX += _tDeltaX;
            _lastFace = new VoxelCoord(-_stepX, 0, 0);
        }
        else if (_tMaxY < _tMaxZ)
        {
            _current = new VoxelCoord(_current.X, _current.Y + _stepY, _current.Z);
            _tMaxY += _tDeltaY;
            _lastFace = new VoxelCoord(0, -_stepY, 0);
        }
        else
        {
            _current = new VoxelCoord(_current.X, _current.Y, _current.Z + _stepZ);
            _tMaxZ += _tDeltaZ;
            _lastFace = new VoxelCoord(0, 0, -_stepZ);
        }

        return _current;
    }

    /// <summary>
    /// Casts the ray through a voxel grid, returning the first non-empty voxel hit
    /// and the face normal of the entry face.
    /// </summary>
    /// <param name="grid">The voxel grid to cast through.</param>
    /// <param name="maxSteps">Maximum number of voxel steps before giving up.</param>
    /// <returns>
    /// A tuple of (hit coordinate, face normal coordinate) if a non-empty voxel was hit,
    /// or null if no hit within maxSteps.
    /// </returns>
    public (VoxelCoord Hit, VoxelCoord FaceNormal)? Cast(VoxelGrid grid, int maxSteps)
    {
        for (var i = 0; i < maxSteps; i++)
        {
            if (grid.Contains(_current) && !grid.IsEmpty(_current))
                return (_current, _lastFace);

            Step();
        }

        return null;
    }
}
