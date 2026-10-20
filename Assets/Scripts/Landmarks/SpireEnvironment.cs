using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Turns the Hanging Spire from a tower into a place, after the hatched crystal drawings Daniel likes:
///   - Crystal blades (long faceted shards, hatched like pencil work) burst from the crown, fan down from the
///     cut ends of the floating towers to a point, and jut from their walls at sharp angles.
///   - A field of loose shards drifts slowly round the spire, turning as it goes.
///   - Long ink guide lines stand in the air round it, vertical and slanted, like construction lines.
///   - Rings and spokes are etched into the ground round the base, with signs along them.
///   - Pale motes rise slowly through the whole area, as if the spire draws the air up.
/// None of it has colliders: it is the setting, not the climb.
/// </summary>
public static class SpireEnvironment
{
    public static void Build(Transform root, Vector3 basePoint, SpireShape spire, IReadOnlyList<Transform> stageTowers,
                             Material spireMaterial, Material glyphMaterial, int seed)
    {
        var random = new System.Random(seed * 4243 + 17);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var parent = new GameObject("Environment").transform;
        parent.SetParent(root, false);

        Material sketch = SketchMaterial(spireMaterial);
        Material ink = Unlit(new Color(0.05f, 0.05f, 0.06f));

        // ---- Crystal blades on the spire ----
        Vector3 crown = basePoint + spire.Axis(spire.height) + Vector3.up * (spire.crown * 0.5f);
        var blades = new Blades();
        for (int i = 0; i < 11; i++)
        {
            // Bursting up and out of the crown.
            Vector3 dir = (Vector3.up * R(0.6f, 1.6f) + Flat(R(0f, 360f)) * R(0.2f, 1f)).normalized;
            blades.Blade(crown + Flat(R(0f, 360f)) * R(0f, spire.innerRadius), dir, R(7f, 20f), R(1f, 2.6f), random);
        }
        if (spire.cutBelow > 0f)
        {
            // Fanning down from the cut end to a point, like the base of the drawing.
            Vector3 cut = basePoint + spire.Axis(spire.cutBelow);
            float radius = spire.Outer(0f, spire.cutBelow);
            Vector3 tip = cut + Vector3.down * R(14f, 22f);
            for (int i = 0; i < 9; i++)
            {
                Vector3 start = cut + Flat(i * 40f + R(-12f, 12f)) * radius * R(0.4f, 0.95f);
                Vector3 toward = (tip + Flat(R(0f, 360f)) * R(0f, 2f) - start);
                blades.Blade(start, toward.normalized, toward.magnitude * R(0.7f, 1.05f), R(1f, 2.4f), random);
            }
        }
        // Jutting from the wall at sharp angles, most of the way up.
        float low = Mathf.Max(6f, spire.cutBelow + 2f);
        for (int i = 0; i < 18; i++)
        {
            float y = R(low, spire.height - 4f);
            float angle = R(0f, 360f);
            Vector3 outward = Flat(angle);
            Vector3 axis = basePoint + spire.Axis(y);
            Vector3 start = axis + outward * (spire.Outer(angle * Mathf.Deg2Rad, y) - 0.6f);
            Vector3 dir = (outward + Vector3.up * R(-0.6f, 1.2f) + Flat(angle + 90f) * R(-0.5f, 0.5f)).normalized;
            blades.Blade(start, dir, R(4f, 11f), R(0.6f, 1.6f), random);
        }
        // The stage towers get a few too: a crown burst each, and a spike under the floating one.
        if (stageTowers != null)
        {
            for (int t = 0; t < stageTowers.Count; t++)
            {
                Bounds b = stageTowers[t].GetComponent<Collider>() != null ? stageTowers[t].GetComponent<Collider>().bounds : new Bounds(stageTowers[t].position, Vector3.one * 4f);
                Vector3 top = new Vector3(b.center.x, b.max.y, b.center.z);
                for (int i = 0; i < 5; i++)
                    blades.Blade(top + Flat(R(0f, 360f)) * R(0f, 2f), (Vector3.up + Flat(R(0f, 360f)) * R(0.2f, 0.9f)).normalized, R(4f, 10f), R(0.7f, 1.6f), random);
                if (t == 1)
                {
                    Vector3 bottom = new Vector3(b.center.x, b.min.y, b.center.z);
                    Vector3 point = bottom + Vector3.down * R(9f, 14f);
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 start = bottom + Flat(i * 60f) * R(1.5f, 3.5f);
                        Vector3 toward = point - start;
                        blades.Blade(start, toward.normalized, toward.magnitude, R(0.8f, 1.8f), random);
                    }
                }
            }
        }
        blades.Attach(parent, "Crystal Blades", sketch, shadows: true);

