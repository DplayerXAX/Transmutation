using UnityEngine;
using UnityEngine.InputSystem;

public enum FishingState { Idle, Waiting, Reeling, Landing, Charging, Casting }
public sealed class FishingPoleCreature : Creature
{
    [SerializeField] float castRange = 8, attractionRadius = 4;
    [SerializeField] Vector2 biteDelay = new Vector2(1, 3), directionInterval = new Vector2(1.5f, 2.5f);
    [SerializeField] float drainRate = 1, failureGrace = 2, telegraphTime = .25f;
    [SerializeField] float minimumCastRange = 3, fullCastHoldTime = 1.2f, landingDuration = .24f;
    public FishingState State { get; private set; }
    public SeaAngelCreature TargetFish { get; private set; }
    public FishingStruggle Struggle { get; } = new FishingStruggle();
    public float LastPullAt { get; private set; } = -10;
    public Vector3 LurePosition { get; private set; }
    public float CastCharge01 => Mathf.Clamp01(chargeTime / Mathf.Max(.01f, fullCastHoldTime));
    public float CastDistance(float charge01) => Mathf.Lerp(Mathf.Min(minimumCastRange, castRange), castRange, Mathf.Clamp01(charge01));
    public override bool HeldInView => true;
    public override bool CanUseWhileCarried => true;
    public override string CarriedPrompt => State == FishingState.Idle ? "Cast" : "Pull";
    SmoothFirstPersonController lockedPlayer;
    float wait, landing, chargeTime, flightTime, flightDuration, reelStartDistance, swimPhase;
    int chargeStartedFrame;
    Vector3 landingStart, castStart, castDestination, reelForward;
    Vector2 swimmingPull;
    Camera View => Carrier != null ? Carrier.CarryCamera : null;
    bool Incompatible => MeditationController.BlocksPlayerInput || RestSpotSystem.BlocksInteraction || BoneRide.Busy ||
        (Carrier != null && Carrier.PlayerController != null && (Carrier.PlayerController.Climbing || Carrier.PlayerController.Sliding || Carrier.PlayerController.WallRunning || Carrier.PlayerController.Restricted || Carrier.PlayerController.IsFishingBlockedExternally));
    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (!IsCarried || IsDead || Incompatible || Time.timeScale <= 0 || View == null) return;
        if (State == FishingState.Idle) BeginCastCharge();
        else if (State == FishingState.Waiting) CancelFishing();
        else if (State == FishingState.Reeling)
        {
            var k = Keyboard.current;
            Vector2Int input = k == null ? Vector2Int.zero : new Vector2Int((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
            Pull(input);
        }
    }
    public bool Pull(Vector2Int direction)
    {
        if (State != FishingState.Reeling || Time.timeScale <= 0) return false;
        bool good = Struggle.Pull(direction); if (good) LastPullAt = Time.time;
        if (Struggle.Won) { State = FishingState.Landing; landing = 0; landingStart = TargetFish.transform.position; Unlock(); }
        return good;
    }
    public void BeginCastCharge()
    {
        if (!IsCarried || State != FishingState.Idle || Incompatible || View == null) return;
        State = FishingState.Charging; chargeTime = 0; chargeStartedFrame = Time.frameCount;
    }
    public void UpdateCastCharge(bool held, bool released, float dt)
    {
        if (State != FishingState.Charging || dt <= 0) return;
        if (held) chargeTime = Mathf.Min(fullCastHoldTime, chargeTime + dt);
        if (released) ReleaseCast();
    }
    public void ReleaseCast()
    {
        if (State != FishingState.Charging || !IsCarried || View == null || Incompatible) return;
        Cast(CastCharge01);
    }
    void Cast(float charge)
    {
        Vector3 origin = View.transform.position, direction = View.transform.forward;
        float distance = CastDistance(charge);
        foreach (var h in Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.GetComponentInParent<Creature>() != null || h.collider.transform.IsChildOf(Carrier.transform)) continue;
            distance = Mathf.Min(distance, Mathf.Max(.1f, h.distance - .25f));
        }
        castDestination = origin + direction * distance;
        castStart = transform.TransformPoint(new Vector3(0, .03f, .94f));
        LurePosition = castStart; State = FishingState.Casting; flightTime = 0;
        flightDuration = Mathf.Lerp(.22f, .45f, charge);
        wait = Random.Range(biteDelay.x, biteDelay.y);
    }
    protected override void TickCreature(float dt)
    {
        if (State == FishingState.Idle) return;
        if (!IsCarried || Incompatible || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)) { CancelFishing(); return; }
        if (dt <= 0) return;
        if (State == FishingState.Charging)
        {
            var mouse = Mouse.current;
            if (Time.frameCount > chargeStartedFrame)
                UpdateCastCharge(mouse != null && mouse.leftButton.isPressed, mouse == null || !mouse.leftButton.isPressed, dt);
            return;
        }
        if (State == FishingState.Casting)
        {
            flightTime += dt;
            float t = Mathf.Clamp01(flightTime / flightDuration);
            LurePosition = Vector3.Lerp(castStart, castDestination, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * .3f;
            if (t >= 1) State = FishingState.Waiting;
            return;
        }
        if (TargetFish != null && (!TargetFish.isActiveAndEnabled || TargetFish.IsDead || !TargetFish.SimulationEnabled || TargetFish.ReservedBy != this)) { CancelFishing(); return; }
        if (State == FishingState.Waiting)
        {
            if (TargetFish == null)
            {
                float best = attractionRadius * attractionRadius;
                foreach (var fish in FindObjectsByType<SeaAngelCreature>(FindObjectsSortMode.None))
                {
                    if (fish.gameObject.scene != gameObject.scene || fish.Materialized || fish.ReservedBy != null || fish.IsDead) continue;
                    float d = (fish.transform.position - LurePosition).sqrMagnitude;
                    if (d < best) { best = d; TargetFish = fish; }
                }
                if (TargetFish != null && !TargetFish.TryReserve(this)) TargetFish = null;
            }
            if (TargetFish == null) return;
            if (wait > 0)
            {
                wait -= dt; TargetFish.NoticeBait(LurePosition, dt); return;
            }
            if (TargetFish.ApproachBait(LurePosition, dt))
            {
                Struggle.Begin(TargetFish.RequiredPull, drainRate, failureGrace, directionInterval, telegraphTime);
                State = FishingState.Reeling; lockedPlayer = Carrier.PlayerController;
                reelStartDistance = Vector3.Distance(View.transform.position, TargetFish.transform.position);
                reelForward = (TargetFish.transform.position - View.transform.position).normalized;
                swimmingPull = -((Vector2)Struggle.CounterDirection).normalized; swimPhase = 0;
                if (lockedPlayer != null) lockedPlayer.FishingInputOwner = this;
            }
        }
        else if (State == FishingState.Reeling)
        {
            if (TargetFish == null) { CancelFishing(); return; }
            Struggle.Tick(dt);
            swimPhase += dt;
            swimmingPull = Vector2.MoveTowards(swimmingPull, -((Vector2)Struggle.CounterDirection).normalized, dt * 1.3f);
            float progress = Struggle.Progress / Struggle.Required;
            float retrieved = Mathf.InverseLerp(.25f, 1, progress);
            float depth = Mathf.Lerp(reelStartDistance, Mathf.Min(2.6f, reelStartDistance), retrieved);
            depth += Mathf.InverseLerp(.25f, 0, progress) * .7f;
            Vector3 center = View.transform.position + reelForward * depth;
            Vector2 sideways = new Vector2(-swimmingPull.y, swimmingPull.x);
            Vector2 curve = swimmingPull * .85f + sideways * Mathf.Sin(swimPhase * 2.1f) * .4f;
            Vector3 goal = center + View.transform.right * curve.x + View.transform.up * curve.y + reelForward * Mathf.Sin(swimPhase * 1.4f) * .2f;
            TargetFish.SwimToward(goal, dt, 1.8f);
            // Successful counter-pulls shorten the tether even while the fish swims against it.
            Vector3 fromPlayer = TargetFish.transform.position - View.transform.position;
            float tetherLength = depth + 1f;
            if (fromPlayer.magnitude > tetherLength)
                TargetFish.transform.position = Vector3.MoveTowards(TargetFish.transform.position, View.transform.position + fromPlayer.normalized * tetherLength, dt * 3f);
            LurePosition = TargetFish.MouthPosition;
            if (Struggle.Lost) CancelFishing();
        }
        else if (State == FishingState.Landing)
        {
            if (TargetFish == null || !TryLandingPoint(out var point)) { CancelFishing(); return; }
            landing += dt / Mathf.Max(.05f, landingDuration);
            float t = Mathf.Clamp01(landing);
            TargetFish.ReelTo(Vector3.Lerp(landingStart, point, 1 - (1 - t) * (1 - t)), dt);
            LurePosition = TargetFish.MouthPosition;
            if (landing >= 1) { TargetFish.Materialize(point); TargetFish = null; CancelFishing(); }
        }
    }
    bool TryLandingPoint(out Vector3 point)
    {
        var cam = View.transform; float radius = TargetFish.BodyLength * .55f;
        foreach (float d in new[] { 2f, 1.5f, 2.5f, 3f })
        {
            point = cam.position + cam.forward * d;
            bool clear = true;
            foreach (var c in Physics.OverlapSphere(point, radius, ~0, QueryTriggerInteraction.Ignore))
                if (c.GetComponentInParent<Creature>() != this && c.GetComponentInParent<Creature>() != TargetFish) { clear = false; break; }
            if (!clear) continue;
            Vector3 path = point - cam.position;
            foreach (var hit in Physics.RaycastAll(cam.position, path.normalized, path.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                var creature = hit.collider.GetComponentInParent<Creature>();
                if (creature == this || creature == TargetFish || hit.collider.transform.IsChildOf(Carrier.transform)) continue;
                clear = false; break;
            }
            if (clear) return true;
        }
        point = default; return false;
    }
    public override bool TryHandleCarriedRightMouse(PlayerCreatureCarrier carrier, bool pressed, bool held, bool released) { if (pressed) CancelFishing(); return false; }
    public void CancelFishing()
    {
        if (TargetFish != null) TargetFish.ReleaseReservation(this);
        TargetFish = null; State = FishingState.Idle; Unlock();
    }
    void Unlock() { if (lockedPlayer != null && lockedPlayer.FishingInputOwner == this) lockedPlayer.FishingInputOwner = null; lockedPlayer = null; }
    protected override void OnDisable() { CancelFishing(); base.OnDisable(); }
    protected override void OnDeath() { CancelFishing(); base.OnDeath(); }
    protected override void OnSimulationChanged() { if (!SimulationEnabled) CancelFishing(); base.OnSimulationChanged(); }
}
