using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A splat of goo left where a thrown goo blob hits a wall or the ground (like paint in Splatoon or the gels in
/// Portal 2). Hands can climb on it without goo of their own and without using any up; off the splat the
/// normal rules apply. The splat has a thin Ground-layer collider, so even smooth unclimbable surfaces can be
/// gripped where it lies. The oldest splats dry up when there are too many.
/// </summary>
public sealed class GooPaint : MonoBehaviour
{
    private static readonly List<GooPaint> All = new List<GooPaint>();
    private const int MaxSplats = 24;

    [Min(0.3f)] public float radius = 1.4f;

    private Vector3 normal;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => All.Clear();

    /// <summary>Leaves a splat at a surface point. The material is the goo blob's own.</summary>
    public static GooPaint Splat(Vector3 point, Vector3 surfaceNormal, Material material, float size = 1.4f)
    {
        var go = new GameObject("Goo Paint") { layer = 7 };
        go.transform.SetPositionAndRotation(point + surfaceNormal * 0.02f, Quaternion.LookRotation(-surfaceNormal, Mathf.Abs(surfaceNormal.y) < 0.9f ? Vector3.up : Vector3.forward));
        var paint = go.AddComponent<GooPaint>();
        paint.radius = size;
        paint.normal = surfaceNormal.normalized;
        paint.Build(material);
        All.Add(paint);
        while (All.Count > MaxSplats)
        {
            GooPaint oldest = All[0];
            All.RemoveAt(0);
            if (oldest != null) Destroy(oldest.gameObject);
        }
        return paint;
    }

    /// <summary>True when a point lies on a splat (within its radius and close to its surface).</summary>
    public static bool Covers(Vector3 point, float margin = 0.25f)
    {
        foreach (GooPaint paint in All)
        {
            if (paint == null) continue;
            Vector3 offset = point - paint.transform.position;
            float off = Vector3.Dot(offset, paint.normal);
            if (Mathf.Abs(off) > 0.45f) continue;
            if ((offset - paint.normal * off).magnitude <= paint.radius + margin) return true;
        }
        return false;
    }

    /// <summary>True when any splat is within reach of a point (for starting a climb).</summary>
    public static bool Near(Vector3 point, float reach)
    {
        foreach (GooPaint paint in All)
            if (paint != null && (paint.transform.position - point).magnitude <= reach + paint.radius) return true;
        return false;
    }

    // A flat, ragged splat: a disc whose edge wobbles, with a few drips on walls.
    private void Build(Material material)
    {
        var random = new System.Random(GetInstanceID());
        const int sides = 22;
        var vertices = new List<Vector3> { Vector3.zero };
        var triangles = new List<int>();
        for (int i = 0; i < sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            float r = radius * Mathf.Lerp(0.7f, 1.1f, (float)random.NextDouble());
            vertices.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
        }
        for (int i = 0; i < sides; i++)
        {
            triangles.Add(0);
            triangles.Add(1 + (i + 1) % sides);
            triangles.Add(1 + i);
        }
        // Drips running down a wall.
        if (Mathf.Abs(normal.y) < 0.6f)
        {
            Vector3 down = transform.InverseTransformDirection(Vector3.down);
            Vector3 across = Vector3.Cross(down, Vector3.forward).normalized;
            for (int d = 0; d < 3; d++)
            {
                float x = ((float)random.NextDouble() - 0.5f) * radius * 1.2f;
                float length = radius * Mathf.Lerp(0.6f, 1.3f, (float)random.NextDouble());
                Vector3 start = across * x + down * radius * 0.5f;
                int b = vertices.Count;
                vertices.Add(start - across * 0.09f);
                vertices.Add(start + across * 0.09f);
                vertices.Add(start + down * length);
                triangles.Add(b); triangles.Add(b + 2); triangles.Add(b + 1);
            }
        }
        var mesh = new Mesh { name = "Goo Paint" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;

        // A thin grip surface just above the wall, so the splat can be held even where the wall itself cannot.
        var grip = gameObject.AddComponent<BoxCollider>();
        grip.size = new Vector3(radius * 1.8f, radius * 1.8f, 0.08f);
        grip.center = new Vector3(0f, 0f, 0.03f);
    }

    private void OnDestroy() => All.Remove(this);
}