        // ---- A field of loose shards drifting round the spire ----
        var field = new GameObject("Shard Field").transform;
        field.SetParent(parent, false);
        field.position = new Vector3(basePoint.x, basePoint.y, basePoint.z);
        // Three ordered rings of blades at set heights, evenly spaced and all lying along their ring, each ring
        // turning slowly the other way to the one below: an orrery round the spire rather than loose debris.
        float lowRing = Mathf.Max(8f, spire.cutBelow + 4f);
        float[] heights = { lowRing, Mathf.Lerp(lowRing, spire.height, 0.5f), spire.height + 8f };
        float[] radii = { spire.Outer(0f, 0f) + 12f, spire.Outer(0f, 0f) + 22f, spire.Outer(0f, 0f) + 16f };
        int[] counts = { 14, 20, 16 };
        for (int ring = 0; ring < 3; ring++)
        {
            float length = new[] { 3.5f, 5f, 4f }[ring];
            float width = length * 0.28f;
            for (int i = 0; i < counts[ring]; i++)
            {
                var shard = new Blades();
                shard.Blade(Vector3.back * (length * 0.5f), Vector3.forward, length, width, random);
                GameObject go = shard.Attach(field, "Shard", sketch, shadows: false);
                var drift = go.AddComponent<Orbiter>();
                drift.centre = basePoint;
                drift.radius = radii[ring];
                drift.height = heights[ring];
                drift.angle = i * 360f / counts[ring];
                drift.speed = (ring % 2 == 0 ? 1f : -1f) * 3f;
                drift.align = true;
                drift.tilt = ring == 1 ? -18f : 18f;
                drift.bob = 0.4f;
            }
        }

        // ---- Ink guide lines: upright, evenly spaced on two circles, all the same weight ----
        // Inner circle tall, outer circle shorter, heights stepping gently round, so they read as one framing.
        float[] circleRadii = { spire.Outer(0f, 0f) + 30f, spire.Outer(0f, 0f) + 60f };
        int[] circleCounts = { 12, 18 };
        for (int c = 0; c < 2; c++)
        {
            float offset = c * 10f;
            for (int i = 0; i < circleCounts[c]; i++)
            {
                float angle = offset + i * 360f / circleCounts[c];
                Vector3 foot = basePoint + Flat(angle) * circleRadii[c];
                float ground = GroundY(foot, basePoint.y);
                float top = basePoint.y + spire.height * (c == 0 ? 1.15f : 0.8f) + 8f * Mathf.Sin(i * Mathf.PI * 2f / circleCounts[c]);
                Line(parent, new Vector3(foot.x, ground - 2f, foot.z), new Vector3(foot.x, top, foot.z), 0.07f, ink);
            }
        }

