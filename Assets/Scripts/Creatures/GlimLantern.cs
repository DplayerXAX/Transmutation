using UnityEngine;

/// <summary>
/// Glim Lantern: a small floating cyan bulb with four black fins and a skirt of trailing threads. It is a living
/// lamp: its light reaches a few metres and can be carried (RMB) to see further in the tunnels.
/// Chain: it drifts to Bell Snails (feeds round their amber shells), flees the Lure Maw's voice, and is the Maw's prey.
/// Baton: played alone it floats straight to the guide point (it flies, so it can cross gaps and climb shafts).
/// Eaten, it bursts and a new one buds from a Bell Snail a while later.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class GlimLantern : CaveGlowCreature
{
    [SerializeField] private float size = 0.32f;
    [SerializeField] private float driftSpeed = 0.7f;
    [SerializeField] private float guidedSpeed = 7f;
    [SerializeField] private float fleeSpeed = 3.2f;
    [Tooltip("Seconds before an eaten glim buds again from a snail.")]
    [SerializeField] private float regrowSeconds = 25f;

    public override string MusicLayer => "BGM_glim";
    public override bool IgnoreCarrySizeLimit => true;
    public override bool CanBeCarried => !Hidden;
    public override Vector3 AimPoint => body != null ? body.position : transform.position;

    private Transform body;
    private readonly Transform[] fins = new Transform[4];
    private LineRenderer[] threads;
    private Vector3[][] threadPoints;
    private Vector3 velocity, home, wander;
    private float wanderTimer, phase, regrow;
    private bool wasCarried;
    private SphereCollider sphere;
    private Renderer[] renderers;

    public void SetHome(Vector3 point) { home = point; wander = point; }

    protected override void InitializeCreature()
    {
        base.InitializeCreature();
        glowColor = new Color(0.25f, 0.95f, 1f);
        voiceReach = 12f;
        phase = Random.value * 10f;
        home = wander = transform.position;
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        sphere = gameObject.AddComponent<SphereCollider>();
        sphere.radius = size * 0.7f;

        Material glow = GlowMaterial(glowColor * 2.2f, 0.9f, 1.4f);
        Material ink = InkMaterial(new Color(0.03f, 0.03f, 0.035f));
        body = new GameObject("Bulb").transform;
        body.SetParent(transform, false);
        Part(PrimitiveType.Sphere, body, Vector3.zero, new Vector3(1f, 1.15f, 1f) * size, glow);
        Part(PrimitiveType.Sphere, body, new Vector3(0f, size * 0.55f, 0f), new Vector3(0.55f, 0.3f, 0.55f) * size, ink); // cap
        for (int i = 0; i < 4; i++)
        {
            var pivot = new GameObject("Fin").transform;
            pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(0f, size * 0.3f, 0f);
            pivot.localRotation = Quaternion.Euler(0f, i * 90f + 45f, 0f);
            Part(PrimitiveType.Cube, pivot, new Vector3(0f, 0f, size * 0.55f), new Vector3(0.02f, size * 0.35f, size * 0.7f), ink);
            fins[i] = pivot;
        }
        Material threadMat = LineGlowMaterial();
        threads = new LineRenderer[5];
        threadPoints = new Vector3[5][];
        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = Line(transform, "Thread", threadMat, 0.012f, 7);
            threads[i].widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.2f);
            threads[i].startColor = glowColor * 1.4f;
            threads[i].endColor = glowColor * 0.3f;
            threadPoints[i] = new Vector3[7];
            for (int k = 0; k < 7; k++) threadPoints[i][k] = transform.position - Vector3.up * k * 0.08f;
        }
        AddHalo(body, Vector3.zero, 2.4f, 0.9f);
        AddLamp(body, 5.5f, 1.6f);
        renderers = GetComponentsInChildren<Renderer>();
    }

    protected override void TickCreature(float deltaTime)
    {
        if (Hidden)
        {
            regrow -= deltaTime;
            if (regrow <= 0f) Regrow();
            UpdateGlow(0f, deltaTime);
            return;
        }
        if (wasCarried && !IsCarried) { home = wander = transform.position; velocity = Vector3.zero; }
        wasCarried = IsCarried;
        if (!IsCarried && !IsBubbleCaptured)
        {
            Vector3 desired = Vector3.zero;
            float speed = driftSpeed;
            if (Muted)
                desired = Vector3.zero;
            else if (Guide.HasValue)
            {
                desired = GuidedTarget(size * 0.6f, false, Vector3.up) - transform.position;
                speed = guidedSpeed;
                // Wherever it is led, it settles there.
                if (desired.magnitude < 1.5f) { home = transform.position; wander = home; }
            }
            else
            {
                LureMaw maw = Hear<LureMaw>(out float fear);
                Vector3 fearFrom = maw != null ? maw.transform.position : transform.position;
                if (Hear<ToneCrate>(out float bang) is ToneCrate crate && crate.Thumping && bang > fear) { fear = bang; fearFrom = crate.transform.position; }
                BellSnail snail = Hear<BellSnail>(out float hunger, 1.6f);
                if (fear > 0.05f)
                {
                    desired = transform.position - fearFrom;
                    desired.y *= 0.3f;
                    speed = fleeSpeed * Mathf.Lerp(0.5f, 1f, fear);
                }
                else if (snail != null && !snail.Closed)
                {
                    // Circle the snail's shell, a little off its surface.
                    float a = Time.time * 0.6f + phase;
                    Vector3 around = snail.SurfaceNormal * 1.1f + new Vector3(Mathf.Cos(a), 0.3f * Mathf.Sin(a * 1.7f), Mathf.Sin(a)) * 0.9f;
                    desired = snail.transform.position + around - transform.position;
                    if (Vector3.Distance(home, snail.transform.position) > 2f) home = snail.transform.position + snail.SurfaceNormal * 1.2f;
                }
                else
                {
                    wanderTimer -= deltaTime;
                    if (wanderTimer <= 0f || Vector3.Distance(transform.position, wander) < 0.3f)
                    {
                        wanderTimer = Random.Range(2.5f, 5f);
                        wander = home + Random.insideUnitSphere * 2.5f;
                    }
                    desired = wander - transform.position;
                }
            }
            Vector3 want = desired.sqrMagnitude > 1e-4f ? Vector3.ClampMagnitude(desired, speed) : Vector3.zero;
            // A gentle bob even when it holds still.
            want += Vector3.up * Mathf.Sin(Time.time * 1.3f + phase) * 0.12f;
            velocity = Follow(velocity, want, Guide.HasValue ? 7f : 1.4f, deltaTime);
            Vector3 p = transform.position;
            Fly(ref p, velocity * deltaTime, size * 0.6f);
            MoveCreature(p - transform.position);
        }
        else velocity = Vector3.zero;

        Animate(deltaTime);
        UpdateGlow(Muted ? 0.22f : 1f, deltaTime);
    }

    private void Animate(float deltaTime)
    {
        float flap = Muted ? 0.15f : 1f;
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Sin(Time.time * 4.2f + phase + i * 0.4f) * 25f * flap;
            fins[i].localRotation = Quaternion.Euler(0f, i * 90f + 45f, 0f) * Quaternion.Euler(-20f + a, 0f, 0f);
        }
        body.localRotation = Quaternion.Euler(velocity.z * 12f, 0f, -velocity.x * 12f);
        // Threads trail behind: each point follows the one before it, a little later.
        Vector3 root = body.position - body.up * size * 0.5f;
        for (int i = 0; i < threads.Length; i++)
        {
            float a = i * Mathf.PI * 2f / threads.Length;
            Vector3[] pts = threadPoints[i];
            pts[0] = root + (body.right * Mathf.Cos(a) + body.forward * Mathf.Sin(a)) * size * 0.3f;
            for (int k = 1; k < pts.Length; k++)
            {
                Vector3 hang = pts[k - 1] - Vector3.up * 0.075f + Vector3.right * Mathf.Sin(Time.time * 1.7f + k * 0.6f + i) * 0.012f;
                pts[k] = Follow(pts[k], hang, 9f - k, deltaTime);
                Vector3 off = pts[k] - pts[k - 1];
                if (off.magnitude > 0.09f) pts[k] = pts[k - 1] + off.normalized * 0.09f;
            }
            threads[i].SetPositions(pts);
        }
    }

    /// <summary>Taken by the Lure Maw: bursts and goes dark, to bud again later.</summary>
    public void Eaten()
    {
        if (Hidden || IsCarried) return;
        Hidden = true;
        regrow = regrowSeconds;
        SetSolo(false);
        sphere.enabled = false;
        foreach (Renderer r in renderers) if (r != null) r.enabled = false;
    }

    private void Regrow()
    {
        // Buds from the nearest open snail to its old home, or at its old home.
        BellSnail best = null;
        float bestD = float.MaxValue;
        foreach (CaveGlowCreature c in All)
            if (c is BellSnail s && !s.Closed)
            {
                float d = Vector3.Distance(s.transform.position, home);
                if (d < bestD) { bestD = d; best = s; }
            }
        Vector3 at = best != null ? best.transform.position + best.SurfaceNormal * 1.1f : home;
        transform.position = at;
        home = wander = at;
        velocity = Vector3.zero;
        for (int i = 0; i < threads.Length; i++)
            for (int k = 0; k < threadPoints[i].Length; k++) threadPoints[i][k] = at - Vector3.up * k * 0.08f;
        Hidden = false;
        shownGlow = 0f;
        sphere.enabled = true;
        foreach (Renderer r in renderers) if (r != null) r.enabled = true;
    }

    protected override void OnCarryStarted()
    {
        velocity = Vector3.zero;
        SetSolo(false);
    }
}
