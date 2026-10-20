using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Drawn over a tower built from stacked drums (SpireShape.drumLength), so its parts read like a model:
///   - fine ink lines down every corner of each drum and round its top and bottom edge,
///   - a thin glowing line in each recessed seam between two drums.
/// Nothing here collides; the drums themselves are the tower wall (LandmarkShapes.Spire).
/// </summary>
public static class SpireStructure
{
    private const float Lift = 0.03f;      // how far the lines stand off the wall
    private const float LineWidth = 0.07f;
    private const float GlowWidth = 0.16f;

    private static Material ink, glow;

    /// <summary>Adds the lines under the tower object (whose local space is the shape's space).</summary>
    public static void Build(Transform tower, SpireShape shape, Material spireMaterial)
    {
        if (!shape.HasDrums || tower == null) return;
        if (ink == null)
        {
            Color inkColour = spireMaterial != null && spireMaterial.HasProperty("_ScriptColor") ? spireMaterial.GetColor("_ScriptColor") : new Color(0.08f, 0.08f, 0.09f);
            Color glowColour = spireMaterial != null && spireMaterial.HasProperty("_GlowColor") ? spireMaterial.GetColor("_GlowColor") : new Color(0.3f, 0.9f, 1f);
            ink = Unlit(inkColour * 0.8f);
            glow = Unlit(Color.Lerp(glowColour, Color.white, 0.35f));
        }

        var lines = new Strips();
        var seams = new Strips();
        float bottomLimit = Mathf.Max(0.3f, shape.cutBelow + 0.3f);
        IReadOnlyList<SpireShape.Drum> drums = shape.Drums;
        for (int i = 0; i < drums.Count; i++)
        {
            SpireShape.Drum d = drums[i];
            float y0 = Mathf.Max(d.BodyBottom(shape.seamHeight) + 0.08f, bottomLimit);
            float y1 = Mathf.Min(d.y1, shape.height) - 0.08f;
            if (y1 - y0 < 0.5f) continue;

            // Down each corner.
            for (int c = 0; c < d.sides; c++)
            {
                float angle = d.twist + (c + 0.5f) * Mathf.PI * 2f / d.sides;
                if (shape.doorHalfAngle > 0f && Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, shape.doorAngle * Mathf.Rad2Deg)) < shape.doorHalfAngle * Mathf.Rad2Deg + 3f && y0 < shape.doorHeight)
                    continue;
                int steps = Mathf.Max(1, Mathf.CeilToInt((y1 - y0) / 1.4f));
                for (int s = 0; s < steps; s++)
                {
                    float ya = Mathf.Lerp(y0, y1, s / (float)steps), yb = Mathf.Lerp(y0, y1, (s + 1) / (float)steps);
                    if (Open(shape, angle, ya) || Open(shape, angle, yb)) continue;
                    lines.Vertical(shape, angle, ya, yb, LineWidth);
                }
            }
            // Round the top edge and, above the seam, the bottom edge.
            lines.Ring(shape, y1 - 0.1f, LineWidth);
            if (d.seam && d.BodyBottom(shape.seamHeight) > bottomLimit) lines.Ring(shape, d.BodyBottom(shape.seamHeight) + 0.12f, LineWidth);
            // The glowing line deep in the seam.
            if (d.seam && d.y0 > bottomLimit) seams.Ring(shape, d.y0 + shape.seamHeight * 0.5f, GlowWidth);
        }
        Add(tower, "Drum Edges", lines.ToMesh("Drum Edges"), ink);
        Add(tower, "Drum Seams", seams.ToMesh("Drum Seams"), glow);
    }

    // Door and window holes have no wall to draw on.
    private static bool Open(SpireShape shape, float angle, float y)
    {
        int k = Mathf.FloorToInt(Mathf.Repeat(angle, Mathf.PI * 2f) / (Mathf.PI * 2f) * shape.segments + 0.001f);
        for (int j = 0; j < shape.Rings; j++)
        {
            if (shape.RingY(j + 1) < y) continue;
            return shape.WindowCell(j, k) || shape.WindowCell(j, k - 1) || shape.DoorCell(j, k) || shape.DoorCell(j, k - 1);
        }
        return false;
    }

    private static void Add(Transform parent, string name, Mesh mesh, Material material)
    {
        if (mesh == null) return;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static Material Unlit(Color colour)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", colour);
        return m;
    }

    /// <summary>Thin flat strips lying just off the wall, facing outwards.</summary>
    private sealed class Strips
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        private static Vector3 OnWall(SpireShape shape, float angle, float y, float lift)
        {
            Vector3 axis = shape.Axis(y);
            float r = shape.Outer(angle, y) + lift;
            return new Vector3(axis.x + Mathf.Cos(angle) * r, y, axis.z + Mathf.Sin(angle) * r);
        }

        public void Vertical(SpireShape shape, float angle, float ya, float yb, float width)
        {
            Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (width * 0.5f);
            Vector3 a = OnWall(shape, angle, ya, Lift), b = OnWall(shape, angle, yb, Lift);
            Quad(a - tangent, a + tangent, b + tangent, b - tangent);
        }

        public void Ring(SpireShape shape, float y, float width)
        {
            int n = shape.segments * 2;
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                Vector3 p0 = OnWall(shape, a0, y, Lift), p1 = OnWall(shape, a1, y, Lift);
                Vector3 up = Vector3.up * (width * 0.5f);
                Quad(p0 - up, p1 - up, p1 + up, p0 + up);
            }
        }

        // Wound so the strips face away from the axis.
        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            Vector3 normal = Vector3.Cross(b - a, c - a);
            Vector3 outward = new Vector3(a.x + c.x, 0f, a.z + c.z) * 0.5f;
            if (Vector3.Dot(normal, outward) > 0f) { triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2); triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3); }
            else { triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 1); triangles.Add(i); triangles.Add(i + 3); triangles.Add(i + 2); }
        }

        public Mesh ToMesh(string name)
        {
            if (vertices.Count == 0) return null;
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave, indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
