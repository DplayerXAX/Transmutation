using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Themed scatter round the landmarks, so each one sits in its own patch of the world:
///   Hanging Spire: chunks of masonry drifting under the cut ends of the floating towers, fallen blocks
///   half sunk in the ground round the bases, and a few small carved tablets on the way in.
///   Bone Kiln: bone shards and loose vertebrae half buried round the mouth, glassy rib tips breaking
///   out of the ground, and the designed flora growing among them.
/// All of it is decoration: drifting pieces have no colliders; ground pieces are solid so they can be stood on.
/// </summary>
public static class LandmarkDecor
{
    public static void Spire(Transform root, Vector3 basePoint, SpireShape spire, IReadOnlyList<Transform> stageTowers,
                             Material stone, Material glyph, int seed)
    {
        var random = new System.Random(seed * 7349 + 11);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var parent = new GameObject("Decor").transform;
        parent.SetParent(root, false);

        // Drifting masonry under the cut end of the spire.
        if (spire.cutBelow > 0f)
            Drift(parent, basePoint + spire.Axis(spire.cutBelow), spire.Outer(0f, spire.cutBelow), spire.cutBelow, 14, stone, random);
        // ...and under the second tower, if it floats.
        if (stageTowers != null && stageTowers.Count > 1)
        {
            Transform tower = stageTowers[1];
            Bounds bounds = tower.GetComponent<Collider>() != null ? tower.GetComponent<Collider>().bounds : new Bounds(tower.position, Vector3.one);
            float cutY = bounds.min.y;
            Drift(parent, new Vector3(tower.position.x, cutY, tower.position.z), 4.5f, cutY - tower.position.y, 8, stone, random);
        }

        // Fallen blocks round the spire's base and the first tower.
        var bases = new List<(Vector3 centre, float radius)> { (basePoint, spire.Outer(0f, 0f) + 4f) };
        if (stageTowers != null && stageTowers.Count > 0) bases.Add((stageTowers[0].position, 7f));
        foreach (var (centre, radius) in bases)
        {
            for (int i = 0; i < 9; i++)
            {
                float angle = R(0f, Mathf.PI * 2f);
                Vector3 p = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (radius + R(0f, 12f));
                if (!Ground(p, out Vector3 ground, out Vector3 normal)) continue;
                Vector3 size = new Vector3(R(1f, 2.6f), R(0.6f, 1.4f), R(1f, 3f));
                GameObject block = Block(parent, "Fallen Block", size, stone, random, solid: true);
                block.transform.SetPositionAndRotation(ground - normal * size.y * 0.35f,
                    Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(R(-25f, 25f), R(0f, 360f), R(-25f, 25f)));
            }
        }

        // Small carved tablets on the way in: a stone slab with a sign cut into its face.
        for (int i = 0; i < 4; i++)
        {
            float angle = R(0f, Mathf.PI * 2f);
            Vector3 p = basePoint + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (spire.Outer(0f, 0f) + R(14f, 26f));
            if (!Ground(p, out Vector3 ground, out _)) continue;
            Vector3 face = (new Vector3(basePoint.x, ground.y, basePoint.z) - ground).normalized;
            GameObject tablet = Block(parent, "Tablet", new Vector3(1.4f, 2.2f, 0.4f), stone, random, solid: true);
            tablet.transform.SetPositionAndRotation(ground + Vector3.up * 0.8f, Quaternion.LookRotation(-face, Vector3.up) * Quaternion.Euler(R(-6f, 6f), 0f, R(-6f, 6f)));
            if (glyph == null) continue;
            var sign = new GameObject("Sign");
            sign.transform.SetParent(tablet.transform, false);
            sign.transform.localPosition = new Vector3(0f, -0.65f, -0.22f);
            sign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            sign.AddComponent<MeshFilter>().sharedMesh = LandmarkShapes.Glyph(LandmarkGlyphs.Get("tablet" + (seed + i)), 0.8f, 0.06f);
            var r = sign.AddComponent<MeshRenderer>();
            r.sharedMaterial = glyph;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    public static void Kiln(Transform root, Vector3 mouth, float holeRadius, System.Func<Vector3, float> groundHeight,
                            Material bone, Material ribGlass, Material flora, int seed)
    {
        var random = new System.Random(seed * 9173 + 3);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var parent = new GameObject("Decor").transform;
        parent.SetParent(root, false);
        Vector3 Around(float min, float max)
        {
            float angle = R(0f, Mathf.PI * 2f);
            Vector3 p = mouth + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (holeRadius + R(min, max));
            p.y = groundHeight(p);
            return p;
        }

        // Bone shards, half buried.
        for (int i = 0; i < 20; i++)
        {
            Vector3 p = Around(3f, 30f);
            float length = R(1.2f, 4f), radius = R(0.2f, 0.5f);
            GameObject shard = Mesh(parent, "Bone Shard", BoneShapes.Segment(Vector3.zero, Vector3.forward * length, radius, radius * R(0.5f, 1f), (float)random.NextDouble()), bone, solid: true);
            shard.transform.SetPositionAndRotation(p + Vector3.down * radius * 0.4f, Quaternion.Euler(R(-30f, 10f), R(0f, 360f), R(0f, 360f)));
        }
        // Loose vertebrae.
        for (int i = 0; i < 7; i++)
        {
            Vector3 p = Around(4f, 24f);
            var builder = new BoneShapes.Builder();
            float r = R(0.5f, 1.1f);
            BoneShapes.Vertebra(builder, Vector3.zero, Vector3.forward, Vector3.up, r, (float)random.NextDouble());
            GameObject vertebra = Mesh(parent, "Loose Vertebra", builder.ToMesh("Loose Vertebra"), bone, solid: true);
            vertebra.transform.SetPositionAndRotation(p + Vector3.down * r * 0.5f, Quaternion.Euler(R(-40f, 40f), R(0f, 360f), R(-60f, 60f)));
        }
        // Glassy rib tips breaking out of the ground in curved hooks.
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = Around(6f, 26f);
            float height = R(2.5f, 6f), lean = R(1f, 3f);
            var points = new List<Vector3>();
            for (int k = 0; k <= 12; k++)
            {
                float t = k / 12f;
                points.Add(new Vector3(0f, height * Mathf.Sin(t * Mathf.PI * 0.6f), lean * t * t) + Vector3.down * 0.8f);
            }
            GameObject tip = Mesh(parent, "Rib Tip", BoneShapes.Ribbon(points, R(0.6f, 0.9f), 0.25f, 0.3f, Vector3.right, (float)random.NextDouble()),
                ribGlass != null ? ribGlass : bone, solid: false);
            tip.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, R(0f, 360f), 0f));
            tip.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        // Designed flora growing among the bones.
        if (flora != null)
        {
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = Around(2f, 28f);
                var growth = new BoneFlora(seed * 31 + i);
                growth.Cluster(Vector3.zero, Vector3.up, R(0.6f, 1.2f));
                GameObject clump = growth.Attach(parent, flora, "Ground Flora");
                if (clump != null) clump.transform.position = p;
            }
        }
    }

    // ---------------- Pieces ----------------

    /// <summary>A ring of masonry chunks drifting below a tower's cut end, slowly bobbing and turning.</summary>
    private static void Drift(Transform parent, Vector3 cutCentre, float radius, float depthRoom, int count, Material stone, System.Random random)
    {
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.PI * 2f / count + R(-0.3f, 0.3f);
            float below = R(1.5f, Mathf.Clamp(depthRoom * 0.5f, 3f, 14f));
            Vector3 p = cutCentre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius * R(0.5f, 1.2f) + Vector3.down * below;
            Vector3 size = new Vector3(R(0.6f, 2f), R(0.4f, 1f), R(0.8f, 2.4f)) * Mathf.Lerp(1f, 0.5f, below / 14f);
            GameObject chunk = Block(parent, "Drifting Chunk", size, stone, random, solid: false);
            chunk.transform.SetPositionAndRotation(p, Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f)));
            var floater = chunk.AddComponent<Floater>();
            floater.bob = R(0.2f, 0.6f);
            floater.spin = R(-8f, 8f);
            floater.phase = R(0f, 10f);
        }
    }

    private static GameObject Block(Transform parent, string name, Vector3 size, Material material, System.Random random, bool solid)
    {
        Vector3 half = size * 0.5f;
        Mesh mesh = LandmarkShapes.Slab(new Vector3(0f, -half.y, -half.z), new Vector3(0f, -half.y, half.z), size.x, size.y, (float)random.NextDouble());
        return Mesh(parent, name, mesh, material, solid);
    }

    private static GameObject Mesh(Transform parent, string name, Mesh mesh, Material material, bool solid)
    {
        var go = new GameObject(name) { layer = solid ? 7 : 0 };
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        if (solid)
        {
            var collider = go.AddComponent<MeshCollider>();
            collider.convex = true;
            collider.sharedMesh = mesh;
        }
        return go;
    }

    private static bool Ground(Vector3 point, out Vector3 ground, out Vector3 normal)
    {
        if (Physics.Raycast(point + Vector3.up * 150f, Vector3.down, out RaycastHit hit, 300f, 1 << 7, QueryTriggerInteraction.Ignore))
        {
            ground = hit.point;
            normal = hit.normal;
            return true;
        }
        ground = point;
        normal = Vector3.up;
        return false;
    }
}

/// <summary>Slow drifting for decoration: bobs up and down and turns, around where it started.</summary>
public sealed class Floater : MonoBehaviour
{
    public float bob = 0.4f;
    public float spin = 5f;
    public float phase;
    private Vector3 home;

    private void Start() => home = transform.position;

    private void Update()
    {
        transform.position = home + Vector3.up * (Mathf.Sin(Time.time * 0.6f + phase) * bob);
        transform.Rotate(Vector3.up, spin * Time.deltaTime, Space.World);
    }
}
