using System.Collections.Generic;
using UnityEngine;

/// <summary>Seeks light only when it stands out against the local background; glowing players are ordinary sources.</summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider), typeof(WebCapture))]
public sealed class LightMoth : Creature
{
    public enum Activity { Wandering, Attracted, Dazzled }
    [SerializeField, Min(0.1f)] private float detectionRadius = 18f;
    [SerializeField, Min(0f)] private float minimumBrightness = 0.45f;
    [SerializeField, Min(1f)] private float requiredContrast = 3f;
    [SerializeField, Min(0.01f)] private float backgroundFloor = 0.1f;
    [SerializeField, Min(0.1f)] private float flightSpeed = 2.6f;
    [SerializeField, Min(0.1f)] private float orbitRadius = 1.1f;
    private readonly List<LuminosityReading> readings = new List<LuminosityReading>();
    private Rigidbody body;
    private Vector3 home, destination;
    private float searchAt, wanderAt;
    private ICreatureStats target;
    public Activity State { get; private set; }
    public Transform TargetRoot => TargetExists ? target.StatRoot : null;
    public float BackgroundBrightness { get; private set; }
    public float TargetContrast { get; private set; }
    private bool TargetExists => target is Object obj && obj != null && target.IsAvailableForPerception;

    protected override void InitializeCreature()
    {
        SetCreatureName("Light Moth");
        SetFoodAndDiet(FoodTag.Meat, FoodTag.None);
        SetLuminosity(0.5f);
        body = GetComponent<Rigidbody>();
        body.useGravity = false; body.mass = 0.08f;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        GetComponent<SphereCollider>().radius = 0.16f;
        home = destination = transform.position;
        Transform glow = transform.Find("Body");
        if (glow != null && !glow.TryGetComponent<GlowBeeLight>(out _))
            glow.gameObject.AddComponent<GlowBeeLight>().Configure(glow.GetComponent<Renderer>(), new Color(0.65f, 0.8f, 1f), 0.6f);
    }

    public void Sense()
    {
        target = null; TargetContrast = 0f;
        if (!SimulationEnabled || !IsAvailableForPerception || FlashBlindness.IsBlinded(this))
        { State = Activity.Dazzled; return; }
        BackgroundBrightness = AmbientLuminosity.Sample(this);
        LuminosityQuery.Nearby(gameObject.scene, AimPosition, detectionRadius, 0f, transform,
            LuminosityQuery.DefaultObstructionMask, readings);
        float brightest = 0f, secondBrightest = 0f;
        foreach (var reading in readings)
        {
            if (reading.PerceivedBrightness >= brightest) { secondBrightest = brightest; brightest = reading.PerceivedBrightness; }
            else secondBrightest = Mathf.Max(secondBrightest, reading.PerceivedBrightness);
        }
        float best = 0f;
        foreach (var reading in readings)
        {
            float contrast = reading.PerceivedBrightness / Mathf.Max(backgroundFloor,
                BackgroundBrightness + (reading.PerceivedBrightness >= brightest ? secondBrightest : brightest));
            if (reading.PerceivedBrightness < minimumBrightness || contrast < requiredContrast || reading.PerceivedBrightness <= best) continue;
            target = reading.Source;
            best = reading.PerceivedBrightness;
            TargetContrast = contrast;
        }
        State = TargetExists ? Activity.Attracted : Activity.Wandering;
    }

    protected override void TickCreature(float deltaTime)
    {
        if (IsCarried || IsBubbleCaptured || (TryGetComponent<WebCapture>(out var web) && web.IsCaptured)) return;
        if (FlashBlindness.IsBlinded(this)) { target = null; State = Activity.Dazzled; destination = body.position; return; }
        if (Time.time >= searchAt) { searchAt = Time.time + 0.2f; Sense(); }
        if (TargetExists)
        {
            float angle = Time.time * 2f + GetInstanceID() % 100;
            destination = target.EmissionPosition + new Vector3(Mathf.Cos(angle) * orbitRadius,
                0.25f + Mathf.Sin(angle * 1.7f) * 0.2f, Mathf.Sin(angle) * orbitRadius);
        }
        else if (Time.time >= wanderAt)
        {
            State = Activity.Wandering;
            wanderAt = Time.time + 2f;
            Vector2 offset = Random.insideUnitCircle * 2f;
            destination = home + new Vector3(offset.x, Random.Range(-0.2f, 0.2f), offset.y);
        }
        AnimateWing("Left wing", -1f); AnimateWing("Right wing", 1f);
    }

    private void AnimateWing(string name, float side)
    {
        Transform wing = transform.Find(name);
        if (wing != null) wing.localRotation = Quaternion.Euler(0f, 0f, side * (15f + Mathf.Sin(Time.time * 38f) * 45f));
    }

    private void FixedUpdate()
    {
        if (!SimulationEnabled || IsCarried || IsBubbleCaptured || body.isKinematic ||
            (TryGetComponent<WebCapture>(out var web) && web.IsCaptured)) return;
        Vector3 desired = FlashBlindness.IsBlinded(this) ? Vector3.zero : Vector3.ClampMagnitude((destination - body.position) * 2f, flightSpeed);
        SetCreatureVelocity(Vector3.MoveTowards(body.linearVelocity, desired, Time.fixedDeltaTime * 8f));
        if (desired.sqrMagnitude > 0.05f) body.MoveRotation(Quaternion.Slerp(body.rotation, Quaternion.LookRotation(desired), Time.fixedDeltaTime * 5f));
    }
}
