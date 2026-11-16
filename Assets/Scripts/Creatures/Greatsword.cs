using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public sealed class Greatsword : Creature
{
    [SerializeField] private float swordDamage = 35f;
    private readonly HashSet<int> struck = new HashSet<int>();
    private enum SlamPhase { Ready, Rising, Falling, Impact }
    private SlamPhase slam;
    [SerializeField, Range(0.5f, 4f)] private float jumpHeights = 2.5f;
    [SerializeField, Range(8f, 45f)] private float slamSpeed = 24f;
    [SerializeField, Range(1f, 8f)] private float slamRadius = 6.75f;
    [SerializeField, Range(1f, 15f)] private float knockupHeights = 10f;
    private Transform blade;
    private float nextUse;
    private float swingStarted;
    public override bool IgnoreCarrySizeLimit => true;
    public override bool Invulnerable => true;
    public override bool CanUseWhileCarried => !IsDead;
    public override string InteractionPrompt => IsCarried && !IsDead ? "Leap and slam" : null;
    public override string CarriedPrompt => IsDead ? null : "[LMB] Leap and slam";

    protected override void InitializeCreature()
    {
        SetResolvedPolarity(CreaturePolarity.None);
        SetCreatureName("Greatsword");
        SetFoodAndDiet(FoodTag.Matter, FoodTag.None);
        blade = transform.Find("Blade");
    }

    protected override void OnCarryStarted() => slam = SlamPhase.Ready;

    protected override void TickCreature(float deltaTime)
    {
        if (blade == null) return;
        if (!IsCarried) slam = SlamPhase.Ready;
        float angle = slam switch
        {
            SlamPhase.Rising => 35f,
            SlamPhase.Falling => -65f,
            SlamPhase.Impact => Mathf.Lerp(-80f, 0f, Mathf.Clamp01((Time.time - swingStarted) / 0.38f)),
            _ => 0f
        };
        blade.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void FixedUpdate() => StepSlam();

    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (!IsCarried || IsDead || Time.time < nextUse) return;
        var player = Carrier.PlayerController;
        var body = Carrier.PlayerBody;
        if (player == null || body == null || body.isKinematic ||
            slam == SlamPhase.Rising || slam == SlamPhase.Falling) return;
        float height = player.GetComponent<Collider>()?.bounds.size.y ?? 2f;
        float rise = player.Grounded ? jumpHeights : jumpHeights * 0.5f;
        float lift = Mathf.Sqrt(2f * Physics.gravity.magnitude * height * rise);
        player.AddMovementImpulse(Vector3.up * Mathf.Max(0f, lift - body.linearVelocity.y));
        slam = SlamPhase.Rising;
    }

    private void StepSlam()
    {
        if (slam == SlamPhase.Ready) return;
        if (!IsCarried || IsDead || !SimulationEnabled || Carrier.PlayerBody == null || Carrier.PlayerBody.isKinematic)
        {
            slam = SlamPhase.Ready;
            return;
        }
        if (slam == SlamPhase.Impact)
        {
            if (Time.time >= nextUse) slam = SlamPhase.Ready;
            return;
        }
        var body = Carrier.PlayerBody;
        if (slam == SlamPhase.Rising)
        {
            if (body.linearVelocity.y > 0.1f) return;
            Carrier.PlayerController.AddMovementImpulse(Vector3.down * (slamSpeed + body.linearVelocity.y));
            slam = SlamPhase.Falling;
        }
        if (!Carrier.PlayerController.CastGround(out var ground)) return;
        slam = SlamPhase.Impact;
        swingStarted = Time.time;
        nextUse = Time.time + 0.45f;
        ShowImpact(ground.point, ground.normal);
        float height = Carrier.PlayerController.GetComponent<Collider>()?.bounds.size.y ?? 2f;
        struck.Clear();
        foreach (var collider in Physics.OverlapBox(ground.point + Vector3.up * height * 0.5f,
            new Vector3(slamRadius, height * 0.6f, slamRadius), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 point = collider.ClosestPoint(ground.point);
            if (Vector3.ProjectOnPlane(point - ground.point, Vector3.up).sqrMagnitude > slamRadius * slamRadius) continue;
            var target = CombatTarget.Owner(collider);
            if (target is not Creature creature || creature == this || !creature.IsCombatActive || creature.IsCarried ||
                !struck.Add(creature.GetInstanceID())) continue;
            var victim = collider.attachedRigidbody;
            creature.ApplyDamage(swordDamage, this);
            if (victim == null || victim.isKinematic) continue;
            float speed = Mathf.Sqrt(2f * Physics.gravity.magnitude * height * knockupHeights);
            CreatureImpulse.Apply(victim, Vector3.up * Mathf.Max(0f, speed - victim.linearVelocity.y), 0.9f);
        }
    }

    private void ShowImpact(Vector3 point, Vector3 normal)
    {
        var effect = new GameObject("Greatsword impact");
        effect.transform.SetPositionAndRotation(point + normal * 0.055f, Quaternion.FromToRotation(Vector3.up, normal));
        var impactRing = effect.AddComponent<LineRenderer>();
        impactRing.useWorldSpace = false;
        impactRing.loop = true;
        if (blade != null) impactRing.sharedMaterial = blade.GetComponent<Renderer>().sharedMaterial;
        impactRing.widthMultiplier = 0.13f;
        impactRing.positionCount = 72;
        for (int i = 0; i < impactRing.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / impactRing.positionCount;
            impactRing.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * slamRadius);
        }
        Destroy(effect, 0.35f);
    }
}
