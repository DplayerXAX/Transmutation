using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A roaming, rideable compost back. Death eligibility uses the existing Creature.IsDead contract.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CompostRideSurface))]
[DefaultExecutionOrder(-200)]
public sealed class CompostCreature : Creature
{
    [Header("Back and Output")]
    [SerializeField] private BoxCollider back;
    [SerializeField] private GooCreature gooPrefab;
    [SerializeField] private Transform[] legs = Array.Empty<Transform>();
    [Min(0.01f)] [SerializeField] private float absorptionSeconds = 5f;
    [Min(1)] [SerializeField] private int fullnessThreshold = 50;
    [Range(0.1f, 0.95f)] [SerializeField] private float fullSinkFraction = 0.8f;
    [Min(1)] [SerializeField] private int gooCost = 10;
    [Min(0.01f)] [SerializeField] private float productionSeconds = 7f;

    [Header("Roaming")]
    [Min(0f)] [SerializeField] private float walkSpeed = 0.6f;
    [Min(0f)] [SerializeField] private float roamRadius = 12f;
    [Min(1f)] [SerializeField] private float turnSpeed = 45f;
    [Range(0f, 60f)] [SerializeField] private float maxGroundSlope = 36f;
    [Min(0.01f)] [SerializeField] private float maxStepHeight = 0.35f;
    [SerializeField] private LayerMask groundLayers = (1 << 7) | (1 << 8);

    private readonly List<CompostAbsorption> absorptions = new List<CompostAbsorption>();
    private Collider[] overlap = new Collider[64];
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private readonly Collider[] outputOverlaps = new Collider[32];
    private Rigidbody body;
    private Vector3 home, destination;
    private Quaternion[] legRest;
    private float wanderTimer, avoidanceTimer, gait, lastWalkSpeed;
    private long storedCharges;
    private bool producing;
    private float productionTime;

    public override bool CanBeCarried => false;
    public long StoredCharges => storedCharges;
    public bool IsFull => storedCharges >= fullnessThreshold;
    public bool IsProducing => producing;
    public float ProductionProgress => producing ? Mathf.Clamp01(productionTime / productionSeconds) : 0f;
    public int AbsorbingBodyCount => absorptions.Count;
    public BoxCollider Back => back;
    public CompostRideSurface RideSurface { get; private set; }
    public Collider[] BodyColliders { get; private set; }
    public float BackTopWS => back.transform.TransformPoint(back.center + Vector3.up * back.size.y * 0.5f).y;

    protected override void InitializeCreature()
    {
        SetCreatureName("Compost Creature");
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        RideSurface = GetComponent<CompostRideSurface>();
        if (back == null) back = RideSurface.Back;
        BodyColliders = GetComponentsInChildren<Collider>();
        home = transform.position;
        destination = home;
        legRest = new Quaternion[legs.Length];
        for (int i = 0; i < legs.Length; i++) legRest[i] = legs[i] != null ? legs[i].localRotation : Quaternion.identity;
        RideSurface.PlanMotion(body.position, body.rotation, Time.fixedDeltaTime);
    }

    public static long ChargesForNutrition(float nutrition)
    {
        if (float.IsNaN(nutrition) || nutrition < 20f) return 1;
        double value = Math.Floor(nutrition / 20d);
        return value >= long.MaxValue ? long.MaxValue : Math.Max(1L, (long)value);
    }

    protected override void TickCreature(float deltaTime)
    {
        AdvanceProcessing(deltaTime);
        gait += lastWalkSpeed * deltaTime * 5f;
        for (int i = 0; i < legs.Length; i++)
        {
            if (legs[i] == null) continue;
            float phase = gait + (i == 0 || i == 3 ? 0f : Mathf.PI);
            float swing = lastWalkSpeed > 0.01f ? Mathf.Sin(phase) * 16f : 0f;
            legs[i].localRotation = legRest[i] * Quaternion.Euler(swing, 0f, 0f);
        }
    }

    internal void AdvanceProcessing(float deltaTime)
    {
        if (!isActiveAndEnabled || !SimulationEnabled || deltaTime <= 0f) return;
        // Stable list order is first confirmed back contact; each deposit affects the next body's threshold.
        for (int i = 0; i < absorptions.Count;)
        {
            CompostAbsorption absorption = absorptions[i];
            if (absorption == null || absorption.Owner != this || !absorption.IsValid)
            {
                if (absorption != null && absorption.Owner == this) absorption.Release(false);
                absorptions.RemoveAt(i);
                continue;
            }
            float limit = IsFull ? Mathf.Max(absorption.Progress, fullSinkFraction) : 1f;
            absorption.Progress = Mathf.MoveTowards(absorption.Progress, limit, deltaTime / absorptionSeconds);
            if (absorption.Progress >= 1f && !IsFull)
            {
                long value = absorption.ChargeValue;
                if (absorption.Complete()) storedCharges = storedCharges > long.MaxValue - value ? long.MaxValue : storedCharges + value;
                absorptions.RemoveAt(i);
            }
            else i++;
        }

        if (producing)
        {
            productionTime = Mathf.Min(productionSeconds, productionTime + deltaTime);
            if (productionTime >= productionSeconds && TryReleaseGoo())
            {
                producing = false;
                productionTime = 0f;
            }
        }
        if (!producing && storedCharges >= gooCost && gooPrefab != null)
        {
            storedCharges -= gooCost;
            producing = true;
            productionTime = 0f;
        }
    }

