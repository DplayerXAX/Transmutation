using UnityEngine;

/// <summary>Portable surface crawler, moving handhold and parent of one replacement egg.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider), typeof(CeilingBatBugSurfaceMotor))]
public sealed class CeilingBatBug : Creature
{
    [SerializeField] private Transform[] legs;
    [SerializeField] private ProceduralWorld habitatWorld;
    private CeilingBatBugLife life;
    public event System.Action SupportInvalidated;
    public CeilingBatBugLife Life => life != null ? life : (life = GetComponent<CeilingBatBugLife>());
    public ProceduralWorld HabitatWorld => Life != null && Life.Habitat != null ? Life.Habitat.world : habitatWorld;
    public bool CanSupportPlayer => isActiveAndEnabled && !IsDead && SimulationEnabled && !IsCarried && !IsBubbleCaptured &&
        !(TryGetComponent<WebCapture>(out var web) && web.IsCaptured) && !CreatureThrower.IsFlying(this) && GetComponent<CeilingBatBugSurfaceMotor>().Attached;
    public override bool CanUseWhileCarried => false;
    public override string InteractionPrompt => CanSupportPlayer && PlayerBugGrip.InReach(this) ? "Hold to grip bug" : null;
    public override string CarriedPrompt => IsDead ? null : "[RMB] Drop";

    protected override void InitializeCreature()
    {
        SetCreatureName("Ceiling Bat Bug");
        SetFoodAndDiet(FoodTag.Meat, FoodTag.None);
        SetMovementPhysics(false, false);
    }

    protected override void Update()
    {
        if (HabitatWorld != null && !HabitatWorld.IsRegionReady(transform.position, 1.5f)) return;
        base.Update();
    }

    protected override void TickCreature(float deltaTime)
    {
        Life?.Advance(deltaTime);
        if (legs == null || IsCarried || IsBubbleCaptured) return;
        float phase = Time.time * 15f + GetInstanceID();
        for (int i = 0; i < legs.Length; i++)
            if (legs[i] != null)
                legs[i].localRotation = Quaternion.Euler(0f, (i < 3 ? -1f : 1f) * 18f +
                    Mathf.Sin(phase + i * 2.3f) * 17f, 0f);
    }

    protected override void OnDeath()
    {
        GetComponent<CeilingBatBugSurfaceMotor>().Detach();
        GetComponent<CeilingBatBugSurfaceMotor>().enabled = false;
        SetMovementPhysics(false, true);
        InvalidateSupport();
    }
    internal void InvalidateSupport() => SupportInvalidated?.Invoke();
    protected override void OnCarryStarted() { GetComponent<CeilingBatBugSurfaceMotor>().Detach(); InvalidateSupport(); }
    protected override void OnCaptureStarted() { GetComponent<CeilingBatBugSurfaceMotor>().Detach(); InvalidateSupport(); }
    protected override void OnDisable() { InvalidateSupport(); base.OnDisable(); }
    protected override void OnSimulationChanged()
    {
        if (SimulationEnabled) return;
        if (TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic) rb.linearVelocity = Vector3.zero;
        InvalidateSupport();
    }
}
