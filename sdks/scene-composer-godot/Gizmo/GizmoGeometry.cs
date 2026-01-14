using Godot;

namespace BeyondImmersion.Bannou.Godot.SceneComposer.Gizmo;

/// <summary>
/// Pure geometry generation functions for gizmo rendering.
/// These methods are stateless and testable.
/// </summary>
public static class GizmoGeometry
{
    /// <summary>
    /// Generate vertices for an arrow (cylinder + cone) along the +Z axis.
    /// </summary>
    /// <param name="shaftLength">Length of the arrow shaft.</param>
    /// <param name="shaftRadius">Radius of the arrow shaft.</param>
    /// <param name="headLength">Length of the arrow head cone.</param>
    /// <param name="headRadius">Radius of the arrow head cone base.</param>
    /// <param name="segments">Number of circular segments (higher = smoother).</param>
    /// <returns>Array of vertices forming the arrow mesh.</returns>
    public static Vector3[] GenerateArrowVertices(
        float shaftLength = 0.8f,
        float shaftRadius = 0.02f,
        float headLength = 0.2f,
        float headRadius = 0.06f,
        int segments = 12)
    {
        var vertices = new List<Vector3>();

        // Generate shaft cylinder
        GenerateCylinderVertices(vertices, shaftRadius, shaftLength, segments);

        // Generate head cone (starting at shaftLength, pointing to shaftLength + headLength)
        GenerateConeVertices(vertices, headRadius, headLength, shaftLength, segments);

        return vertices.ToArray();
    }