    private void FixedUpdate()
    {
        if (body == null || back == null) return;
        Vector3 next = body.position;
        Quaternion rotation = body.rotation;
        lastWalkSpeed = 0f;
        if (SimulationEnabled && isActiveAndEnabled && walkSpeed > 0f && roamRadius > 0f)
        {
            wanderTimer -= Time.fixedDeltaTime;
            avoidanceTimer = Mathf.Max(0f, avoidanceTimer - Time.fixedDeltaTime);
            if (wanderTimer <= 0f || Vector3.ProjectOnPlane(destination - next, Vector3.up).sqrMagnitude < 0.3f)
                ChooseDestination();
            Vector3 heading = Vector3.ProjectOnPlane(destination - next, Vector3.up);
            if (heading.sqrMagnitude > 0.01f)
            {
                rotation = Quaternion.RotateTowards(rotation, Quaternion.LookRotation(heading), turnSpeed * Time.fixedDeltaTime);
                Vector3 forward = rotation * Vector3.forward;
                Vector3 right = rotation * Vector3.right;
                Vector3 candidate = next + forward * (walkSpeed * Time.fixedDeltaTime);
                bool withinHome = Vector3.ProjectOnPlane(candidate - home, Vector3.up).sqrMagnitude <= roamRadius * roamRadius;
                if (withinHome && CanWalk(candidate, forward, right, out float groundY))
                {
                    next = new Vector3(candidate.x, groundY, candidate.z);
                    lastWalkSpeed = walkSpeed;
                }
                else if (avoidanceTimer <= 0f)
                {
                    // Hold an escape heading long enough to turn; rerolling every step causes wall jitter.
                    Vector3 away = -forward + right * (UnityEngine.Random.value < 0.5f ? -0.8f : 0.8f);
                    Vector3 offset = Vector3.ProjectOnPlane(body.position + away.normalized * 3f - home, Vector3.up);
                    destination = home + Vector3.ClampMagnitude(offset, roamRadius);
                    avoidanceTimer = 4f;
                    wanderTimer = 6f;
                }
            }
        }
        RideSurface.PlanMotion(next, rotation, Time.fixedDeltaTime);
        body.MovePosition(next);
        body.MoveRotation(rotation);
        if (SimulationEnabled && isActiveAndEnabled) ScanBack();
        foreach (CompostAbsorption absorption in absorptions)
            if (absorption != null && absorption.Owner == this) absorption.FollowBack();
    }

    private void ChooseDestination()
    {
        Vector2 offset = UnityEngine.Random.insideUnitCircle * roamRadius;
        destination = home + new Vector3(offset.x, 0f, offset.y);
        wanderTimer = UnityEngine.Random.Range(3f, 7f);
    }

    private bool CanWalk(Vector3 candidate, Vector3 forward, Vector3 right, out float groundY)
    {
        groundY = candidate.y;
        if (!GroundAt(candidate, out RaycastHit ground)) return false;
        groundY = ground.point.y;
        if (Mathf.Abs(groundY - body.position.y) > maxStepHeight) return false;
        // Both front feet need support; narrow ledges cannot carry the wide shell.
        if (!GroundAt(candidate + forward * 1.05f + right * 0.4f, out RaycastHit left) ||
            !GroundAt(candidate + forward * 1.05f - right * 0.4f, out RaycastHit rightHit) ||
            Mathf.Abs(left.point.y - groundY) > maxStepHeight || Mathf.Abs(rightHit.point.y - groundY) > maxStepHeight) return false;
        int count = gameObject.scene.GetPhysicsScene().SphereCast(body.position + Vector3.up * 1.3f, 0.44f, forward,
            hits, 1.15f + walkSpeed * Time.fixedDeltaTime, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider obstacle = hits[i].collider;
            if (obstacle.GetComponentInParent<CompostCreature>() == this ||
                obstacle.GetComponentInParent<CompostAbsorption>()?.Owner == this) continue;
            return false;
        }
        return true;
    }

