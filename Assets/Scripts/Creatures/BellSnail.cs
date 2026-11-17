using UnityEngine;

/// <summary>
/// Bell Snail: a slow snail with a coiled amber shell that glows like a lantern, creeping over the tunnel walls and
/// ceilings. The snails are the tunnels' fixed lights: they mark the way, and Glims gather round their shells.
/// Chain: it hears the Lure Maw's voice and shuts itself into its shell (the light goes out) until the Maw is gone.
/// Baton: played alone it crawls along the rock towards the guide point (walls and ceilings too), slowly.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class BellSnail : CaveGlowCreature
{
    [SerializeField] private float size = 0.55f;
    [SerializeField] private float creepSpeed = 0.18f;
    [SerializeField] private float guidedSpeed = 0.8f;
    [Tooltip("How far it wanders from where it was placed (or last led).")]
    [SerializeField] private float roam = 3f;

    public override string MusicLayer => "BGM_bell";
    public override bool CanBeCarried => false;
    public bool Closed => closed > 0.5f;
    public Vector3 SurfaceNormal => normal;
    public override Vector3 AimPoint => shell != null ? shell.position : transform.position;

    private Vector3 normal = Vector3.up, heading = Vector3.forward, home, wanderDir;
    private float closed, calmTimer, wanderTimer, phase;
    private Transform shell, foot;
    private readonly Transform[] stalks = new Transform[2];
    private bool placed;

    /// <summary>Puts the snail on a rock surface.</summary>
    public void Attach(Vector3 point, Vector3 surfaceNormal)
    {
        transform.position = point;
        normal = surfaceNormal.normalized;
        home = point;
        heading = Vector3.ProjectOnPlane(Random.onUnitSphere, normal).normalized;
        placed = true;
        Orient(1f);
    }

    protected override void InitializeCreature()
    {
        base.InitializeCreature();
        glowColor = new Color(1f, 0.62f, 0.12f);
        voiceReach = 10f;
        phase = Random.value * 10f;
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        var col = gameObject.AddComponent<SphereCollider>();
        col.center = new Vector3(0f, size * 0.45f, 0f);
        col.radius = size * 0.45f;

        Material glow = GlowMaterial(glowColor * 2.4f, 0.75f, 1.3f);
        Material deep = GlowMaterial(glowColor * 0.9f, 0.6f, 0.6f);
        Material ink = InkMaterial(new Color(0.035f, 0.03f, 0.03f));
        foot = Part(PrimitiveType.Sphere, transform, new Vector3(0f, size * 0.08f, 0f), new Vector3(0.55f, 0.16f, 1.25f) * size, ink).transform;

        shell = new GameObject("Shell").transform;
        shell.SetParent(transform, false);
        shell.localPosition = new Vector3(0f, size * 0.45f, -size * 0.12f);
        // A coil of shrinking glowing chambers, each ringed by a dark seam: the bell.
        for (int i = 0; i < 9; i++)
        {
            float t = i / 8f;
            float a = t * Mathf.PI * 3.2f;
            float r = Mathf.Lerp(0.32f, 0.02f, t) * size;
            Vector3 at = new Vector3(0f, Mathf.Sin(a) * r, Mathf.Cos(a) * r);
            float s = Mathf.Lerp(0.5f, 0.12f, t) * size;
            Part(PrimitiveType.Sphere, shell, at, new Vector3(0.85f, 1f, 1f) * s, i % 2 == 0 ? glow : deep);
            Part(PrimitiveType.Sphere, shell, at + Vector3.right * s * 0.36f, new Vector3(0.2f, 1.05f, 1.05f) * s, ink);
        }
        for (int i = 0; i < 2; i++)
        {
            stalks[i] = new GameObject("Stalk").transform;
            stalks[i].SetParent(transform, false);
            stalks[i].localPosition = new Vector3((i - 0.5f) * size * 0.22f, size * 0.15f, size * 0.55f);
            Part(PrimitiveType.Cylinder, stalks[i], new Vector3(0f, size * 0.18f, 0f), new Vector3(0.03f, 0.18f, 0.03f) * size, ink);
            Part(PrimitiveType.Sphere, stalks[i], new Vector3(0f, size * 0.38f, 0f), Vector3.one * 0.07f * size, glow);
        }
        AddHalo(shell, Vector3.zero, 2.8f, 0.8f);
        AddLamp(shell, 6f, 1.8f);
        if (!placed) { home = transform.position; normal = transform.up; heading = transform.forward; }
    }

    protected override void TickCreature(float deltaTime)
    {
        // Shut in at the Maw's voice; open again a few seconds after it is gone.
        LureMaw maw = Hear<LureMaw>(out float fear);
        if (maw != null && fear > 0.1f) calmTimer = 3f;
        if (Hear<ToneCrate>(out float bang) is ToneCrate crate && crate.Thumping && bang > 0f) calmTimer = Mathf.Max(calmTimer, 2f);
        else calmTimer -= deltaTime;
        bool shut = calmTimer > 0f;
        closed = Mathf.MoveTowards(closed, shut ? 1f : 0f, deltaTime * (shut ? 2.5f : 0.6f));

        if (!shut && !Muted)
        {
            Vector3 p = transform.position;
            if (Guide.HasValue)
            {
                Vector3 to = Guide.Value - p;
                if (to.magnitude > 0.4f && Crawl(ref p, ref normal, to, guidedSpeed * deltaTime))
                {
                    heading = Vector3.ProjectOnPlane(to, normal).normalized;
                    home = p;
                }
            }
            else
            {
                wanderTimer -= deltaTime;
                if (wanderTimer <= 0f)
                {
                    wanderTimer = Random.Range(4f, 9f);
                    Vector3 back = home - p;
                    wanderDir = back.magnitude > roam ? back : Random.onUnitSphere;
                }
                Vector3 dir = Vector3.Slerp(heading, Vector3.ProjectOnPlane(wanderDir, normal).normalized, deltaTime * 0.4f);
                if (Crawl(ref p, ref normal, dir, creepSpeed * deltaTime)) heading = Vector3.ProjectOnPlane(dir, normal).normalized;
                else wanderTimer = 0f;
            }
            MoveCreature(p - transform.position);
        }
        Orient(deltaTime);
        Animate();
        UpdateGlow(Muted ? 0.25f : Mathf.Lerp(1f, 0.06f, closed), deltaTime);
    }

    private void Orient(float deltaTime)
    {
        if (heading.sqrMagnitude < 1e-4f) heading = Vector3.ProjectOnPlane(transform.forward, normal);
        if (heading.sqrMagnitude < 1e-4f) return;
        Quaternion want = Quaternion.LookRotation(heading.normalized, normal);
        FaceCreature(Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-4f * deltaTime)));
    }

    private void Animate()
    {
        // The foot and stalks draw into the shell as it shuts.
        float open = 1f - closed;
        float pulse = 1f + Mathf.Sin(Time.time * 0.9f + phase) * 0.04f * open;
        shell.localScale = Vector3.one * pulse;
        shell.localPosition = new Vector3(0f, size * Mathf.Lerp(0.3f, 0.45f, open), -size * 0.12f);
        foot.localScale = new Vector3(0.55f, 0.16f, Mathf.Lerp(0.5f, 1.25f, open) * (1f + Mathf.Sin(Time.time * 2f + phase) * 0.05f)) * size;
        for (int i = 0; i < 2; i++)
        {
            stalks[i].localScale = Vector3.one * Mathf.Max(0.01f, open);
            stalks[i].localRotation = Quaternion.Euler(20f + Mathf.Sin(Time.time * 1.1f + i + phase) * 12f, (i - 0.5f) * 30f, 0f);
        }
    }
}
