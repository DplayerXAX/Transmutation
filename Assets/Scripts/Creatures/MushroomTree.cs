using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A giant mushroom that never moves on its own. Young, it is small enough to pick up and replant:
/// set down on Ground and left still, it roots and grows. Grown, it is fixed in place and can no longer
/// be carried; its cap is a platform (a bouncy one when bouncyCap is set) and Mushroom Essence forms on top.
/// Enough uncollected essence condenses into a BounceShroom, which drops off the cap to the ground below.
/// The body is authored as children (Stalk, Cap/Platform); growth only rescales them.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class MushroomTree : Creature
{
    [Header("Body")]
    [SerializeField] private Transform stalk;
    [SerializeField] private Transform cap;
    [SerializeField] private Renderer capRenderer;
    [SerializeField] private Material bounceCapMaterial;
    [SerializeField] private Material solidCapMaterial;

    [Header("Shape")]
    [Tooltip("Grown: height of the cap top above the foot (the pivot).")]
    [Min(0.5f)] [SerializeField] private float adultHeight = 12f;
    [Min(0.2f)] [SerializeField] private float adultCapRadius = 4f;
    [Tooltip("Young: small enough for the player to carry (the carry limit is 2.2 m).")]
    [Min(0.2f)] [SerializeField] private float youngHeight = 1.1f;
    [Min(0.1f)] [SerializeField] private float youngCapRadius = 0.45f;
    [Tooltip("Green cap that throws whatever lands on it (BouncySurface sets the launch speed). Off = solid blue cap.")]
    [SerializeField] private bool bouncyCap = true;

    [Header("Growth")]
    [Tooltip("0 = young, 1 = grown. Grown trees cannot be carried.")]
    [Range(0f, 1f)] [SerializeField] private float growth;
    [Tooltip("Seconds rooted in the ground to grow from young to grown.")]
    [Min(1f)] [SerializeField] private float growthDuration = 120f;
    [Tooltip("Seconds a dropped young tree must rest on Ground before it roots.")]
    [Min(0f)] [SerializeField] private float rootDelay = 1f;

    [Header("Essence")]
    [SerializeField] private MushroomEssence essencePrefab;
    [Tooltip("Seconds between essence forming on a grown cap.")]
    [Min(1f)] [SerializeField] private float essenceInterval = 30f;
    [Tooltip("Essence on the cap that condenses into one BounceShroom.")]
    [Min(1)] [SerializeField] private int essencePerShroom = 3;
    [SerializeField] private BounceShroom shroomPrefab;
    [Tooltip("While this many of its BounceShrooms still lie under the tree, essence just waits on the cap.")]
    [Min(0)] [SerializeField] private int maxShroomsBelow = 1;

    [Header("Runtime (Read Only)")]
    [SerializeField] private bool rooted;
    [SerializeField] private float essenceTimer;

    private const int GroundMask = 1 << 7;
    private readonly List<MushroomEssence> onCap = new List<MushroomEssence>();
    private readonly List<BounceShroom> dropped = new List<BounceShroom>();
    private Rigidbody body;
    private BouncySurface surface;
    private float restTimer;

    public float Growth => growth;
    public bool IsGrown => growth >= 1f;
    public bool Rooted => rooted || IsGrown;
    public int EssenceOnCap => onCap.Count;
    public bool BouncyCap => bouncyCap;
    /// <summary>World height of the standable cap top.</summary>
    public float CapTop => transform.position.y + Mathf.Lerp(youngHeight, adultHeight, Shaped(growth));

    public override bool CanBeCarried => !IsGrown;
    public override bool Invulnerable => IsGrown;
    protected override bool KeepCorpseStationary => IsGrown;

    protected override void InitializeCreature()
    {
        body = GetComponent<Rigidbody>();
        surface = GetComponent<BouncySurface>();
        ApplyShape();
        if (IsGrown || rooted) SetMovementPhysics(true, false);
        else SetMovementPhysics(false, true);
        if (surface != null) surface.SetBouncy(IsGrown && bouncyCap);
    }

    protected override void TickCreature(float deltaTime)
    {
        if (IsCarried) return;
        if (!IsGrown)
        {
            if (!rooted) { TryRoot(deltaTime); return; }
            growth = Mathf.Min(1f, growth + deltaTime / growthDuration);
            ApplyShape();
            if (IsGrown && surface != null) surface.SetBouncy(bouncyCap);
            return;
        }

        onCap.RemoveAll(e => e == null || !e.OnStalk || e.IsConsumptionPending);
        Vector3 foot = transform.position;
        dropped.RemoveAll(s => s == null || s.IsCarried ||
            new Vector2(s.transform.position.x - foot.x, s.transform.position.z - foot.z).magnitude > adultCapRadius + 8f);

        if (onCap.Count >= essencePerShroom)
        {
            if (shroomPrefab != null && dropped.Count < maxShroomsBelow) Condense();
            return;
        }
        essenceTimer += deltaTime;
        if (essenceTimer < essenceInterval) return;
        essenceTimer = 0f;
        SpawnEssence();
    }

    /// <summary>Grows a new essence on the cap now (also used by tests and the editor menu).</summary>
    public MushroomEssence SpawnEssence()
    {
        if (!IsGrown || essencePrefab == null || cap == null) return null;
        float radius = adultCapRadius;
        // Spread around the top so drops do not land on each other.
        float angle = (onCap.Count * 2.4f + Random.value * 0.6f);
        Vector3 at = cap.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius * Random.Range(0.1f, 0.4f) +
            Vector3.up * (0.43f * radius + 0.2f);
        MushroomEssence essence = Instantiate(essencePrefab, at, Quaternion.identity);
        essence.HoldOnStalk();
        onCap.Add(essence);
        return essence;
    }

    /// <summary>
    /// The essence on the cap turns into one BounceShroom. A solid cap keeps it on top; a bouncy cap would
    /// throw it up forever, so there it forms just past the rim and drops to the ground.
    /// </summary>
    private void Condense()
    {
        Vector3 centre = Vector3.zero;
        for (int i = 0; i < essencePerShroom; i++) centre += onCap[i].transform.position;
        centre /= essencePerShroom;
        for (int i = essencePerShroom - 1; i >= 0; i--)
        {
            onCap[i].CondenseAway();
            onCap.RemoveAt(i);
        }
        if (surface == null || !surface.IsBouncy)
        {
            dropped.Add(MushroomEssence.SpawnShroom(shroomPrefab, centre + Vector3.up * 0.3f));
            return;
        }
        Vector2 random = Random.insideUnitCircle.normalized;
        Vector3 outward = new Vector3(random.x, 0f, random.y);
        Vector3 rim = cap.position + outward * (adultCapRadius * 1.15f + 0.6f) + Vector3.up * 0.2f;
        BounceShroom shroom = MushroomEssence.SpawnShroom(shroomPrefab, rim);
        if (shroom.TryGetComponent(out Rigidbody shroomBody) && !shroomBody.isKinematic)
            shroomBody.linearVelocity = outward * 1.5f;
        dropped.Add(shroom);
    }

    private void TryRoot(float deltaTime)
    {
        bool resting = body.isKinematic || body.linearVelocity.sqrMagnitude < 0.04f;
        bool onGround = ReflectionPhysics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 0.8f, GroundMask,
            QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<MushroomTree>() != this;
        restTimer = resting && onGround ? restTimer + deltaTime : 0f;
        if (restTimer < rootDelay) return;
        restTimer = 0f;
        rooted = true;
        SetMovementPhysics(true, false);
        // Stand upright where it fell.
        transform.SetPositionAndRotation(hit.point, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
    }

    protected override void OnCarryStarted()
    {
        rooted = false;
        restTimer = 0f;
        SetMovementPhysics(false, true);
    }

    /// <summary>Sets the grown shape and stage at once: for scene authoring of grown trees.</summary>
    public void Configure(float height, float capRadius, bool bouncy, float startGrowth)
    {
        adultHeight = Mathf.Max(0.5f, height);
        adultCapRadius = Mathf.Max(0.2f, capRadius);
        bouncyCap = bouncy;
        growth = Mathf.Clamp01(startGrowth);
        ApplyShape();
    }

    /// <summary>Scales the stalk and places the cap for the current growth. The cap mesh's origin is its rim.</summary>
    public void ApplyShape()
    {
        if (stalk == null || cap == null) return;
        float t = Shaped(growth);
        float height = Mathf.Lerp(youngHeight, adultHeight, t), radius = Mathf.Lerp(youngCapRadius, adultCapRadius, t);
        float capOrigin = height - 0.35f * radius, stalkRadius = 0.25f * Mathf.Lerp(0.4f, 1f, t) + radius * 0.12f;
        stalk.localScale = new Vector3(stalkRadius, capOrigin + 0.1f * radius, stalkRadius);
        cap.localPosition = Vector3.up * capOrigin;
        cap.localScale = Vector3.one * radius;
        if (capRenderer != null)
        {
            Material material = bouncyCap ? bounceCapMaterial : solidCapMaterial;
            if (material != null) capRenderer.sharedMaterial = material;
        }
    }

    /// <summary>Slow start, then most of the height arrives in the middle of the growth.</summary>
    private static float Shaped(float g) => Mathf.SmoothStep(0f, 1f, g);

    protected override void OnDeath()
    {
        if (surface != null) surface.SetBouncy(false);
    }

    private void OnValidate()
    {
        youngHeight = Mathf.Min(youngHeight, adultHeight);
        youngCapRadius = Mathf.Min(youngCapRadius, adultCapRadius);
        ApplyShape();
    }
}
