using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Strange water in the dark caves under the crack (shader Capstone/Cave/StrangeWater, after Daniel's toon water):
///   hanging pools - flattened water lenses stuck to the cavern ceiling, gravity reversed (their surface is
///                   underneath, their light shafts rise into the rock); they drip down to the floor,
///   floating blobs - wobbling balls of water hanging in the dark air, each with its own "down": they shed drops
///                   sideways or upwards along it,
///   drops          - fall along their own gravity until they hit rock, then leave a widening glowing ring.
/// No colliders: it is scenery. DarkTunnels.Build places it once the caves are built.
/// </summary>
public sealed class StrangeWater : MonoBehaviour
{
    private enum Kind { Pool, Blob }

    private Kind kind;
    private Vector3 down, home;
    private float nextDrop, phase;
    private Material material;
    private static Shader shader, lineShader;
    private static Mesh sphere;

    private struct Drop { public Transform t; public Vector3 velocity, gravity; }
    private struct Ring { public LineRenderer line; public float age; public Vector3 normal; }
    private readonly List<Drop> drops = new List<Drop>();
    private readonly List<Ring> rings = new List<Ring>();
    private readonly Stack<Transform> dropPool = new Stack<Transform>();
    private Material dropMaterial, ringMaterial;

    /// <summary>Off (default): still mirror pools on the floor (MirrorPool). On: the older water lenses hanging from the ceiling.</summary>
    public static bool HangingLenses = false;
    /// <summary>The older floating balls of water (off: the water hangs as lakes under the rock, HangingLake).</summary>
    public static bool FloatingBlobs = false;

    // Still mirror pools with stalagmites (and stalactites in the grotto), each with a faint glow of its own.
    private static void MirrorPools(Transform parent, int mask)
    {
        Material rock = null;
        var world = FindFirstObjectByType<ProceduralWorld>();
        if (world != null)
            foreach (MeshRenderer r in world.GetComponentsInChildren<MeshRenderer>())
                if (r.gameObject.layer == world.TerrainLayer && r.sharedMaterial != null) { rock = r.sharedMaterial; break; }
        if (rock == null) return;
        // Grotto: a small pool under a ceiling full of stalactites.
        if (Physics.Raycast(DarkTunnelShape.Grotto, Vector3.down, out RaycastHit grotto, 8f, mask))
        {
            MirrorPool.Create(parent, grotto.point, 3.4f, new Color(0.35f, 0.75f, 1f), 3, mask, rock);
            for (int i = 0; i < 9; i++)
            {
                Vector3 from = DarkTunnelShape.Grotto + new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
                if (Physics.Raycast(from, Vector3.up, out RaycastHit ceiling, 8f, mask))
                    MirrorPool.Stalagmite(parent, ceiling.point + Vector3.up * 0.3f, Random.Range(0.8f, 2.4f), Random.Range(0.15f, 0.4f), rock, true);
            }
        }
        // Open cavern: a wide still lake between two pillars, and a smaller one by the stair tower's door.
        Vector3[] spots = { new Vector3(-124f, -60f, 164f), new Vector3(-151f, -60f, 140f) }; // the lake laps at a pillar's foot
        float[] sizes = { 6f, 4.5f };
        Color[] glows = { new Color(0.3f, 0.95f, 0.85f), new Color(0.7f, 0.45f, 1f) };
        for (int i = 0; i < spots.Length; i++)
            if (Physics.Raycast(spots[i], Vector3.down, out RaycastHit floor, 12f, mask))
                MirrorPool.Create(parent, floor.point, sizes[i], glows[i], 9, mask, rock);
    }

    /// <summary>Places the cave water (call once the caves' terrain is built).</summary>
    public static void Build(Transform parent)
    {
        int mask = DarkTunnels.TerrainMask;
        if (HangingLenses)
        {
            // The older look: water lenses stuck to the cavern ceiling, dripping down.
            Vector3[] poolSpots = { new Vector3(-122f, -45f, 128f), new Vector3(-96f, -45f, 192f), new Vector3(-150f, -45f, 192f), new Vector3(-80f, -45f, 132f) };
            foreach (Vector3 spot in poolSpots)
                if (Physics.Raycast(spot, Vector3.up, out RaycastHit hit, 30f, mask))
                    Pool(parent, hit.point, hit.normal);
        }
        else MirrorPools(parent, mask);
        // Blobs floating in the dark air, each with its own gravity.
        Vector3[] blobs = { new Vector3(-112f, -50f, 140f), new Vector3(-100f, -46f, 178f), new Vector3(-134f, -52f, 176f), new Vector3(-120f, -56f, 160f), new Vector3(-104f, -57f, 146f) };
        Vector3[] downs = { Vector3.up, new Vector3(1f, 0.2f, 0f), Vector3.down, new Vector3(0f, 0.4f, -1f), new Vector3(-0.6f, 1f, 0.3f) };
        if (FloatingBlobs)
            for (int i = 0; i < blobs.Length; i++) Blob(parent, blobs[i], downs[i].normalized, 1.2f + (i % 3) * 0.7f);
        // Lakes hanging under the rock, flowing to the points they drip from: under the shelves and on the high ceiling.
        Vector3[] lakes =
        {
            new Vector3(-126f, -60f, 131f), new Vector3(-116f, -58.5f, 175f), new Vector3(-88f, -53f, 140f),
            new Vector3(-150f, -56f, 160f), new Vector3(-90f, -55f, 161f), new Vector3(-136f, -40f, 192f), new Vector3(-72f, -40f, 128f),
        };
        float[] sizes = { 10f, 9f, 9f, 10f, 8f, 15f, 15f };
        for (int i = 0; i < lakes.Length; i++) HangingLake.Create(parent, lakes[i], sizes[i], mask);
    }

    private static StrangeWater Make(Transform parent, string name, Vector3 position, Vector3 scale, Quaternion rotation)
    {
        if (shader == null) shader = Shader.Find("Capstone/Cave/StrangeWater");
        if (lineShader == null) lineShader = Shader.Find("Capstone/Cave/GlowLine");
        if (sphere == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = sphere;
        var r = go.AddComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        var w = go.AddComponent<StrangeWater>();
        w.material = new Material(shader) { name = name };
        r.sharedMaterial = w.material;
        w.dropMaterial = new Material(shader) { name = name + " drop" };
        w.dropMaterial.SetFloat("_Shape", 1f);
        w.dropMaterial.SetFloat("_depthRange", 0.3f);
        w.ringMaterial = new Material(lineShader) { name = name + " ring" };
        w.home = position;
        w.phase = Random.value * 10f;
        return w;
    }

    private static void Pool(Transform parent, Vector3 ceiling, Vector3 normal)
    {
        // A flattened lens half sunk in the rock; gravity up, so its surface faces the floor.
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, -normal); // local up = into the rock
        float w = Random.Range(4f, 6.5f), d = Random.Range(3.5f, 5.5f);
        var pool = Make(parent, "Hanging pool", ceiling + normal * 0.2f, new Vector3(w, 2.2f, d), rot);
        pool.kind = Kind.Pool;
        pool.down = normal.y < 0f ? -normal : Vector3.up; // towards the rock
        pool.material.SetFloat("_Shape", 1f);
        pool.material.SetVector("_Down", new Vector4(0f, 1f, 0f, 0f));
        pool.material.SetFloat("_depthRange", 1.2f);
        pool.nextDrop = Time.time + Random.Range(0.5f, 2f);
    }

    private static void Blob(Transform parent, Vector3 at, Vector3 gravity, float size)
    {
        var blob = Make(parent, "Floating water", at, Vector3.one * size, Random.rotation);
        blob.kind = Kind.Blob;
        blob.down = gravity;
        blob.material.SetFloat("_Shape", 1f);
        blob.material.SetFloat("_Wobble", 0.06f);
        blob.material.SetFloat("_depthRange", size * 0.8f);
        // Its own gravity, carried into object space.
        blob.material.SetVector("_Down", blob.transform.InverseTransformDirection(gravity));
        blob.material.SetColor("_causticsColor", new Color(0.7f, 0.5f, 2.2f));
        blob.nextDrop = Time.time + Random.Range(1f, 4f);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (kind == Kind.Blob)
        {
            // Drift and turn slowly round where it hangs; its gravity turns with it.
            transform.position = home + new Vector3(Mathf.Sin(Time.time * 0.21f + phase), Mathf.Sin(Time.time * 0.33f + phase * 2f) * 0.6f,
                Mathf.Cos(Time.time * 0.17f + phase)) * 0.8f;
            transform.Rotate(new Vector3(0.13f, 0.31f, 0.07f), 6f * dt, Space.Self);
            material.SetVector("_Down", transform.InverseTransformDirection(down));
        }
        if (Time.time >= nextDrop)
        {
            nextDrop = Time.time + (kind == Kind.Pool ? Random.Range(1.2f, 3f) : Random.Range(3.5f, 7f));
            Shed();
        }
        StepDrops(dt);
        StepRings(dt);
    }

    private void Shed()
    {
        Transform t = dropPool.Count > 0 ? dropPool.Pop() : NewDrop();
        t.gameObject.SetActive(true);
        Vector3 g = kind == Kind.Pool ? Vector3.down : down;
        // Leaves from the side its gravity pulls to (a pool's underside, a blob's "bottom").
        Vector3 edge = kind == Kind.Pool ? transform.position + Vector3.down * transform.localScale.y * 0.45f
            : transform.position + down * transform.localScale.x * 0.5f;
        t.position = edge + Random.insideUnitSphere * 0.3f;
        t.localScale = new Vector3(0.12f, 0.12f, 0.12f);
        drops.Add(new Drop { t = t, velocity = g * 0.3f, gravity = g * (kind == Kind.Pool ? 9.8f : 4f) });
    }

    private Transform NewDrop()
    {
        var go = new GameObject("Drop");
        go.transform.SetParent(transform.parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = sphere;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = dropMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    private void StepDrops(float dt)
    {
        int mask = DarkTunnels.TerrainMask;
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            Drop d = drops[i];
            d.velocity += d.gravity * dt;
            Vector3 step = d.velocity * dt;
            float speed = d.velocity.magnitude;
            // Stretched along its fall.
            d.t.localScale = new Vector3(0.11f, 0.11f + Mathf.Min(0.35f, speed * 0.03f), 0.11f);
            if (speed > 0.01f) d.t.rotation = Quaternion.FromToRotation(Vector3.up, d.velocity / speed);
            bool gone = d.t.position.y < -90f || Vector3.Distance(d.t.position, transform.position) > 60f;
            if (!gone && Physics.Raycast(d.t.position, step.normalized, out RaycastHit hit, step.magnitude + 0.05f, mask))
            {
                AddRing(hit.point + hit.normal * 0.03f, hit.normal);
                gone = true;
            }
            if (gone)
            {
                d.t.gameObject.SetActive(false);
                dropPool.Push(d.t);
                drops.RemoveAt(i);
                continue;
            }
            d.t.position += step;
            drops[i] = d;
        }
    }

    private void AddRing(Vector3 at, Vector3 normal)
    {
        var go = new GameObject("Ripple");
        go.transform.SetParent(transform.parent, false);
        go.transform.position = at;
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = ringMaterial;
        line.loop = true;
        line.positionCount = 24;
        line.useWorldSpace = false;
        line.widthMultiplier = 0.025f;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
        rings.Add(new Ring { line = line, age = 0f, normal = normal });
    }

    private void StepRings(float dt)
    {
        Color c = material.GetColor("_causticsColor");
        for (int i = rings.Count - 1; i >= 0; i--)
        {
            Ring r = rings[i];
            r.age += dt;
            if (r.age > 1.6f) { Destroy(r.line.gameObject); rings.RemoveAt(i); continue; }
            float radius = 0.1f + r.age * 0.9f;
            for (int k = 0; k < 24; k++)
            {
                float a = k / 24f * Mathf.PI * 2f;
                r.line.SetPosition(k, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            Color fade = c * (1f - r.age / 1.6f) * 0.6f;
            r.line.startColor = r.line.endColor = fade;
            rings[i] = r;
        }
    }

    private void OnDestroy()
    {
        foreach (Drop d in drops) if (d.t != null) Destroy(d.t.gameObject);
        foreach (Transform t in dropPool) if (t != null) Destroy(t.gameObject);
        foreach (Ring r in rings) if (r.line != null) Destroy(r.line.gameObject);
    }
}
