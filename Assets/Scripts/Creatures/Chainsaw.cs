using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public sealed class Chainsaw : Creature
{
    [field: SerializeField, Min(0.1f)] public float TopSpeed { get; private set; } = 32f;
    [field: SerializeField, Min(0.1f)] public float Acceleration { get; private set; } = 8f;
    [field: SerializeField, Min(0.1f)] public float SideAcceleration { get; private set; } = 34.2f;
    [field: SerializeField, Min(0f)] public float TurnRate { get; private set; } = 65f;
    [field: SerializeField, Range(0f, 80f)] public float MaximumSlope { get; private set; } = 65f;
    [SerializeField] private Transform bladeTip;
    [Tooltip("Radius around the planted blade that can hit a creature during a drive.")]
    [SerializeField, Min(0.05f)] private float damageRadius = 1f;

    private SmoothFirstPersonController driver;
    private Rigidbody body;
    private Vector3 heading, plantedOffset, lastStart, lastTip, lastCenter;
    private Quaternion plantedRotation;
    private bool hasPose, planted, hasSweep;
    private readonly HashSet<int> contacts = new HashSet<int>();
    private readonly HashSet<int> sampleContacts = new HashSet<int>();
    private Collider[] overlaps = new Collider[16];
    private RaycastHit[] obstacles = new RaycastHit[16];

    public override bool Invulnerable => true;
    public override bool IgnoreCarrySizeLimit => true;
    public override bool CanUseWhileCarried => !IsDead;
    public override string CarriedPrompt => "[Hold LMB] Drive chainsaw   [A/D] Steer";
    public override string InteractionPrompt => IsCarried ? "Drive chainsaw" : null;

    protected override void InitializeCreature()
    {
        SetCreatureName("Chainsaw");
        SetResolvedPolarity(CreaturePolarity.None);
        SetFoodAndDiet(FoodTag.Matter, FoodTag.None);
        body = GetComponent<Rigidbody>();
    }

    protected override void TickCreature(float deltaTime) { }

    internal bool CanDrive(SmoothFirstPersonController player) =>
        isActiveAndEnabled && SimulationEnabled && !IsDead && IsCarried && Carrier.PlayerController == player;

    internal void PressTrigger()
    {
        var player = Carrier != null ? Carrier.PlayerController : null;
        if (player == null || !CanDrive(player) || !player.EngageChainsaw(this)) return;
        driver = player;
    }

    internal void ReleaseTrigger()
    {
        if (driver != null) CancelRide();
    }

    private void ClearSweep()
    {
        planted = false;
        hasSweep = false;
        contacts.Clear();
    }

    internal void CancelRide()
    {
        if (driver != null) driver.EndChainsaw(this);
        driver = null;
        hasPose = false;
        ClearSweep();
    }

    protected override void OnDisable()
    {
        CancelRide();
        base.OnDisable();
    }

    internal void UpdateRidePose(Vector3 center, Vector3 forward, RaycastHit ground, bool supported, float speed)
    {
        heading = forward;
        hasPose = true;
        if (!supported)
        {
            ClearSweep();
            ApplyRidePose();
            return;
        }

        planted = true;
        Vector3 along = Vector3.ProjectOnPlane(forward, ground.normal).normalized;
        Vector3 right = Vector3.Cross(ground.normal, along);
        Vector3 foot = center - ground.normal * Vector3.Dot(center - ground.point, ground.normal);
        Vector3 tip = foot + along * 1.4f + right * 0.34f + ground.normal * 0.06f;
        plantedRotation = Quaternion.LookRotation(along - ground.normal * 0.9f, ground.normal);
        Vector3 position = tip - plantedRotation * bladeTip.localPosition;
        plantedOffset = position - center;
        ApplyRidePose();
        Vector3 start = position + plantedRotation * (Vector3.forward * 0.22f);
        float travel = hasSweep ? Mathf.Max(Vector3.Distance(lastStart, start), Vector3.Distance(lastTip, tip)) : 0f;
        int steps = Mathf.Max(1, Mathf.CeilToInt(travel / (damageRadius * 0.5f)));
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            SampleBlade(hasSweep ? Vector3.Lerp(lastCenter, center, t) : center,
                hasSweep ? Vector3.Lerp(lastStart, start, t) : start,
                hasSweep ? Vector3.Lerp(lastTip, tip, t) : tip, speed);
        }
        lastCenter = center;
        lastStart = start;
        lastTip = tip;
        hasSweep = true;
    }

    internal bool ApplyRidePose()
    {
        if (!hasPose || driver == null || !IsCarried) return false;
        Vector3 offset = planted ? plantedOffset : heading * 0.55f +
            Vector3.Cross(Vector3.up, heading) * 0.34f + Vector3.up * 0.12f;
        Quaternion rotation = planted ? plantedRotation : Quaternion.LookRotation(heading + Vector3.up * 0.12f);
        Vector3 position = driver.transform.position + offset;
        transform.SetPositionAndRotation(position, rotation);
        body.position = position;
        body.rotation = rotation;
        return true;
    }

    private void SampleBlade(Vector3 center, Vector3 start, Vector3 tip, float speed)
    {
        int count;
        while ((count = ReflectionPhysics.OverlapCapsuleNonAlloc(start, tip, damageRadius, overlaps, ~0,
                   QueryTriggerInteraction.Ignore)) == overlaps.Length)
            Array.Resize(ref overlaps, overlaps.Length * 2);

        sampleContacts.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider collider = overlaps[i];
            ICombatTarget target = CombatTarget.Owner(collider);
            if (!CombatTarget.Exists(target) || target.TargetComponent == this ||
                target.TargetComponent.transform.IsChildOf(driver.transform)) continue;
            int id = target.TargetComponent.GetInstanceID();
            sampleContacts.Add(id);
            if (contacts.Contains(id) || speed <= 0f) continue;
            Vector3 point = collider.ClosestPoint(start);
            if (Covered(center, point, target)) continue;
            contacts.Add(id);
            target.ApplyDamage(70f * Mathf.Clamp01(speed / TopSpeed), this);
        }
        contacts.IntersectWith(sampleContacts);
    }

    private bool Covered(Vector3 from, Vector3 point, ICombatTarget target)
    {
        Vector3 ray = point - from;
        if (ray.sqrMagnitude < 0.000001f) return false;
        int count;
        while ((count = ReflectionPhysics.RaycastNonAlloc(from, ray.normalized, obstacles, ray.magnitude,
                   ~0, QueryTriggerInteraction.Ignore)) == obstacles.Length)
            Array.Resize(ref obstacles, obstacles.Length * 2);
        for (int i = 0; i < count; i++)
        {
            Collider hit = obstacles[i].collider;
            if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(driver.transform) ||
                CombatTarget.Owner(hit) == target) continue;
            return true;
        }
        return false;
    }
}
