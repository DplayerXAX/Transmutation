using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum ProjectileFlightState { Flying, Spent, Resolved }

[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public abstract class ProjectileCreature : Creature
{
    private struct Protection { public Collider Other; public bool WasIgnored; }
    private readonly List<Protection> protection = new List<Protection>(8);
    protected Rigidbody Body { get; private set; }
    private Collider solid;
    public Creature Emitter { get; private set; }
    public GameObject FiringPlayer { get; private set; }
    public ProjectileFlightState FlightState { get; private set; } = ProjectileFlightState.Spent;
    public float ImpactDamage { get; private set; }
    public float BlastRadius { get; private set; }
    public float BlastPush { get; private set; }
    public event System.Action<ProjectileFlightState> FlightChanged;

    protected override void InitializeCreature()
    {
        SetResolvedPolarity(CreaturePolarity.None);
        SetFoodAndDiet(FoodTag.Matter, FoodTag.None);
        Body = GetComponent<Rigidbody>(); solid = GetComponent<Collider>();
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Body.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public static ProjectileCreature Spawn(ProjectileCreature prefab, Creature emitter, GameObject player,
        Vector3 origin, Vector3 velocity, float damage, float radius, float push)
    {
        var staging = new GameObject("Projectile Launch Staging"); staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, emitter.gameObject.scene);
        ProjectileCreature projectile = Instantiate(prefab, origin, Quaternion.LookRotation(velocity.normalized), staging.transform);
        projectile.gameObject.SetActive(true);
        projectile.transform.SetParent(null, true);
        projectile.Launch(emitter, player, velocity, damage, radius, push);
        Destroy(staging);
        return projectile;
    }

    public void Launch(Creature emitter, GameObject player, Vector3 velocity, float damage, float radius, float push)
    {
        if (IsDead || IsConsumptionPending || IsCarried || IsBubbleCaptured) return;
        Emitter = emitter; FiringPlayer = player; ImpactDamage = damage; BlastRadius = radius; BlastPush = push;
        SetResolvedPolarity(CreaturePolarity.None);
        SetFlightState(ProjectileFlightState.Flying);
        Body.isKinematic = false; Body.useGravity = false; Body.linearDamping = 0f;
        Body.linearVelocity = velocity;
        Protect(emitter != null ? emitter.gameObject : null); Protect(player);
    }
    private void Protect(GameObject root)
    {
        if (root == null) return;
        foreach (Collider other in root.GetComponentsInChildren<Collider>(true))
        {
            if (other == solid || protection.Exists(pair => pair.Other == other)) continue;
            protection.Add(new Protection { Other = other, WasIgnored = Physics.GetIgnoreCollision(solid, other) });
            Physics.IgnoreCollision(solid, other, true);
        }
    }
    protected override void TickCreature(float deltaTime) => RestoreProtection(false);
    private void RestoreProtection(bool all)
    {
        for (int i = protection.Count - 1; i >= 0; i--)
        {
            var pair = protection[i];
            if (!all && pair.Other != null && pair.Other.bounds.Intersects(solid.bounds)) continue;
            if (pair.Other != null && solid != null) Physics.IgnoreCollision(solid, pair.Other, pair.WasIgnored);
            protection.RemoveAt(i);
        }
    }
    protected override void OnCarryStarted() => BecomeSpent();
    protected override void OnCaptureStarted() => BecomeSpent();
    public void BecomeSpent()
    {
        if (FlightState == ProjectileFlightState.Resolved || IsDead) return;
        SetFlightState(ProjectileFlightState.Spent);
        SetMovementPhysics(false, true);
        if (Body != null && !Body.isKinematic && !IsCarried) Body.linearVelocity = Vector3.zero;
        RestoreProtection(true);
    }
    protected void SetFlightState(ProjectileFlightState state)
    {
        if (FlightState == state) return;
        FlightState = state;
        if (FlightChanged != null)
            foreach (System.Action<ProjectileFlightState> listener in FlightChanged.GetInvocationList())
                try { listener(state); }
                catch (System.Exception exception) { Debug.LogException(exception, this); }
    }
    private void OnCollisionEnter(Collision collision)
    {
        if (FlightState != ProjectileFlightState.Flying || !SimulationEnabled || IsConsumptionPending) return;
        if (IsCarried || IsBubbleCaptured || (TryGetComponent<WebCapture>(out var web) && web.IsCaptured))
        { BecomeSpent(); return; }
        OnFlightImpact(CreatureSpatialQuery.Owner(collision.collider));
    }
    protected abstract void OnFlightImpact(Creature struck);

    public override void Die()
    {
        if (IsDead) return;
        base.Die();
        // Notifications observe the original body. Removal belongs to projectile death, not Age.
        SetFlightState(ProjectileFlightState.Resolved);
        RestoreProtection(true);
        Destroy(gameObject);
    }
    protected override void OnDisable() { RestoreProtection(true); base.OnDisable(); }
}
