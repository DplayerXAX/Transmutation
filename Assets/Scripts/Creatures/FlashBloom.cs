using System.Collections.Generic;
using UnityEngine;

/// <summary>A timid luminous sac. Hard impacts and damage release a defensive flash through shared luminosity.</summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class FlashBloom : Creature
{
    [Header("Defensive flash")]
    [SerializeField, Min(0f)] private float restingLuminosity = 3f;
    [SerializeField, Min(0f)] private float flashLuminosity = 1800f;
    [SerializeField, Min(0.05f)] private float flashSeconds = 0.8f;
    [SerializeField, Min(0f)] private float cooldownSeconds = 8f;
    [SerializeField, Min(0f)] private float blindRadius = 10f;
    [SerializeField, Min(0f)] private float blindSeconds = 3.5f;
    [SerializeField, Min(0f)] private float minimumPerceivedBrightness = 12f;
    private float flashRemaining, cooldownRemaining;
    private readonly List<Creature> candidates = new List<Creature>();
    private readonly List<Vector3> positions = new List<Vector3>();
    private RaycastHit[] hits = new RaycastHit[16];
    public bool IsFlashing => flashRemaining > 0f;
    public float CooldownRemaining => cooldownRemaining;
    public int LastBlindedCount { get; private set; }

    protected override void InitializeCreature()
    {
        SetCreatureName("Flash Bloom");
        SetFoodAndDiet(FoodTag.Plant, FoodTag.None);
        SetLuminosity(restingLuminosity);
        var body = GetComponent<Rigidbody>();
        body.mass = 0.6f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Transform sac = transform.Find("Glow sac");
        if (sac != null && !sac.TryGetComponent<GlowBeeLight>(out _))
            sac.gameObject.AddComponent<GlowBeeLight>().Configure(sac.GetComponent<Renderer>(),
                new Color(0.65f, 0.9f, 1f), 10f);
        Damaged += OnDamaged;
    }

    private void OnDamaged(Creature victim, float amount, Creature attacker) => TryFlash();

    public override void ReceiveImpact(CreatureImpact impact)
    {
        if (CanReceiveImpact(impact)) TryFlash();
    }

    public bool TryFlash()
    {
        if (!SimulationEnabled || !IsAvailableForPerception || cooldownRemaining > 0f || IsFlashing) return false;
        flashRemaining = Mathf.Max(0.05f, flashSeconds);
        cooldownRemaining = flashRemaining + cooldownSeconds;
        SetLuminosity(flashLuminosity);
        LastBlindedCount = 0;
        // Take a fresh registry snapshot: a newly spawned creature must see the same flash as an older one.
        CreatureRegistry.SnapshotCombat(gameObject.scene, candidates, positions);
        foreach (Creature target in candidates)
        {
            if (target == this || target is FlashBloom || !target.IsAvailableForPerception) continue;
            float squareDistance = (target.AimPosition - EmissionPosition).sqrMagnitude;
            if (squareDistance > blindRadius * blindRadius ||
                Luminosity / (1f + squareDistance) < minimumPerceivedBrightness) continue;
            // Geometric visibility only: already dazzled targets may have their recovery extended.
            if (CreatureSpatialQuery.FirstSolid(gameObject.scene, EmissionPosition, target.AimPosition,
                this, null, ref hits, out var hit) && CreatureSpatialQuery.Owner(hit.collider) != target) continue;
            FlashBlindness.Apply(target, blindSeconds);
            LastBlindedCount++;
        }
        return true;
    }

    protected override void TickCreature(float deltaTime) => AdvanceFlash(deltaTime);

    public void AdvanceFlash(float seconds)
    {
        if (!SimulationEnabled || !IsAvailableForPerception) return;
        float dt = Mathf.Max(0f, seconds);
        cooldownRemaining = Mathf.Max(0f, cooldownRemaining - dt);
        flashRemaining = Mathf.Max(0f, flashRemaining - dt);
        float fraction = flashRemaining / Mathf.Max(0.05f, flashSeconds);
        SetLuminosity(Mathf.Lerp(restingLuminosity, flashLuminosity, fraction * fraction));
    }

    protected override void OnDeath()
    {
        flashRemaining = cooldownRemaining = 0f;
        SetLuminosity(0f);
    }
}