        // ---- Rings and spokes etched round the base ----
        if (glyphMaterial != null)
        {
            var etch = new Etching();
            foreach (float radius in new[] { spire.Outer(0f, 0f) + 8f, spire.Outer(0f, 0f) + 22f, spire.Outer(0f, 0f) + 40f })
                etch.Ring(basePoint, radius, 0.18f);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f + R(-5f, 5f);
                etch.Spoke(basePoint, Flat(angle), spire.Outer(0f, 0f) + 8f, spire.Outer(0f, 0f) + 40f + R(0f, 25f), 0.14f);
            }
            etch.Attach(parent, glyphMaterial);
        }

        // ---- Pale motes rising slowly through the area ----
        Motes(parent, basePoint + Vector3.up * Mathf.Max(0f, spire.cutBelow * 0.5f), spire.Outer(0f, 0f) + 50f, spire.height + 20f);
    }

    // ---------------- Pieces ----------------

    private static Vector3 Flat(float degrees) => new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad), 0f, Mathf.Sin(degrees * Mathf.Deg2Rad));

    private static Vector3 Random3(System.Random random)
    {
        Vector3 v = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f);
        return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.up;
    }

    private static float GroundY(Vector3 point, float fallback)
    {
        return Physics.Raycast(point + Vector3.up * 200f, Vector3.down, out RaycastHit hit, 400f, 1 << 7, QueryTriggerInteraction.Ignore) ? hit.point.y : fallback;
    }

    /// <summary>The spire's stone, redrawn as hatched pencil work: pale, ink-edged, heavily cross-hatched.</summary>
    private static Material SketchMaterial(Material spire)
    {
        if (spire == null) return null;
        var m = new Material(spire) { name = "Spire Sketch (runtime)" };
        if (m.HasProperty("_Color"))
        {
            m.SetColor("_Color", new Color(0.86f, 0.85f, 0.82f));
            m.SetColor("_ShadowColor", new Color(0.32f, 0.32f, 0.33f));
            m.SetColor("_LineColor", new Color(0.05f, 0.05f, 0.06f));
            m.SetColor("_HatchColor", new Color(0.12f, 0.12f, 0.13f));
            m.SetFloat("_HatchStrength", 1f);
            m.SetFloat("_HatchScale", 16f);
            m.SetFloat("_ScriptChance", 0f);
            m.SetFloat("_ScriptChanceInside", 0f);
            m.SetFloat("_ScriptGlowBase", 0f);
            // Fine ink lines across every facet and dense cross-hatching: drawn, not plain.
            m.SetFloat("_CourseHeight", 0.45f);
            m.SetVector("_BlockWidth", new Vector4(0.4f, 1.2f, 0f, 0f));
            m.SetFloat("_ArcRadius", 2f);
            m.SetFloat("_LineWidth", 1.2f);
            m.SetFloat("_GlitchAmount", 0f);
            m.SetFloat("_RimStrength", 0.8f);
        }
        return m;
    }

    private static Material Unlit(Color colour)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return null;
        var m = new Material(shader);
        m.SetColor("_BaseColor", colour);
        return m;
    }

    /// <summary>A thin square rod from a to b.</summary>
    private static void Line(Transform parent, Vector3 a, Vector3 b, float width, Material material)
    {
        if (material == null) return;
        var go = new GameObject("Guide Line");
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, (b - a).normalized));
        go.transform.localScale = new Vector3(width, (b - a).magnitude, width);
        go.AddComponent<MeshFilter>().sharedMesh = Cube();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static Mesh cube;
    private static Mesh Cube()
    {
        if (cube != null) return cube;
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube = temp.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(temp);
        return cube;
    }

    private static void Motes(Transform parent, Vector3 centre, float radius, float height)
    {
        var go = new GameObject("Rising Motes");
        go.transform.SetParent(parent, false);
        go.transform.position = centre;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 18f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.2f);
        main.startColor = new Color(0.95f, 0.95f, 0.9f, 0.8f);
        main.maxParticles = 900;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 55f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(radius * 2f, height, radius * 2f);
        shape.position = Vector3.up * (height * 0.5f);
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.15f), new GradientAlphaKey(0.85f, 0.8f), new GradientAlphaKey(0f, 1f) });
        colour.color = gradient;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader != null) renderer.sharedMaterial = new Material(shader);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play();
    }

    /// <summary>Long faceted crystal blades: flattened bipyramids, sharp at both ends, built into one mesh.</summary>
    private sealed class Blades
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();

        public void Blade(Vector3 start, Vector3 dir, float length, float width, System.Random random)
        {
            dir = dir.normalized;
            Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            side = Quaternion.AngleAxis((float)random.NextDouble() * 360f, dir) * side;
            Vector3 flat = Vector3.Cross(dir, side);
            // The widest point sits a third of the way along; the cross-section is a flat, slightly uneven hexagon.
            float shoulderAt = 0.2f + 0.25f * (float)random.NextDouble();
            Vector3 shoulder = start + dir * (length * shoulderAt);
            Vector3 end = start + dir * length;
            const int sides = 6;
            var ring = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float angle = k * Mathf.PI * 2f / sides;
                float jag = 0.8f + 0.4f * (float)random.NextDouble();
                ring[k] = shoulder + (side * Mathf.Cos(angle) * width * 0.5f + flat * Mathf.Sin(angle) * width * 0.18f) * jag;
            }
            float part = (float)random.NextDouble();
            for (int k = 0; k < sides; k++)
            {
                int k1 = (k + 1) % sides;
                // Separate vertices per face: crisp facets.
                Face(start, ring[k1], ring[k], part);
                Face(end, ring[k], ring[k1], part);
            }
        }

        private void Face(Vector3 a, Vector3 b, Vector3 c, float part)
        {
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            uvs.Add(new Vector2(0f, part)); uvs.Add(new Vector2(0f, part)); uvs.Add(new Vector2(0f, part));
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
        }

        public GameObject Attach(Transform parent, string name, Material material, bool shadows)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }
    }

    /// <summary>Thin flat strips laid on the ground (rings and spokes), following the terrain.</summary>
    private sealed class Etching
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        public void Ring(Vector3 centre, float radius, float width)
        {
            var points = new List<Vector3>();
            int steps = Mathf.CeilToInt(radius * 2f);
            for (int i = 0; i <= steps; i++) points.Add(centre + Flat(i * 360f / steps) * radius);
            Strip(points, width);
        }

        public void Spoke(Vector3 centre, Vector3 dir, float from, float to, float width)
        {
            var points = new List<Vector3>();
            for (float d = from; d <= to; d += 1f) points.Add(centre + dir * d);
            Strip(points, width);
        }

        private void Strip(List<Vector3> points, float width)
        {
            int start = vertices.Count;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                p.y = GroundY(p, p.y) + 0.06f;
                Vector3 along = points[Mathf.Min(points.Count - 1, i + 1)] - points[Mathf.Max(0, i - 1)];
                along.y = 0f;
                Vector3 side = Vector3.Cross(Vector3.up, along.normalized) * (width * 0.5f);
                vertices.Add(p - side);
                vertices.Add(p + side);
            }
            for (int i = 0; i < points.Count - 1; i++)
            {
                int a = start + i * 2;
                triangles.Add(a); triangles.Add(a + 2); triangles.Add(a + 1);
                triangles.Add(a + 1); triangles.Add(a + 2); triangles.Add(a + 3);
            }
        }

        public void Attach(Transform parent, Material material)
        {
            var mesh = new Mesh { name = "Ground Etching", hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Ground Etching");
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}

/// <summary>Drifts slowly round a centre at a height, bobbing and turning (for the spire's shard field).</summary>
public sealed class Orbiter : MonoBehaviour
{
    public Vector3 centre;
    public float radius = 20f;
    public float height = 10f;
    public float angle;
    public float speed = 3f;
    public Vector3 spin;
    public float bob = 0.5f;
    [Tooltip("Lie along the orbit (pointing the way it travels), tilted, instead of tumbling.")]
    public bool align;
    public float tilt;

    private void Update()
    {
        angle += speed * Time.deltaTime;
        float a = angle * Mathf.Deg2Rad;
        transform.position = centre + new Vector3(Mathf.Cos(a) * radius, height + Mathf.Sin(Time.time * 0.4f + angle) * bob, Mathf.Sin(a) * radius);
        if (align)
        {
            Vector3 along = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Mathf.Sign(speed);
            transform.rotation = Quaternion.LookRotation(along, Vector3.up) * Quaternion.Euler(tilt, 0f, 0f);
        }
        else transform.Rotate(spin * Time.deltaTime, Space.Self);
    }
}
