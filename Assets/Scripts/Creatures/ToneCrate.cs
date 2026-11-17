using UnityEngine;

/// <summary>
/// Tone Mesa (class kept as ToneCrate): a huge flat-topped lump of living rock (a generated mesa mesh with strata
/// ledges, drawn with the terrain's own material, on the ground layer with a mesh collider: the player walks on it and
/// climbs it with both hands like any rock face). It lies across the way (over the holes down into the floor tunnels).
/// Glowing crystals in its sides show its note (count and colour: E violet 1, G gold 2, B teal 3, D rose 4).
/// It never turns. When the conductor baton plays its note, it shoots off in a straight line along the axis nearest to
/// the crosshair and slides on with inertia until it hits rock, then thumps; play again to move it on. Muted (another
/// note sounding) it does not move.
/// Chain: its thump carries far in the dark, the Lure Maw comes to look, Bell Snails shut, Glims scatter.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class ToneCrate : CaveGlowCreature
{
    [Tooltip("Width and depth (m).")]
    [SerializeField] private float size = 7f;
    [SerializeField] private float height = 4.5f;
    [Tooltip("Speed its note gives it (m/s).")]
    [SerializeField] private float kick = 5f;
    [Tooltip("How fast it slows down while sliding (m/s each second).")]
    [SerializeField] private float friction = 1.1f;
    [Tooltip("How far the crosshair point must be from its edge before its note moves it (m).")]
    [SerializeField] private float pickDistance = 1f;

    public override string MusicLayer => "";
    public override bool CanBeCarried => false;
    public override Vector3 AimPoint => transform.position + Vector3.up * height * 0.85f;
    protected override float Loudness => 1f + thump * 3f;
    /// <summary>Just hit something hard: the others hear a bang.</summary>
    public bool Thumping => thump > 0.2f;

    private Vector3 velocity, lastGround;
    private bool grounded, built;
    private float fall, cooldown, thump, wobble, flash;
    private Material bandGlow;
    private Transform body;
    private Mesh mesh;
    private MeshCollider ownCollider;
    private readonly RaycastHit[] castHits = new RaycastHit[16];

    /// <summary>Sets it down on a floor point with a note.</summary>
    public void Settle(Vector3 floorPoint, CaveNote tone)
    {
        transform.SetPositionAndRotation(floorPoint, Quaternion.identity);
        velocity = Vector3.zero;
        note = tone;
        Build();
    }

    protected override void InitializeCreature()
    {
        base.InitializeCreature();
        voiceReach = 9f;
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void Build()
    {
        if (built) return;
        built = true;
        glowColor = NoteColor(note);

        // The terrain's own look (same material as the cave walls), so it reads as a piece of the ground that moves.
        Material rock = null;
        int ground = 7;
        var world = FindFirstObjectByType<ProceduralWorld>();
        if (world != null)
        {
            ground = world.TerrainLayer;
            foreach (MeshRenderer r in world.GetComponentsInChildren<MeshRenderer>())
                if (r.gameObject.layer == ground && r.sharedMaterial != null) { rock = r.sharedMaterial; break; }
        }
        if (rock == null) rock = InkMaterial(new Color(0.05f, 0.05f, 0.06f));

        // One organic slab of rock: a flat-topped, steep-sided lump with strata ledges and a lumpy skin, on the ground
        // layer with a mesh collider, so the player walks on it and climbs it with both hands like any rock face.
        body = new GameObject("Body").transform;
        body.SetParent(transform, false);
        body.gameObject.layer = ground;
        mesh = MesaMesh(size, height, Random.Range(0f, 100f));
        body.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = body.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = rock;
        ownCollider = body.gameObject.AddComponent<MeshCollider>();
        ownCollider.sharedMesh = mesh;

        // Its note: 1 to 4 glowing crystals half sunk in each side, in the note's colour.
        bandGlow = GlowMaterial(glowColor * 2.2f, 0.85f, 0.8f);
        int count = (int)note + 1;
        for (int f = 0; f < 4; f++)
        {
            Quaternion turn = Quaternion.Euler(0f, f * 90f + 15f, 0f);
            for (int k = 0; k < count; k++)
            {
                float across = (k - (count - 1) * 0.5f) * 0.9f;
                Vector3 local = turn * new Vector3(across, height * 0.72f, 0f);
                Vector3 dir = turn * Vector3.forward;
                // Out along the side until the surface.
                if (ownCollider.Raycast(new Ray(transform.position + local + dir * size, -dir), out RaycastHit hit, size * 2f))
                {
                    var crystal = Part(PrimitiveType.Sphere, body, transform.InverseTransformPoint(hit.point), new Vector3(0.35f, 0.5f, 0.35f), bandGlow);
                    crystal.layer = 0;
                }
            }
        }
        AddHalo(body, new Vector3(0f, height * 0.85f, 0f), size * 0.55f, 0.6f);
        AddLamp(body, size * 0.9f, 1.2f);
        lastGround = transform.position;
    }

    /// <summary>A rounded mesa: a superellipse in plan, flat top, steep sides with strata ledges, noisy skin.</summary>
    private static Mesh MesaMesh(float width, float tall, float seed)
    {
        const int around = 40, rings = 14;
        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();
        float half = width * 0.5f;
        // Profile from the bottom (t = 0) to the rim (t = 1) and in over the top: radius scale and height.
        for (int j = 0; j <= rings; j++)
        {
            float t = j / (float)rings;
            float y, scale;
            if (t < 0.8f)
            {
                float u = t / 0.8f;
                y = u * tall;
                // Steep sides, wider at the foot, strata ledges every metre or so.
                scale = 1.08f - 0.16f * u + 0.05f * Mathf.Round(Mathf.Sin(u * 9f + seed) * 1.5f) / 1.5f;
            }
            else
            {
                float u = (t - 0.8f) / 0.2f;
                y = tall + Mathf.Sin(u * Mathf.PI * 0.5f) * 0.25f;
                scale = 0.92f * Mathf.Cos(u * Mathf.PI * 0.5f);
            }
            for (int i = 0; i < around; i++)
            {
                float a = i / (float)around * Mathf.PI * 2f;
                float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                // Superellipse: a rounded square in plan.
                float px = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 0.45f), pz = Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.45f);
                float lump = 1f + (Mathf.PerlinNoise(a * 1.7f + seed, y * 0.45f + seed) - 0.5f) * 0.22f;
                vertices.Add(new Vector3(px * half * scale * lump, y + (Mathf.PerlinNoise(px * 2f + seed, pz * 2f) - 0.5f) * 0.3f * (t > 0.8f ? 1f : 0f), pz * half * scale * lump));
            }
        }
        int top = vertices.Count;
        vertices.Add(new Vector3(0f, tall + 0.3f, 0f));
        int bottom = vertices.Count;
        vertices.Add(new Vector3(0f, 0f, 0f));
        for (int j = 0; j < rings; j++)
            for (int i = 0; i < around; i++)
            {
                int a0 = j * around + i, a1 = j * around + (i + 1) % around;
                int b0 = a0 + around, b1 = a1 + around;
                triangles.Add(a0); triangles.Add(b0); triangles.Add(a1);
                triangles.Add(a1); triangles.Add(b0); triangles.Add(b1);
            }
        int last = rings * around;
        for (int i = 0; i < around; i++)
        {
            triangles.Add(last + i); triangles.Add(top); triangles.Add(last + (i + 1) % around);
            triangles.Add(i); triangles.Add((i + 1) % around); triangles.Add(bottom);
        }
        var m = new Mesh { name = "Tone Mesa" };
        m.SetVertices(vertices);
        m.SetTriangles(triangles, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    protected override void TickCreature(float deltaTime)
    {
        if (!built) Build();
        cooldown -= deltaTime;
        thump = Mathf.MoveTowards(thump, 0f, deltaTime * 0.5f);
        flash = Mathf.MoveTowards(flash, 0f, deltaTime * 1.5f);

        // Its note: off along the axis nearest to the crosshair point, once it has come to rest.
        if (Guide.HasValue && !Muted && cooldown <= 0f && velocity.sqrMagnitude < 0.09f)
        {
            Vector3 off = Guide.Value - transform.position;
            off.y = 0f;
            if (off.magnitude > size * 0.5f + pickDistance)
            {
                Vector3 dir = Mathf.Abs(off.x) > Mathf.Abs(off.z) ? new Vector3(Mathf.Sign(off.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(off.z));
                velocity = dir * kick;
                cooldown = 0.6f;
                flash = 1f;
                AudioManager.PostEventOnGrid("Play_Crate_" + note, gameObject, 2);
            }
        }

        Vector3 p = transform.position;
        float speed = velocity.magnitude;
        if (speed > 0.01f)
        {
            Vector3 dir = velocity / speed;
            float step = speed * deltaTime;
            // Cast a box lifted off the floor: a cast that starts touching the terrain ignores the whole chunk.
            Vector3 centre = p + Vector3.up * (height * 0.5f + 0.4f);
            Vector3 half = new Vector3(size * 0.48f, height * 0.4f, size * 0.48f);
            if (CastAhead(centre, half, dir, step + 0.05f, out RaycastHit hit) && Vector3.Dot(hit.normal, dir) < -0.3f)
            {
                p += dir * Mathf.Max(0f, hit.distance - 0.05f);
                Thump(speed);
                velocity = Vector3.zero;
            }
            else
            {
                p += dir * step;
                velocity = dir * Mathf.Max(0f, speed - friction * deltaTime);
            }
        }

        // Rest on the highest floor under its middle and corners (so it bridges a hole rather than dropping in).
        if (FloorUnder(p, 1.2f + fall * deltaTime, out float floorY))
        {
            if (fall > 4f) Thump(fall * 0.5f);
            p.y = Mathf.Lerp(p.y, floorY - 0.3f, 1f - Mathf.Exp(-10f * deltaTime));
            fall = 0f;
            lastGround = p;
            grounded = true;
        }
        else
        {
            fall += 12f * deltaTime;
            p.y -= fall * deltaTime;
            // Never lost through the world: a long fall puts it back where it last stood.
            if (grounded && lastGround.y - p.y > 10f) { p = lastGround; fall = 0f; velocity = Vector3.zero; }
        }
        MoveCreature(p - transform.position);

        UpdateGlow(Muted ? 0.2f : 0.6f + thump * 0.5f, deltaTime);
        wobble = Mathf.MoveTowards(wobble, 0f, deltaTime * 2f);
        if (body != null) body.localPosition = new Vector3(0f, Mathf.Sin(Time.time * 25f) * wobble * 0.03f, 0f);
        if (bandGlow != null) bandGlow.SetFloat("_Glow", shownGlow * (0.7f + flash * 2f));
    }

    // The nearest rock ahead that is not the mesa itself.
    private bool CastAhead(Vector3 centre, Vector3 half, Vector3 dir, float distance, out RaycastHit best)
    {
        best = default;
        float nearest = float.MaxValue;
        int n = Physics.BoxCastNonAlloc(centre, half, dir, castHits, Quaternion.identity, distance, TerrainMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (castHits[i].collider == ownCollider || castHits[i].distance <= 0f) continue;
            if (castHits[i].distance < nearest) { nearest = castHits[i].distance; best = castHits[i]; }
        }
        return nearest < float.MaxValue;
    }

    private bool FloorUnder(Vector3 p, float below, out float y)
    {
        y = float.MinValue;
        bool found = false;
        float r = size * 0.38f;
        for (int i = 0; i < 5; i++)
        {
            Vector3 at = p + (i == 0 ? Vector3.zero : new Vector3(i % 2 == 0 ? r : -r, 0f, i < 3 ? r : -r));
            int n = Physics.RaycastNonAlloc(at + Vector3.up * 1.5f, Vector3.down, castHits, 1.5f + below, TerrainMask, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < n; h++)
            {
                if (castHits[h].collider == ownCollider) continue;
                y = Mathf.Max(y, castHits[h].point.y);
                found = true;
            }
        }
        return found;
    }

    private void Thump(float speed)
    {
        thump = Mathf.Clamp01(speed / kick);
        wobble = thump;
        AudioManager.PostEventOnGrid("Play_Crate_Thump", gameObject, 4);
    }
}
