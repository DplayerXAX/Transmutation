using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A charred bone nest on real ground near the Bone Kiln that releases fire bubbles.
/// The colossus's children go after these bubbles, which shows the player that fire and the bone children belong
/// together. Optional timed cleanup follows each bubble's lifetime setting; claimed bubbles belong to the
/// player or creature instead of the nest. Built by BoneKiln.
/// </summary>
public sealed class KilnBubbleNest : MonoBehaviour
{
    [Tooltip("Fire bubble to copy. Empty = the one fire flowers in the scene release, or a copy of a bubble already in the scene.")]
    public FireBubble bubblePrefab;
    [Min(2f)] public float interval = 9f;
    [Range(1, 6)] public int maxAlive = 3;
    [Tooltip("Seconds before an unclaimed bubble pops, only when its Limit Lifetime setting is enabled.")]
    [Min(2f)] public float popAfter = 24f;
    [Tooltip("Bubbles drift over towards this point (the mother's skull end) and hang about it...")]
    public Vector3 drawTo;
    [Tooltip("...but keep away from here (her heart: a bubble there would wake her by itself).")]
    public Vector3 keepFrom;
    [Min(0f)] public float drawStrength = 5f;

    private readonly List<(FireBubble bubble, float born)> alive = new List<(FireBubble, float)>();
    private float timer;
    private ProceduralWorld world;
    private Vector3 requestedPosition;
    private bool placed;
    private float nextSettle;
    private MeshRenderer shellRenderer;
    private MeshCollider shellCollider;
    private Transform ember;
    private Material shellMaterial, emberMaterial;
    private Mesh cupMesh;
    private int placementCandidate;
    public bool Placed => placed;

    public static KilnBubbleNest Build(Transform parent, Vector3 position, Material bone, int seed, Vector3 drawTo, Vector3 keepFrom)
    {
        var go = new GameObject("Kiln Bubble Nest");
        go.transform.SetParent(parent, true);
        go.transform.position = position;
        var nest = go.AddComponent<KilnBubbleNest>();
        nest.requestedPosition = position;
        nest.world = FindFirstObjectByType<ProceduralWorld>();
        // An organic ash-bone nest, using an independent, subdued version of the world's grain.
        var cup = new GameObject("Cup") { layer = 7 };
        cup.transform.SetParent(go.transform, false);
        Mesh mesh = nest.cupMesh = BowlMesh(seed);
        cup.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = cup.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Hidden/Internal-Colored");
        Shader shellShader = Shader.Find("Capstone/Landmarks/BoneMarble") ?? shader;
        nest.shellMaterial = new Material(shellShader) { name = "Kiln nest charred bone" };
        if (nest.shellMaterial.HasProperty("_ColorA"))
        {
            nest.shellMaterial.SetColor("_ColorA", new Color(0.48f, 0.5f, 0.46f));
            nest.shellMaterial.SetColor("_ColorB", new Color(0.045f, 0.055f, 0.055f));
            nest.shellMaterial.SetColor("_ColorMid", new Color(0.18f, 0.2f, 0.19f));
            nest.shellMaterial.SetFloat("_Scale", 2.2f);
            nest.shellMaterial.SetFloat("_Warp", 1.5f);
            nest.shellMaterial.SetFloat("_Bands", 3f);
            nest.shellMaterial.SetFloat("_Sharpness", 6f);
            nest.shellMaterial.SetFloat("_Flow", 0f);
            nest.shellMaterial.SetFloat("_FringeStrength", 0f);
            nest.shellMaterial.SetFloat("_FleshShare", 0f);
            nest.shellMaterial.SetColor("_GlowColor", new Color(0.7f, 0.25f, 0.06f));
            nest.shellMaterial.SetFloat("_GlowStrength", 0.18f);
        }
        else
        {
            nest.shellMaterial.SetColor("_BaseColor", new Color(0.25f, 0.28f, 0.25f));
            nest.shellMaterial.SetColor("_Color", new Color(0.25f, 0.28f, 0.25f));
            nest.shellMaterial.SetFloat("_Smoothness", 0.05f);
        }
        nest.emberMaterial = new Material(shader) { name = "Kiln nest embers" };
        nest.emberMaterial.SetColor("_BaseColor", new Color(0.65f, 0.25f, 0.06f));
        nest.emberMaterial.SetColor("_Color", new Color(0.65f, 0.25f, 0.06f));
        nest.emberMaterial.EnableKeyword("_EMISSION");
        nest.emberMaterial.SetColor("_EmissionColor", new Color(0.65f, 0.2f, 0.04f));
        r.sharedMaterials = new[] { nest.shellMaterial, nest.emberMaterial };
        r.shadowCastingMode = ShadowCastingMode.On;
        nest.shellRenderer = r; r.enabled = false;
        nest.shellCollider = cup.AddComponent<MeshCollider>();
        nest.shellCollider.sharedMesh = mesh; nest.shellCollider.enabled = false;
        var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = "Ember core"; glow.transform.SetParent(cup.transform, false);
        glow.transform.localPosition = Vector3.up * 0.34f; glow.transform.localScale = Vector3.one * 0.34f;
        LandmarkShapes.Discard(glow.GetComponent<Collider>());
        glow.GetComponent<Renderer>().sharedMaterial = nest.emberMaterial;
        glow.SetActive(false); nest.ember = glow.transform;
        nest.timer = 2f + seed % 5;
        nest.drawTo = drawTo;
        nest.keepFrom = keepFrom;
        nest.TryPlace();
        return nest;
    }