    private bool GroundAt(Vector3 position, out RaycastHit ground)
    {
        ground = default;
        int count = gameObject.scene.GetPhysicsScene().Raycast(position + Vector3.up * (maxStepHeight + 0.15f), Vector3.down,
            hits, maxStepHeight * 2f + 0.3f, groundLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider.GetComponentInParent<Creature>() != null || hit.distance >= nearest ||
                Vector3.Angle(hit.normal, Vector3.up) > maxGroundSlope) continue;
            nearest = hit.distance;
            ground = hit;
        }
        return nearest < float.MaxValue;
    }

    private void ScanBack()
    {
        Vector3 centre = back.transform.TransformPoint(back.center + Vector3.up * (back.size.y * 0.5f + 0.04f));
        Vector3 half = Vector3.Scale(back.size, back.transform.lossyScale) * 0.5f;
        half.y = 0.08f;
        int count = gameObject.scene.GetPhysicsScene().OverlapBox(centre, half, overlap, back.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        if (count == overlap.Length)
        {
            Array.Resize(ref overlap, overlap.Length * 2);
            count = gameObject.scene.GetPhysicsScene().OverlapBox(centre, half, overlap, back.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        }
        for (int i = 0; i < count; i++)
        {
            Creature target = overlap[i].GetComponentInParent<Creature>();
            if (target != null) TryBeginAbsorption(target, overlap[i]);
        }
    }

    internal bool TryBeginAbsorption(Creature target, Collider contactCollider)
    {
        if (!SimulationEnabled || !isActiveAndEnabled || back == null || target == null || target == this || !target.IsDead ||
            !target.isActiveAndEnabled || target.IsCarried || target.IsBubbleCaptured || target.IsConsumptionPending ||
            contactCollider == null || contactCollider.isTrigger) return false;
        if (target.TryGetComponent<CompostAbsorption>(out var existing) && existing.Owner != null) return false;
        Bounds contactBounds = contactCollider.bounds;
        Vector3 centreOS = back.transform.InverseTransformPoint(contactBounds.center) - back.center;
        if (Mathf.Abs(centreOS.x) > back.size.x * 0.5f || Mathf.Abs(centreOS.z) > back.size.z * 0.5f ||
            Mathf.Abs(contactBounds.min.y - BackTopWS) > 0.045f) return false;
        Vector3 foot = new Vector3(contactBounds.center.x, contactBounds.min.y + 0.05f, contactBounds.center.z);
        int count = gameObject.scene.GetPhysicsScene().Raycast(foot, Vector3.down, hits, 0.12f, ~0, QueryTriggerInteraction.Ignore);
        Collider support = null;
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.GetComponentInParent<Creature>() == target || hits[i].distance >= nearest) continue;
            support = hits[i].collider;
            nearest = hits[i].distance;
        }
        if (support != back || !CreatureBounds.TryMeasure(target.transform, out Bounds bounds)) return false;
        CompostAbsorption claim = existing != null ? existing : target.gameObject.AddComponent<CompostAbsorption>();
        if (!claim.TryBegin(this, target, bounds, ChargesForNutrition(target.Nutrition))) return false;
        absorptions.Add(claim);
        return true;
    }

    private bool TryReleaseGoo()
    {
        if (gooPrefab == null) return false;
        Vector3 position = body.position - transform.forward * 1.55f;
        // Drop just above known ground, or retain the finished batch at an unsafe ledge.
        if (!GroundAt(position, out RaycastHit ground)) return false;
        position.y = ground.point.y + 0.12f;
        Vector3 centre = position + Vector3.up * 0.2f;
        int count = gameObject.scene.GetPhysicsScene().OverlapSphere(centre, 0.31f, outputOverlaps, ~0, QueryTriggerInteraction.Ignore);
        if (count == outputOverlaps.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider obstacle = outputOverlaps[i];
            if (obstacle.GetComponentInParent<CompostCreature>() == this || obstacle == ground.collider) continue;
            return false;
        }
        GooCreature goo = Instantiate(gooPrefab, position, body.rotation);
        SceneManager.MoveGameObjectToScene(goo.gameObject, gameObject.scene);
        return true;
    }

    protected override bool KeepCorpseStationary => true;

    protected override void OnDeath()
    {
        ReleaseAll();
        storedCharges = 0;
        producing = false;
        productionTime = 0f;
        lastWalkSpeed = 0f;
        if (body != null) RideSurface.PlanMotion(body.position, body.rotation, Time.fixedDeltaTime);
    }

    private void ReleaseAll()
    {
        foreach (CompostAbsorption absorption in absorptions)
            if (absorption != null && absorption.Owner == this) absorption.Release(true);
        absorptions.Clear();
    }

    protected override void OnDisable()
    {
        ReleaseAll();
        if (body != null && RideSurface != null) RideSurface.PlanMotion(body.position, body.rotation, Time.fixedDeltaTime);
        base.OnDisable();
    }
}