    /// <summary>
    /// Generate vertices for a cylinder along the +Z axis.
    /// </summary>
    public static void GenerateCylinderVertices(
        List<Vector3> vertices,
        float radius,
        float length,
        int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            float angle1 = (float)(i * 2 * System.Math.PI / segments);
            float angle2 = (float)((i + 1) * 2 * System.Math.PI / segments);

            float x1 = (float)System.Math.Cos(angle1) * radius;
            float y1 = (float)System.Math.Sin(angle1) * radius;
            float x2 = (float)System.Math.Cos(angle2) * radius;
            float y2 = (float)System.Math.Sin(angle2) * radius;

            // Bottom cap
            vertices.Add(new Vector3(0, 0, 0));
            vertices.Add(new Vector3(x2, y2, 0));
            vertices.Add(new Vector3(x1, y1, 0));

            // Top cap
            vertices.Add(new Vector3(0, 0, length));
            vertices.Add(new Vector3(x1, y1, length));
            vertices.Add(new Vector3(x2, y2, length));

            // Side quad (2 triangles)
            vertices.Add(new Vector3(x1, y1, 0));
            vertices.Add(new Vector3(x2, y2, 0));
            vertices.Add(new Vector3(x1, y1, length));

            vertices.Add(new Vector3(x2, y2, 0));
            vertices.Add(new Vector3(x2, y2, length));
            vertices.Add(new Vector3(x1, y1, length));
        }
    }

    /// <summary>
    /// Generate vertices for a cone along the +Z axis with apex at base + height.
    /// </summary>
    public static void GenerateConeVertices(
        List<Vector3> vertices,
        float radius,
        float height,
        float baseZ,
        int segments)
    {
        float apexZ = baseZ + height;

        for (int i = 0; i < segments; i++)
        {
            float angle1 = (float)(i * 2 * System.Math.PI / segments);
            float angle2 = (float)((i + 1) * 2 * System.Math.PI / segments);

            float x1 = (float)System.Math.Cos(angle1) * radius;
            float y1 = (float)System.Math.Sin(angle1) * radius;
            float x2 = (float)System.Math.Cos(angle2) * radius;
            float y2 = (float)System.Math.Sin(angle2) * radius;

            // Base cap
            vertices.Add(new Vector3(0, 0, baseZ));
            vertices.Add(new Vector3(x2, y2, baseZ));
            vertices.Add(new Vector3(x1, y1, baseZ));

            // Side triangle
            vertices.Add(new Vector3(x1, y1, baseZ));
            vertices.Add(new Vector3(x2, y2, baseZ));
            vertices.Add(new Vector3(0, 0, apexZ));
        }
    }

    /// <summary>
    /// Generate vertices for a rotation ring (torus segment) in the XY plane.
    /// </summary>
    /// <param name="majorRadius">Radius of the ring center.</param>
    /// <param name="minorRadius">Thickness radius of the tube.</param>
    /// <param name="majorSegments">Number of segments around the ring.</param>
    /// <param name="minorSegments">Number of segments around the tube cross-section.</param>
    /// <returns>Array of vertices forming the ring mesh.</returns>
    public static Vector3[] GenerateRingVertices(
        float majorRadius = 0.8f,
        float minorRadius = 0.02f,
        int majorSegments = 32,
        int minorSegments = 8)
    {
        var vertices = new List<Vector3>();

        for (int i = 0; i < majorSegments; i++)
        {
            float majorAngle1 = (float)(i * 2 * System.Math.PI / majorSegments);
            float majorAngle2 = (float)((i + 1) * 2 * System.Math.PI / majorSegments);

            for (int j = 0; j < minorSegments; j++)
            {
                float minorAngle1 = (float)(j * 2 * System.Math.PI / minorSegments);
                float minorAngle2 = (float)((j + 1) * 2 * System.Math.PI / minorSegments);

                // 4 corners of the quad
                var p1 = GetTorusPoint(majorRadius, minorRadius, majorAngle1, minorAngle1);
                var p2 = GetTorusPoint(majorRadius, minorRadius, majorAngle2, minorAngle1);
                var p3 = GetTorusPoint(majorRadius, minorRadius, majorAngle2, minorAngle2);
                var p4 = GetTorusPoint(majorRadius, minorRadius, majorAngle1, minorAngle2);

                // Quad as 2 triangles
                vertices.Add(p1);
                vertices.Add(p2);
                vertices.Add(p3);

                vertices.Add(p1);
                vertices.Add(p3);
                vertices.Add(p4);
            }
        }

        return vertices.ToArray();
    }

    /// <summary>
    /// Get a point on a torus surface.
    /// </summary>
    private static Vector3 GetTorusPoint(float majorRadius, float minorRadius, float majorAngle, float minorAngle)
    {
        float cosMajor = (float)System.Math.Cos(majorAngle);
        float sinMajor = (float)System.Math.Sin(majorAngle);
        float cosMinor = (float)System.Math.Cos(minorAngle);
        float sinMinor = (float)System.Math.Sin(minorAngle);

        float r = majorRadius + minorRadius * cosMinor;

        return new Vector3(
            r * cosMajor,
            r * sinMajor,
            minorRadius * sinMinor
        );
    }

    /// <summary>
    /// Generate vertices for a scale cube handle.
    /// </summary>
    /// <param name="size">Size of the cube.</param>
    /// <param name="offsetZ">Distance from origin along Z axis.</param>
    /// <returns>Array of vertices forming the cube mesh.</returns>
    public static Vector3[] GenerateScaleCubeVertices(float size = 0.1f, float offsetZ = 0.9f)
    {
        var vertices = new List<Vector3>();

        float half = size / 2;
        var center = new Vector3(0, 0, offsetZ);

        // Define 8 corners
        var corners = new Vector3[]
        {
            center + new Vector3(-half, -half, -half),
            center + new Vector3(half, -half, -half),
            center + new Vector3(half, half, -half),
            center + new Vector3(-half, half, -half),
            center + new Vector3(-half, -half, half),
            center + new Vector3(half, -half, half),
            center + new Vector3(half, half, half),
            center + new Vector3(-half, half, half),
        };

        // 6 faces (2 triangles each)
        // Front (-Z)
        AddQuad(vertices, corners[0], corners[1], corners[2], corners[3]);
        // Back (+Z)
        AddQuad(vertices, corners[5], corners[4], corners[7], corners[6]);
        // Left (-X)
        AddQuad(vertices, corners[4], corners[0], corners[3], corners[7]);
        // Right (+X)
        AddQuad(vertices, corners[1], corners[5], corners[6], corners[2]);
        // Bottom (-Y)
        AddQuad(vertices, corners[4], corners[5], corners[1], corners[0]);
        // Top (+Y)
        AddQuad(vertices, corners[3], corners[2], corners[6], corners[7]);

        return vertices.ToArray();
    }

    /// <summary>
    /// Add a quad (2 triangles) to the vertex list.
    /// </summary>
    private static void AddQuad(List<Vector3> vertices, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        vertices.Add(p0);
        vertices.Add(p1);
        vertices.Add(p2);

        vertices.Add(p0);
        vertices.Add(p2);
        vertices.Add(p3);
    }

    /// <summary>
    /// Transform vertices from +Z axis orientation to a specified axis direction.
    /// </summary>
    /// <param name="vertices">Input vertices oriented along +Z.</param>
    /// <param name="axis">Target axis (X, Y, or Z).</param>
    /// <returns>Transformed vertices.</returns>
    public static Vector3[] TransformToAxis(Vector3[] vertices, GizmoAxisDirection axis)
    {
        var result = new Vector3[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            result[i] = axis switch
            {
                GizmoAxisDirection.X => new Vector3(vertices[i].Z, vertices[i].Y, -vertices[i].X),
                GizmoAxisDirection.Y => new Vector3(vertices[i].X, vertices[i].Z, -vertices[i].Y),
                GizmoAxisDirection.Z => vertices[i],
                _ => vertices[i]
            };
        }

        return result;
    }

    /// <summary>
    /// Scale all vertices by a uniform factor.
    /// </summary>
    public static Vector3[] ScaleVertices(Vector3[] vertices, float scale)
    {
        var result = new Vector3[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            result[i] = vertices[i] * scale;
        }

        return result;
    }

    /// <summary>
    /// Translate all vertices by an offset.
    /// </summary>
    public static Vector3[] TranslateVertices(Vector3[] vertices, Vector3 offset)
    {
        var result = new Vector3[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            result[i] = vertices[i] + offset;
        }

        return result;
    }
}

/// <summary>
/// Gizmo axis direction.
/// </summary>
public enum GizmoAxisDirection
{
    /// <summary>X axis (right).</summary>
    X,
    /// <summary>Y axis (up).</summary>
    Y,
    /// <summary>Z axis (forward).</summary>
    Z
}
