using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public enum FishingState { Idle, Waiting, Reeling, Landing, Charging, Casting }
public sealed class FishingPoleCreature : Creature
{
    [SerializeField, HideInInspector] int fishingFeelVersion;
    [SerializeField] float castRange = 8, attractionRadius = 4;
    [SerializeField] Vector2 biteDelay = new Vector2(1, 3);
    [FormerlySerializedAs("directionInterval"), SerializeField] Vector2 swimDirectionInterval = new Vector2(1.5f, 2.5f);
    [SerializeField] float drainRate = .35f, failureGrace = 2;
    [SerializeField] float pullGain = 1.5f, loadBonus = .2f, loadSmoothing = .15f, fullLoadSpeed = 2;
    [SerializeField] float hookedSpeed = 3, hookedAcceleration = 6, hookedTurnSpeed = 220, lateralReach = 1;
    [SerializeField] float turnAnticipation = .2f, tetherSpeed = 4, tetherResponse = 18, virtualSteeringReach = .45f;
    [SerializeField] float minimumCastRange = 3, fullCastHoldTime = 1.2f;
    [SerializeField] float maximumRetrievalSpeed = 8, maximumRetrievalAcceleration = 80, retrievalResponse = 16;
    [SerializeField] float arrivalDistance = .2f, arrivalSpeed = .6f, stallTimeout = 2;
    [SerializeField] float minimumFightDistance = 4.5f, remainingFightDistance = .82f;
    [SerializeField] float catchWindup = .1f, catchArcDuration = .8f, catchLiftSpeed = 5.5f;
    public FishingState State { get; private set; }
    public SeaAngelCreature TargetFish { get; private set; }
    public FishingStruggle Struggle { get; } = new FishingStruggle();
    public float LastPullAt { get; private set; } = -10;
    public float LastRejectedPullAt { get; private set; } = -10;
    public float LastPullLoad { get; private set; }
    public event System.Action<bool> PullResolved;
    public event System.Action<SeaAngelCreature> CatchSucceeded;
    public float CatchCommittedAt { get; private set; } = -10;
    public bool SteeringAccepted => State == FishingState.Reeling && Struggle.Accepts(SteeringInput);
    public float NormalizedLoad => load.Normalized;
    public float TetherLength { get; private set; }
    public float LineTautness => TargetFish != null && View != null && State == FishingState.Reeling
        ? Mathf.InverseLerp(TetherLength - .45f, TetherLength, Vector3.Distance(View.transform.position, TargetFish.transform.position)) : 0;
    public Vector3 LurePosition { get; private set; }
    public Vector2Int SteeringInput { get; private set; }
    public Vector2Int RequiredDirection => Struggle.CounterDirection;
    public Vector3 CatchPoint { get; private set; }
    public float CastCharge01 => Mathf.Clamp01(chargeTime / Mathf.Max(.01f, fullCastHoldTime));
    public float CastDistance(float charge01) => Mathf.Lerp(Mathf.Min(minimumCastRange, castRange), castRange, Mathf.Clamp01(charge01));
    public override bool HeldInView => true;
    public override bool CanUseWhileCarried => true;
    public override string CarriedPrompt => State == FishingState.Idle ? "Cast" : "Pull";
    SmoothFirstPersonController lockedPlayer;
    float wait, chargeTime, flightTime, flightDuration, reelStartDistance, swimPhase, swimDecisionTimer, stalledTime, bestCatchDistance;
    int chargeStartedFrame;
    Vector3 castStart, castDestination, reelForward, previousCatchPoint;
    bool hasCatchPoint;
    Vector2 swimmingPull, desiredSwimmingPull;
    readonly FishingLoad load = new FishingLoad();
    Vector3 virtualSteering;
    float inwardSpeed, anticipationRemaining;
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
            Pull(ReadSteering());
        }
    }
    public bool Pull(Vector2Int direction)
    {
        if (State != FishingState.Reeling || Time.timeScale <= 0) return false;
        RefreshCounterDirection(); SteeringInput = direction;
        bool good = Struggle.Pull(direction, NormalizedLoad, pullGain, loadBonus);
        if (good) { LastPullAt = Time.time; LastPullLoad = NormalizedLoad; UpdateTetherLength(); }
        else LastRejectedPullAt = Time.time;
        PullResolved?.Invoke(good);
        if (Struggle.Won)
        {
            State = FishingState.Landing; SteeringInput = Vector2Int.zero;
            stalledTime = 0; bestCatchDistance = float.PositiveInfinity; hasCatchPoint = false;
            if (!TargetFish.BeginCaughtRetrieval(this)) CancelFishing();
            else { CatchCommittedAt = Time.time; CatchSucceeded?.Invoke(TargetFish); }
        }
        return good;
    }
    static Vector2Int ReadSteering()
    {
        var k = Keyboard.current;
        return k == null ? Vector2Int.zero : new Vector2Int((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0),
            (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
    }
    public void RefreshCounterDirection()
    {
        if (State != FishingState.Reeling || TargetFish == null || View == null) return;
        Struggle.SetCounterDirection(FishingCounterPull.Calculate(Carrier.transform.position, TargetFish.transform.position,
            View.transform.right, View.transform.up, Struggle.CounterDirection));
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
        if (TargetFish != null && (!TargetFish.isActiveAndEnabled || TargetFish.ReservedBy != this ||
            (State != FishingState.Landing && (TargetFish.IsDead || !TargetFish.SimulationEnabled)))) { CancelFishing(); return; }
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
                Struggle.Begin(TargetFish.RequiredPull, drainRate, failureGrace);
                State = FishingState.Reeling; lockedPlayer = Carrier.PlayerController;
                reelStartDistance = Vector3.Distance(View.transform.position, TargetFish.transform.position);
                reelForward = (TargetFish.transform.position - View.transform.position).normalized;
                swimmingPull = desiredSwimmingPull = Random.insideUnitCircle.normalized; swimPhase = swimDecisionTimer = 0;
                load.Reset(); virtualSteering = Vector3.zero; inwardSpeed = anticipationRemaining = 0;
                LastPullAt = LastRejectedPullAt = -10; LastPullLoad = 0; UpdateTetherLength();
                RefreshCounterDirection();
                if (lockedPlayer != null) lockedPlayer.FishingInputOwner = this;
            }
        }
        else if (State == FishingState.Reeling)
        {
            if (TargetFish == null) { CancelFishing(); return; }
            SteeringInput = ReadSteering(); RefreshCounterDirection();
            Struggle.Tick(dt);
            swimPhase += dt;
            swimDecisionTimer -= dt;
            if (swimDecisionTimer <= 0)
            {
                desiredSwimmingPull = Random.insideUnitCircle.normalized;
                swimDecisionTimer = Random.Range(swimDirectionInterval.x, swimDirectionInterval.y);
                anticipationRemaining = turnAnticipation;
            }
            anticipationRemaining = Mathf.Max(0, anticipationRemaining - dt);
            if (anticipationRemaining <= 0) swimmingPull = Vector2.MoveTowards(swimmingPull, desiredSwimmingPull, dt * 3);
            UpdateTetherLength();
            // Escape destinations remain outside the shortened tether, so the fish
            // swims against it instead of calmly following a moving orbit center.
            Vector3 center = View.transform.position + reelForward * (TetherLength + .8f);
            Vector2 sideways = new Vector2(-swimmingPull.y, swimmingPull.x);
            Vector2 curve = swimmingPull * lateralReach + sideways * Mathf.Sin(swimPhase * 2.1f) * .4f;
            Vector3 goal = center + View.transform.right * curve.x + View.transform.up * curve.y + reelForward * Mathf.Sin(swimPhase * 1.4f) * .2f;
            Vector3 beforeSwim = TargetFish.transform.position;
            Vector3 anticipated = View.transform.right * desiredSwimmingPull.x + View.transform.up * desiredSwimmingPull.y;
            TargetFish.SetStruggleAnticipation(anticipated, anticipationRemaining > 0 ? 1 : 0, dt);
            TargetFish.SwimToward(goal, dt, hookedSpeed, .05f, hookedAcceleration, hookedTurnSpeed);
            Vector3 naturalTravel = TargetFish.transform.position - beforeSwim;
            virtualSteering = Vector3.Lerp(virtualSteering,
                new Vector3(SteeringInput.x, SteeringInput.y, 0) * virtualSteeringReach,
                1 - Mathf.Exp(-12 * dt));
            Vector3 restTip = View.transform.position + View.transform.forward * .9f + View.transform.right * .35f - View.transform.up * .2f;
            load.Sample(Carrier.transform.position, restTip, View.transform.TransformDirection(virtualSteering), beforeSwim, naturalTravel,
                View.transform.forward, dt, loadSmoothing, fullLoadSpeed);
            Vector3 fromPlayer = TargetFish.transform.position - View.transform.position;
            float excess = Mathf.Max(0, fromPlayer.magnitude - TetherLength);
            float desiredInwardSpeed = Mathf.Min(tetherSpeed, excess * 20);
            inwardSpeed = Mathf.Lerp(inwardSpeed, desiredInwardSpeed, 1 - Mathf.Exp(-tetherResponse * dt));
            TargetFish.transform.position -= fromPlayer.normalized * Mathf.Min(excess, inwardSpeed * dt);
            load.CommitFishPosition(TargetFish.transform.position);
            LurePosition = TargetFish.MouthPosition;
            if (Struggle.Lost) CancelFishing();
        }
        else if (State == FishingState.Landing)
        {
            if (TargetFish == null) { CancelFishing(); return; }
            LurePosition = TargetFish.MouthPosition;
        }
    }
    void UpdateTetherLength()
    {
        float progress = Struggle.Progress / Struggle.Required;
        float start = Mathf.Max(minimumFightDistance, reelStartDistance);
        TetherLength = Mathf.Lerp(start, Mathf.Max(minimumFightDistance, start * remainingFightDistance), Mathf.InverseLerp(.25f, 1, progress))
            + Mathf.InverseLerp(.25f, 0, progress) * .7f;
    }
    void FixedUpdate()
    {
        if (State != FishingState.Landing) return;
        if (!IsCarried || IsDead || !SimulationEnabled || Incompatible || View == null || TargetFish == null ||
            !TargetFish.isActiveAndEnabled || !TargetFish.CaughtRetrieval || TargetFish.IsCarried || TargetFish.IsBubbleCaptured ||
            (TargetFish.TryGetComponent<WebCapture>(out var web) && web.IsCaptured)) { CancelFishing(); return; }
        var body = TargetFish.GetComponent<Rigidbody>();
        if (body == null || body.isKinematic || !TryCatchPoint(out var point)) { CancelFishing(); return; }
        float catchAge = Time.time - CatchCommittedAt;
        // Let the exhausted body and the pole's windup register before the sweep.
        if (catchAge < catchWindup) return;
        float dt = Time.fixedDeltaTime;
        Vector3 targetVelocity = hasCatchPoint ? Vector3.ClampMagnitude((point - previousCatchPoint) / dt, maximumRetrievalSpeed) : Vector3.zero;
        previousCatchPoint = CatchPoint = point; hasCatchPoint = true;
        Vector3 error = point - body.position;
        if (error.magnitude <= arrivalDistance && (body.linearVelocity - targetVelocity).magnitude <= arrivalSpeed) { CancelFishing(); return; }
        if (error.magnitude < bestCatchDistance - .02f) { bestCatchDistance = error.magnitude; stalledTime = 0; }
        else stalledTime += dt;
        if (stalledTime >= stallTimeout) { CancelFishing(); return; }
        float arcPhase = (catchAge - catchWindup) / Mathf.Max(.01f, catchArcDuration);
        float lift = arcPhase < 1 ? catchLiftSpeed * Mathf.Cos(Mathf.PI * arcPhase) : 0;
        Vector3 approachVelocity = Vector3.ClampMagnitude(targetVelocity + error * 8, maximumRetrievalSpeed);
        Vector3 desiredVelocity = Vector3.ClampMagnitude(approachVelocity + Vector3.up * lift, maximumRetrievalSpeed);
        Vector3 acceleration = (desiredVelocity - body.linearVelocity) * retrievalResponse;
        if (body.useGravity) acceleration -= Physics.gravity;
        body.AddForce(Vector3.ClampMagnitude(acceleration, maximumRetrievalAcceleration), ForceMode.Acceleration);
        LurePosition = TargetFish.MouthPosition;
    }
    bool TryCatchPoint(out Vector3 point)
    {
        float radius = TargetFish.BodyLength * .55f;
        float playerRadius = Carrier.TryGetComponent<CapsuleCollider>(out var capsule) ? capsule.radius * Mathf.Max(Carrier.transform.lossyScale.x, Carrier.transform.lossyScale.z) : .4f;
        float clearance = radius + playerRadius + .15f;
        Vector3 forward = Vector3.ProjectOnPlane(View.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f) forward = Carrier.transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 center = View.transform.position - Vector3.up * .35f;
        for (int candidate = 0; candidate < 3; candidate++)
        {
            point = center + (candidate < 2 ? right * (candidate == 0 ? clearance : -clearance) + forward * .55f : forward * (clearance + .55f));
            bool clear = true;
            foreach (var c in Physics.OverlapSphere(point, radius, ~0, QueryTriggerInteraction.Ignore))
                if (c.GetComponentInParent<Creature>() != this && c.GetComponentInParent<Creature>() != TargetFish) { clear = false; break; }
            if (clear) return true;
        }
        point = default; return false;
    }
    public override bool TryHandleCarriedRightMouse(PlayerCreatureCarrier carrier, bool pressed, bool held, bool released) { if (pressed) CancelFishing(); return false; }
    public void CancelFishing()
    {
        if (TargetFish != null) { TargetFish.EndCaughtRetrieval(); TargetFish.ReleaseReservation(this); }
        TargetFish = null; State = FishingState.Idle; SteeringInput = Vector2Int.zero; hasCatchPoint = false;
        load.Reset(); virtualSteering = Vector3.zero; inwardSpeed = 0; TetherLength = 0; Unlock();
    }
    void Unlock() { if (lockedPlayer != null && lockedPlayer.FishingInputOwner == this) lockedPlayer.FishingInputOwner = null; lockedPlayer = null; }
    protected override void OnDisable() { CancelFishing(); base.OnDisable(); }
    protected override void OnDeath() { CancelFishing(); base.OnDeath(); }
    protected override void OnSimulationChanged() { if (!SimulationEnabled) CancelFishing(); base.OnSimulationChanged(); }
}
