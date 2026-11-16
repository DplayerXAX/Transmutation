using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public sealed class Spear : Creature
{
    [SerializeField] private float stabDamage = 20f;
    private readonly HashSet<int> struck = new HashSet<int>();
    private PlayerCreatureCarrier activeCarrier;
    private Vector3 attackDirection;
    [SerializeField, Range(1f, 15f)] private float spearKnockback = 6f;

    private float chargeStarted;
    private float nextUse;
    private bool charging;

    private float dashUntil;
    private const float TapHitDuration = 0.10f;
    private const float FullChargeHitDuration = 0.45f;
    private Transform blade;
    private Vector3 bladeRest;

    public override bool Invulnerable => true;
    public override bool CanUseWhileCarried => !IsDead;
    public override string InteractionPrompt => IsCarried && !IsDead ? "Charge spear" : null;
    public override string CarriedPrompt => IsDead ? null : "[Hold LMB] Charge, release to thrust";
    private bool ThrustActive => IsCarried && SimulationEnabled && !IsDead && Carrier == activeCarrier && Time.time < dashUntil;
    private bool chargedThrust;
    private Renderer bladeRenderer;
    private MaterialPropertyBlock tint;
    private bool tinted;
    private Color idleColor;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    protected override void InitializeCreature()
    {
        SetResolvedPolarity(CreaturePolarity.None);
        SetCreatureName("Spear");
        SetFoodAndDiet(FoodTag.Matter, FoodTag.None);
        blade = transform.Find("Blade");
        if (blade == null) return;
        bladeRest = blade.localPosition;
        bladeRenderer = blade.GetComponent<Renderer>();
        if (bladeRenderer == null) return;
        tint = new MaterialPropertyBlock();
        bladeRenderer.GetPropertyBlock(tint);
        idleColor = tint.HasColor(BaseColor) ? tint.GetColor(BaseColor) : bladeRenderer.sharedMaterial.GetColor(BaseColor);
    }

    protected override void OnCarryStarted()
    {
        charging = false;
        dashUntil = 0f;
    }

    // Thrown creatures pause their simulation, but their attack tint still follows flight.
    private void LateUpdate()
    {
        bool active = ThrustActive || CreatureThrower.IsFlying(this);
        if (bladeRenderer == null || active == tinted) return;
        bladeRenderer.GetPropertyBlock(tint);
        tint.SetColor(BaseColor, active ? new Color(0.52f, 0.055f, 0.065f) : idleColor);
        bladeRenderer.SetPropertyBlock(tint);
        tinted = active;
    }

    protected override void TickCreature(float deltaTime)
    {
        if (blade == null) return;
        if (!IsCarried) charging = false;
        float charge = charging ? Mathf.Clamp01((Time.time - chargeStarted) / 0.90f) : 0f;
        blade.localPosition = bladeRest + Vector3.back * (0.35f * charge) + Vector3.forward * (ThrustActive ? 0.3f : 0f);
        blade.localRotation = Quaternion.Euler(-12f * charge, 0f, 0f);
    }

    private void FixedUpdate()
    {
        if (ThrustActive) HitCreatures(0.9f, chargedThrust ? 0.7f : 0.9f);
    }

    public void SetSpearHeld(bool pressed, bool released, Vector3 direction)
    {
        if (!IsCarried || IsDead || Carrier == null) { charging = false; return; }
        if (pressed && !charging && Time.time >= nextUse)
        {
            activeCarrier = Carrier;
            chargeStarted = Time.time;
            charging = true;
        }
        if (!charging || !released) return;
        charging = false;
        if (activeCarrier != Carrier) return;
        attackDirection = direction.normalized;
        if (attackDirection.sqrMagnitude < 0.01f) return;
        float held = Time.time - chargeStarted;
        float charge = Mathf.Clamp01((held - 0.20f) / 0.70f);
        chargedThrust = charge > 0f;
        dashUntil = Time.time + Mathf.Lerp(TapHitDuration, FullChargeHitDuration, charge);
        struck.Clear();
        HitCreatures(0.9f, 0.9f);
        if (charge > 0f && Carrier.PlayerController != null)
        {
            var player = Carrier.PlayerController;
            float height = player.GetComponent<Collider>()?.bounds.size.y ?? 2f;
            // At a fixed launch angle, ballistic range scales with the square of launch speed.
            const float fullChargeSpeedInHeightsPerSecond = 15.4f;
            float launchSpeed = fullChargeSpeedInHeightsPerSecond * height * charge;
            float brakeDelay = Mathf.Lerp(0.12f, 0.55f, charge);
            player.AddMovementImpulse(attackDirection * launchSpeed, brakeDelay);
            nextUse = Time.time + 0.65f;
        }
        else nextUse = Time.time + 0.25f;
    }

    public void CancelSpearCharge() => charging = false;

    private void HitCreatures(float reachInHeights, float minDot)
    {
        if (Carrier == null || Carrier.PlayerController == null) return;
        var player = Carrier.PlayerController;
        float height = player.GetComponent<Collider>()?.bounds.size.y ?? 2f;
        Vector3 origin = player.transform.position + Vector3.up * height * 0.55f;
        float reach = reachInHeights * height;
        Vector3 center = origin + attackDirection * reach * 0.5f;
        foreach (var collider in Physics.OverlapSphere(center, reach * 0.7f, ~0, QueryTriggerInteraction.Ignore))
        {
            ICombatTarget target = CombatTarget.Owner(collider);
            if (!CombatTarget.Exists(target) || target.TargetComponent == this ||
                target.TargetComponent.transform.IsChildOf(player.transform)) continue;
            Vector3 toward = target.AimPosition - origin;
            Vector3 aimToTarget = toward;
            Vector3 facing = attackDirection;
            if (toward.magnitude > reach + height * 0.25f ||
                aimToTarget.sqrMagnitude < 0.001f || Vector3.Dot(aimToTarget.normalized, facing.normalized) < minDot) continue;
            int id = target.TargetComponent.GetInstanceID();
            if (struck.Contains(id)) continue;
            bool blocked = false;
            foreach (var obstacle in Physics.RaycastAll(origin, toward.normalized, toward.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (obstacle.collider.transform.IsChildOf(player.transform) ||
                    obstacle.collider.transform.IsChildOf(transform) ||
                    CombatTarget.Owner(obstacle.collider) == target) continue;
                blocked = true;
                break;
            }
            if (blocked) continue;
            struck.Add(id);
            ApplySpearHit(target, collider.attachedRigidbody, attackDirection);
        }
    }

    public override void ReceiveThrownImpact(CreatureImpact impact)
    {
        base.ReceiveThrownImpact(impact);
        if (impact.OtherCollider == null) return;
        var target = CombatTarget.Owner(impact.OtherCollider);
        if (CombatTarget.Exists(target))
            ApplySpearHit(target, impact.OtherCollider.attachedRigidbody, impact.ReceiverVelocity.normalized);
    }

    private void ApplySpearHit(ICombatTarget target, Rigidbody body, Vector3 direction)
    {
        target.ApplyDamage(stabDamage, this);
        CreatureImpulse.Apply(body, direction * spearKnockback, 0.30f);
    }
}
