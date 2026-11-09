using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(AsuraMotor))]
public sealed class RollingAsura : Creature
{
    [SerializeField] private float speed = 5f;
    [SerializeField] private float fuseSeconds = 0.6f;
    [SerializeField] private float triggerGap = 0.65f;
    private readonly List<Creature> nearby = new List<Creature>();
    private readonly List<int> indices = new List<int>();
    private RaycastHit[] hits = new RaycastHit[16];
    private AsuraMotor motor;
    private Creature target;
    private float nextSearch, fuse;
    private bool spent, detonated;
    private Vector3 lastPosition;
    public bool Armed { get; private set; }
    public bool Spent => spent;
    public float RollDegrees { get; private set; }
    protected override void InitializeCreature()
    { SetCreatureName("Rolling Asura"); SetFoodAndDiet(FoodTag.Meat, FoodTag.None); motor = GetComponent<AsuraMotor>(); lastPosition = transform.position; }
    protected override void TickCreature(float dt)
    {
        float distance = Vector3.ProjectOnPlane(transform.position - lastPosition, Vector3.up).magnitude;
        lastPosition = transform.position;
        RollDegrees += Mathf.Min(distance, 0.3f) / 0.45f * Mathf.Rad2Deg;
        if (spent || IsCarried || IsBubbleCaptured) { motor.Stop(); return; }
        if (Armed)
        {
            motor.Stop(); fuse -= dt;
            if (fuse <= 0f && !detonated)
            {
                detonated = true; Armed = false;
                ExplosionResolver.Request(gameObject.scene, AimPosition, 2.8f, 45f, 9f, this);
                Die();
            }
            return;
        }
        if (target != null && !Valid(target)) target = null;
        if (target == null && Time.time >= nextSearch)
        {
            nextSearch = Time.time + 0.25f;
            CreatureSpatialQuery.Nearby(gameObject.scene, AimPosition, 20f, nearby, indices);
            float best = float.PositiveInfinity;
            foreach (var candidate in nearby)
            {
                if (!Valid(candidate)) continue;
                float d = (candidate.AimPosition - AimPosition).sqrMagnitude;
                if (d < best && CreatureSpatialQuery.Visible(this, candidate, AimPosition, ref hits)) { best = d; target = candidate; }
            }
        }
        if (target == null) { motor.Stop(); return; }
        Vector3 delta = target.AimPosition - AimPosition;
        motor.SetIntent(delta.normalized * speed, delta);
        if (CreatureSpatialQuery.TryGetSolid(target, out var solid) && CreatureSpatialQuery.TryGetSolid(this, out var own))
        {
            Vector3 point = solid.ClosestPoint(own.bounds.center);
            if ((point - own.ClosestPoint(point)).sqrMagnitude <= triggerGap * triggerGap && CreatureSpatialQuery.Visible(this, target, AimPosition, ref hits))
            { Armed = true; fuse = fuseSeconds; motor.Stop(); }
        }
    }
    private bool Valid(Creature other) => other != null && other != this && other.gameObject.scene == gameObject.scene &&
        other.isActiveAndEnabled && !other.IsDead && !other.IsConsumptionPending && other.CanBeAITargeted &&
        other.Polarity != Polarity && (other.AimPosition - AimPosition).sqrMagnitude < 625f && CreatureSpatialQuery.TryGetSolid(other, out _);
    private void Disarm() { Armed = false; spent = true; motor.Stop(); }
    protected override void OnCarryStarted() => Disarm();
    protected override void OnCaptureStarted() => Disarm();
    protected override void OnSimulationChanged() { if (!SimulationEnabled) motor.Stop(); }
    protected override void OnDeath() { Armed = false; motor.Stop(); }
}