    private void Update()
    {
        if (!placed)
        {
            if (Time.time >= nextSettle) { nextSettle = Time.time + 1f; TryPlace(); }
            if (!placed) return;
        }
        if (ember != null) ember.localScale = Vector3.one * (0.34f + Mathf.Sin(Time.time * 2f) * 0.015f);
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            FireBubble b = alive[i].bubble;
            if (b == null) { alive.RemoveAt(i); continue; }
            bool held = b.IsCarried || b.IsLeashed || b.IsBoundToBone || BoneChild.IsCradled(b);
            // Held ones are the player's (or a child's) now: no longer the nest's to pop.
            if (held) { alive.RemoveAt(i); continue; }
            if (b.LifetimeLimited && Time.time - alive[i].born > popAfter) { Destroy(b.gameObject); alive.RemoveAt(i); }
        }
        timer -= Time.deltaTime;
        if (timer > 0f || alive.Count >= maxAlive) return;
        timer = interval;
        FireBubble prefab = ResolvePrefab();
        if (prefab == null) return;
        FireBubble bubble = Instantiate(prefab, transform.position + Vector3.up * 1.35f, Quaternion.identity);
        bubble.name = "Fire Bubble (kiln nest)";
        bubble.gameObject.SetActive(true);
        alive.Add((bubble, Time.time));
    }

    private void FixedUpdate()
    {
        // Free bubbles from the nest drift over to the mother and wander about above her, away from her heart.
        foreach (var (bubble, born) in alive)
        {
            if (bubble == null || bubble.IsCarried || bubble.IsBoundToBone || BoneChild.IsCradled(bubble)) continue;
            var rb = bubble.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic) continue;
            float wander = Time.time * 0.3f + born;
            Vector3 goal = drawTo + new Vector3(Mathf.Sin(wander) * 6f, 3.5f + Mathf.Sin(wander * 1.7f), Mathf.Cos(wander * 0.8f) * 6f);
            Vector3 pull = goal - rb.position;
            Vector3 push = rb.position - keepFrom;
            push.y = 0f;
            Vector3 force = Vector3.ClampMagnitude(pull * 0.6f - rb.linearVelocity * 0.4f, drawStrength);
            if (push.magnitude < 9f) force += push.normalized * drawStrength * 1.5f;
            // Hold it near the goal height instead of rising out of reach.
            force.y = Mathf.Clamp((goal.y - rb.position.y) * 0.6f - rb.linearVelocity.y * 0.5f, -2f, 2f);
            rb.AddForce(force, ForceMode.Acceleration);
        }
    }

    private FireBubble ResolvePrefab()
    {
        if (bubblePrefab != null) return bubblePrefab;
        // The bubble prefab a fire flower releases.
        var field = typeof(FireFlower).GetField("bubblePrefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (FireFlower flower in FindObjectsByType<FireFlower>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (field?.GetValue(flower) is FireBubble fromFlower && fromFlower != null) return bubblePrefab = fromFlower;
        // Otherwise a copy of one already in the scene.
        return FindFirstObjectByType<FireBubble>();
    }

    private void TryPlace()
    {
        if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
        if (world == null || !world.LayoutReady) return;
        // Unloaded or obstructed terrain can persist. Continue the search across updates instead
        // of repeating hundreds of terrain queries for every source in one frame each second.
        for (int probe = 0; probe < 6; probe++)
        {
            int attempt = placementCandidate;
            placementCandidate = (placementCandidate + 1) % 49;
            float angle = attempt * 2.399963f;
            float radius = attempt == 0 ? 0f : 2f + (attempt - 1) / 8 * 2f;
            Vector3 at = requestedPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            bool safe = true;
            for (int sample = 0; sample < 5; sample++)
            {
                Vector3 point = at + (sample == 0 ? Vector3.zero : Quaternion.Euler(0f, sample * 90f, 0f) * Vector3.forward * 1.1f);
                world.ColumnHeights(point, out float surface, out _, out _);
                float nearest = float.PositiveInfinity;
                RaycastHit ground = default;
                foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(point.x, surface + 30f, point.z),
                             Vector3.down, 45f, 1 << world.TerrainLayer, QueryTriggerInteraction.Ignore))
                    if (world.OwnsCollider(hit.collider) && hit.normal.y >= 0.8f && hit.distance < nearest)
                    { nearest = hit.distance; ground = hit; }
                if (ground.collider == null) { safe = false; break; }
                low = Mathf.Min(low, ground.point.y); high = Mathf.Max(high, ground.point.y);
            }
            if (!safe || high - low > 0.3f) continue;
            at.y = high + 0.025f;
            foreach (Collider obstacle in Physics.OverlapCapsule(at + Vector3.up, at + Vector3.up * 1.8f,
                         0.95f, ~0, QueryTriggerInteraction.Ignore))
                if (!obstacle.transform.IsChildOf(transform)) { safe = false; break; }
            if (!safe) continue;
            transform.position = at;
            shellRenderer.enabled = shellCollider.enabled = true;
            ember.gameObject.SetActive(true);
            placed = true;
            return;
        }
    }

    private static Mesh BowlMesh(int seed)
    {
        const int sides = 24;
        Vector2[] profile = { new Vector2(0.04f, 0f), new Vector2(0.48f, 0f), new Vector2(0.76f, 0.18f),
            new Vector2(0.88f, 0.48f), new Vector2(0.82f, 0.68f), new Vector2(0.67f, 0.62f),
            new Vector2(0.55f, 0.3f), new Vector2(0.04f, 0.2f) };
        var vertices = new List<Vector3>();
        var outside = new List<int>(); var inside = new List<int>();
        for (int ring = 0; ring < profile.Length; ring++)
        for (int side = 0; side < sides; side++)
        {
            float angle = side * Mathf.PI * 2f / sides;
            float uneven = 1f + 0.045f * Mathf.Sin(side * 2.17f + seed) + 0.025f * Mathf.Cos(side * 1.31f + seed * 0.7f);
            float lip = ring >= 3 && ring <= 5 ? Mathf.Sin(angle * 7f + seed) * 0.07f : 0f;
            vertices.Add(new Vector3(Mathf.Cos(angle) * profile[ring].x * uneven,
                profile[ring].y + lip, Mathf.Sin(angle) * profile[ring].x * uneven));
            if (ring == 0) continue;
            int a = (ring - 1) * sides + side, b = (ring - 1) * sides + (side + 1) % sides;
            int c = ring * sides + side, d = ring * sides + (side + 1) % sides;
            (ring >= 5 ? inside : outside).AddRange(new[] { a, c, b, b, c, d });
        }
        var mesh = new Mesh { name = "Charred bone fire nest", subMeshCount = 2 };
        mesh.SetVertices(vertices); mesh.SetTriangles(outside, 0); mesh.SetTriangles(inside, 1);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy()
    {
        if (shellMaterial != null) Destroy(shellMaterial);
        if (emberMaterial != null) Destroy(emberMaterial);
        if (cupMesh != null) Destroy(cupMesh);
    }
}
