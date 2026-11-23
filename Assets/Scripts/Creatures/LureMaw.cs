using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lure Maw: a long, low, black crawler (five body rings) with a gaping magenta-lit mouth and a glowing lure bulb on
/// a stalk arching over its head. In the dark only the lure and the mouth show. It hunts by ear.
/// Chain: it hears Glims (their voice) and crawls after the loudest one; a Glim it reaches is eaten, then it rests.
/// It also comes to look when a Tone Crate thumps into a wall.
/// Its own voice frightens Glims away and shuts Bell Snails. So a Glim played alone with the baton calls the Maw
/// from far away; a muted Glim is safe; a muted Maw stops dead and goes dim.
/// Baton: played alone it crawls over the tunnel floor towards the guide point (floors only, no walls).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class LureMaw : CaveGlowCreature
{
    [SerializeField] private float crawlSpeed = 1.3f;
    [SerializeField] private float guidedSpeed = 5f;
    [Tooltip("Metres from its den it roams while nothing calls it.")]
    [SerializeField] private float roam = 5f;
    [Tooltip("Seconds it rests after eating.")]
    [SerializeField] private float restSeconds = 14f;
    [Tooltip("It hears Glims this many times further than their voice carries.")]
    [SerializeField] private float hearing = 1f;

    public override string MusicLayer => "BGM_maw";
    public override bool CanBeCarried => false;
    public override Vector3 AimPoint => lure != null ? lure.position : transform.position;

    private const int Rings = 5;
    private const float Spacing = 0.48f;
    private readonly Transform[] rings = new Transform[Rings];
    private readonly SphereCollider[] ringColliders = new SphereCollider[Rings];
    private readonly List<Vector3> trail = new List<Vector3>();
    private Transform head, jaw, lure;
    private LineRenderer stalk;
    private readonly Vector3[] stalkPoints = new Vector3[8];
    private Vector3 normal = Vector3.up, heading = Vector3.forward, den, wanderTarget;
    private float rest, gape, phase, wanderTimer;
    private GlimLantern prey;

    public void SetDen(Vector3 floorPoint, Vector3 facing)
    {
        transform.position = floorPoint;
        den = floorPoint;
        heading = Vector3.ProjectOnPlane(facing, Vector3.up).normalized;
        trail.Clear();
        for (int i = 0; i <= Rings * 6; i++) trail.Add(floorPoint - heading * i * Spacing / 6f);
        FaceCreature(Quaternion.LookRotation(heading, Vector3.up));
        PlaceRings(1f);
    }

    protected override void InitializeCreature()
    {
        base.InitializeCreature();
        glowColor = new Color(1f, 0.16f, 0.82f);
        voiceReach = 10f;
        phase = Random.value * 10f;
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        Material ink = InkMaterial(new Color(0.03f, 0.025f, 0.035f));
        Material glow = GlowMaterial(glowColor * 2.6f, 0.9f, 1.3f);
        Material gums = GlowMaterial(glowColor * 1.1f, 0.5f, 1f);

        head = new GameObject("Head").transform;
        head.SetParent(transform, false);
        Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.42f, 0f), new Vector3(0.95f, 0.62f, 0.9f), ink);
        // Lit gums inside the mouth and a row of glowing teeth round it.
        Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.36f, 0.3f), new Vector3(0.62f, 0.34f, 0.4f), gums);
        jaw = new GameObject("Jaw").transform;
        jaw.SetParent(head, false);
        jaw.localPosition = new Vector3(0f, 0.28f, 0.05f);
        Part(PrimitiveType.Sphere, jaw, new Vector3(0f, -0.06f, 0.22f), new Vector3(0.85f, 0.26f, 0.7f), ink);
        for (int i = 0; i < 7; i++)
        {
            float a = Mathf.Lerp(-70f, 70f, i / 6f) * Mathf.Deg2Rad;
            Vector3 at = new Vector3(Mathf.Sin(a) * 0.36f, 0.27f, 0.22f + Mathf.Cos(a) * 0.28f);
            var tooth = Part(PrimitiveType.Capsule, head, at, new Vector3(0.035f, 0.07f, 0.035f), glow);
            tooth.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
        }
        for (int i = 0; i < Rings; i++)
        {
            var ring = new GameObject("Ring " + i).transform;
            float s = Mathf.Lerp(0.85f, 0.42f, i / (Rings - 1f));
            Part(PrimitiveType.Sphere, ring, new Vector3(0f, s * 0.42f, 0f), new Vector3(s, s * 0.7f, s * 0.95f), ink);
            // A faint glowing seam on each ring: it reads as a body when the lure lights it.
            Part(PrimitiveType.Cube, ring, new Vector3(0f, s * 0.75f, 0f), new Vector3(s * 0.7f, 0.02f, 0.03f), gums);
            for (int side = -1; side <= 1; side += 2)
            {
                var leg = Part(PrimitiveType.Capsule, ring, new Vector3(side * s * 0.5f, 0.12f, 0f), new Vector3(0.07f, 0.16f, 0.07f), ink);
                leg.transform.localRotation = Quaternion.Euler(0f, 0f, side * 50f);
            }
            ring.SetParent(transform.parent, true);
            ring.name = "Lure Maw ring " + i;
            rings[i] = ring;
            ringColliders[i] = ring.gameObject.AddComponent<SphereCollider>();
            ringColliders[i].center = new Vector3(0f, s * 0.42f, 0f);
            ringColliders[i].radius = s * 0.45f;
        }
        var headCol = gameObject.AddComponent<SphereCollider>();
        headCol.center = new Vector3(0f, 0.42f, 0f);
        headCol.radius = 0.45f;

        Material stalkMat = InkMaterial(new Color(0.05f, 0.04f, 0.05f));
        stalk = Line(transform, "Lure stalk", stalkMat, 0.045f, stalkPoints.Length);
        stalk.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.35f);
        lure = new GameObject("Lure").transform;
        lure.SetParent(transform, false);
        lure.localPosition = new Vector3(0f, 1.6f, 0.9f);
        Part(PrimitiveType.Sphere, lure, Vector3.zero, Vector3.one * 0.22f, glow);
        AddHalo(lure, Vector3.zero, 2.6f, 1f);
        AddLamp(lure, 4.5f, 1.6f);
        den = transform.position;
        if (trail.Count == 0) for (int i = 0; i <= Rings * 6; i++) trail.Add(transform.position - transform.forward * i * Spacing / 6f);
    }

    protected override void TickCreature(float deltaTime)
    {
        rest -= deltaTime;
        Vector3 p = transform.position;
        Vector3 target = p;
        float speed = 0f;
        prey = null;
        if (Muted) { }
        else if (Guide.HasValue) { target = GuidedTarget(0f, true, normal, 0.55f); speed = guidedSpeed; }
        else if (rest <= 0f)
        {
            prey = Hear<GlimLantern>(out float heard, hearing);
            if (prey != null && !prey.IsCarried) { target = prey.transform.position; speed = crawlSpeed * Mathf.Lerp(0.6f, 1.2f, heard); }
            else if (Hear<ToneCrate>(out float thump) is ToneCrate crate && thump > 0.05f && Vector3.Distance(p, crate.transform.position) > 2.2f)
            {
                // A thump in the dark: it goes to see what made it (it finds only a box, and soon loses interest).
                prey = null;
                target = crate.transform.position;
                speed = crawlSpeed * 0.8f;
                wanderTarget = crate.transform.position;
                wanderTimer = 6f;
            }
            else
            {
                prey = null;
                wanderTimer -= deltaTime;
                if (wanderTimer <= 0f) { wanderTimer = Random.Range(6f, 12f); wanderTarget = den + Vector3.ProjectOnPlane(Random.insideUnitSphere, Vector3.up) * roam; }
                target = wanderTarget;
                speed = crawlSpeed * 0.35f;
            }
        }

        Vector3 to = Vector3.ProjectOnPlane(target - p, normal);
        if (speed > 0f && to.magnitude > 0.5f)
        {
            heading = Vector3.Slerp(heading, to.normalized, 1f - Mathf.Exp((Guide.HasValue ? -8f : -2.2f) * deltaTime)).normalized;
            if (Crawl(ref p, ref normal, heading, speed * deltaTime, 0.55f))
            {
                normal = Vector3.Slerp(normal, Vector3.up, 0.3f).normalized;
                MoveCreature(p - transform.position);
                RecordTrail();
            }
        }
        FaceCreature(Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(heading, normal), 1f - Mathf.Exp(-5f * deltaTime)));

        // Snap at a Glim in reach (low enough to bite).
        gape = Mathf.MoveTowards(gape, prey != null && Vector3.Distance(MouthPoint, prey.transform.position) < 3f ? 1f : 0f, deltaTime * 2f);
        if (prey != null && Vector3.Distance(MouthPoint, prey.transform.position) < 1.25f)
        {
            prey.Eaten();
            prey = null;
            rest = restSeconds;
            gape = 0f;
        }

        PlaceRings(deltaTime);
        Animate(deltaTime);
        UpdateGlow(Muted ? 0.18f : rest > 0f ? 0.45f : 1f, deltaTime);
    }

    private Vector3 MouthPoint => transform.TransformPoint(new Vector3(0f, 0.4f, 0.55f));

    private void RecordTrail()
    {
        if (trail.Count == 0 || Vector3.Distance(trail[0], transform.position) > Spacing / 6f)
        {
            trail.Insert(0, transform.position);
            if (trail.Count > Rings * 6 + 2) trail.RemoveAt(trail.Count - 1);
        }
    }

    private void PlaceRings(float deltaTime)
    {
        // Each ring sits further back along the path the head took, eased so the body flows.
        for (int i = 0; i < Rings; i++)
        {
            int k = Mathf.Min(trail.Count - 1, (i + 1) * 6);
            Vector3 at = trail[k];
            Vector3 ahead = i == 0 ? transform.position : rings[i - 1].position;
            Vector3 fwd = ahead - at;
            float wave = Mathf.Sin(Time.time * 2f - i * 0.8f + phase) * 0.04f;
            rings[i].position = Follow(rings[i].position, at + Vector3.up * wave, 8f, deltaTime);
            if (fwd.sqrMagnitude > 1e-4f)
                rings[i].rotation = Quaternion.Slerp(rings[i].rotation, Quaternion.LookRotation(fwd.normalized, normal), 1f - Mathf.Exp(-6f * deltaTime));
        }
    }

    private void Animate(float deltaTime)
    {
        jaw.localRotation = Quaternion.Euler(Mathf.Lerp(4f, 34f, gape) + Mathf.Sin(Time.time * 1.3f + phase) * 2f, 0f, 0f);
        // The lure sways ahead of the mouth, bobbing like bait.
        float sway = Muted ? 0.1f : 1f;
        Vector3 lureLocal = new Vector3(Mathf.Sin(Time.time * 0.7f + phase) * 0.25f * sway, 1.55f + Mathf.Sin(Time.time * 1.1f + phase) * 0.12f * sway,
            0.95f + Mathf.Sin(Time.time * 0.5f) * 0.1f * sway);
        lure.localPosition = Follow(lure.localPosition, lureLocal, 3f, deltaTime);
        Vector3 root = transform.TransformPoint(new Vector3(0f, 0.75f, 0.05f));
        Vector3 tip = lure.position;
        Vector3 bend = transform.TransformPoint(new Vector3(0f, 1.9f, 0.2f));
        for (int i = 0; i < stalkPoints.Length; i++)
        {
            float t = i / (stalkPoints.Length - 1f);
            stalkPoints[i] = Vector3.Lerp(Vector3.Lerp(root, bend, t), Vector3.Lerp(bend, tip, t), t);
        }
        stalk.SetPositions(stalkPoints);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        foreach (Transform r in rings) if (r != null) Destroy(r.gameObject);
    }
}
