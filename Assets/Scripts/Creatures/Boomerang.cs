using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>One outbound retrieval contact followed by a collision-free return to the owner.</summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class Boomerang : Creature
{
    private enum Flight { Idle, Outbound, ArcingBack, Returning, Releasing }
    [SerializeField, Min(1f)] private float rangeMultiplier = 2f;
    [SerializeField, Min(0.05f)] private float sweepRadius = 0.35f;
    [SerializeField, Min(0.05f)] private float retrievalRadius = 0.85f;
    private Flight flight;
    private PlayerCreatureCarrier owner;
    private Rigidbody body;
    private Transform visual;
    private Vector3 start, control, end, previous, lastClearCargo;
    private Vector3 returnControl, returnBow;
    private float elapsed, duration, returnSpeed, pressedAt;
    private int button;
    private Creature cargo;
    private bool cargoSimulation;
    private readonly List<(Collider collider, bool enabled)> cargoColliders = new();
    private bool bodyKinematic, bodyGravity, bodyCollisions;
    private CollisionDetectionMode bodyMode;
    private Collider[] ownColliders;
    private bool[] ownEnabled;
    private bool InputBlocked => !Application.isFocused || RestSpotSystem.BlocksInteraction ||
        MeditationController.BlocksPlayerInput || ReflectionVisitor.ConsumedClick ||
        BoneAwakeningCutscene.Playing || BoneRide.Busy || Time.timeScale == 0f ||
        Carrier != null && Carrier.PlayerController != null &&
        Carrier.PlayerController.TryGetComponent<PlayerBugGrip>(out var grip) && grip.ConsumesInput;

    public bool InFlight => flight != Flight.Idle;
    public override bool CanBeCarried => !InFlight;
    public override bool CanBeConsumed => false;
    public override bool CanBeCaptured => !InFlight;
    public override bool Invulnerable => true;
    public override bool IsInPhysicalWorld => !InFlight;
    public override bool CanUseWhileCarried => true;
    public override string InteractionPrompt => IsCarried ? "Throw boomerang" : null;
    public override string CarriedPrompt => "[Hold LMB] Throw · [Tap RMB] Put down · [Hold RMB] Throw";

    protected override void InitializeCreature()
    {
        SetCreatureName("Boomerang");
        SetResolvedPolarity(CreaturePolarity.None);
        SetFoodAndDiet(FoodTag.Matter, FoodTag.None);
        ConfigureLifespan(300f, 0f);
        body = GetComponent<Rigidbody>();
        visual = transform.Find("Visual");
        ownColliders = GetComponentsInChildren<Collider>();
        ownEnabled = new bool[ownColliders.Length];
    }

    protected override void TickCreature(float dt)
    {
        if (visual != null)
            visual.Rotate(Vector3.up, InFlight ? 1600f * dt : button != 0 ? 300f * dt : 0f, Space.Self);
        if (!IsCarried || InFlight) { button = 0; return; }
        if (cargo != null) { button = 0; return; }
        if (InputBlocked)
        { button = 0; return; }
        var mouse = Mouse.current;
        if (mouse == null) { button = 0; return; }
        HandleButton(1, mouse.leftButton.wasPressedThisFrame, mouse.leftButton.wasReleasedThisFrame);
        if (button == 1 && !mouse.leftButton.isPressed && !mouse.leftButton.wasReleasedThisFrame) button = 0;
    }

    private void HandleButton(int which, bool pressed, bool released)
    {
        if (pressed && button == 0) { button = which; pressedAt = Time.time; }
        if (!released || button != which || !IsCarried) return;
        float held = Time.time - pressedAt;
        button = 0;
        if (which == 2 && held < Carrier.ThrowHoldThreshold) { Carrier.Drop(); return; }
        Launch(Mathf.InverseLerp(Carrier.ThrowHoldThreshold, Carrier.FullThrowHoldTime, held));
    }

    public override bool TryHandleCarriedRightMouse(PlayerCreatureCarrier carrier, bool pressed, bool held, bool released)
    {
        if (InputBlocked)
        { button = 0; return true; }
        HandleButton(2, pressed, released);
        if (button == 2 && !held && !released) button = 0;
        return true;
    }

    public void Launch(float charge)
    {
        if (!IsCarried || InFlight || cargo != null || Carrier.CarryCamera == null) return;
        owner = Carrier;
        var camera = owner.CarryCamera.transform;
        float height = owner.PlayerBody != null ? owner.PlayerBody.GetComponent<Collider>()?.bounds.size.y ?? 2f : 2f;
        float maxRange = owner.GetComponent<CreatureThrower>().LevelThrowRange(height * 0.85f) * rangeMultiplier;
        charge = Mathf.Clamp01(charge);
        start = camera.position + camera.forward * 0.6f;
        end = start + camera.forward * Mathf.Lerp(4f, maxRange, charge);
        // Aim at the visible item's position when it is inside this charge's reach.
        // Otherwise the sideways bow can pass an aimed item before reaching a distant endpoint.
        var aimHits = new RaycastHit[32];
        var physics = gameObject.scene.GetPhysicsScene();
        int aimCount = physics.Raycast(camera.position, camera.forward, aimHits,
            Vector3.Distance(start, end), ~0, QueryTriggerInteraction.Ignore);
        while (aimCount == aimHits.Length)
        {
            aimHits = new RaycastHit[aimHits.Length * 2];
            aimCount = physics.Raycast(camera.position, camera.forward, aimHits,
                Vector3.Distance(start, end), ~0, QueryTriggerInteraction.Ignore);
        }
        float aimDistance = float.MaxValue;
        RaycastHit aimed = default;
        for (int i = 0; i < aimCount; i++)
        {
            if (aimHits[i].transform.IsChildOf(transform) || aimHits[i].rigidbody == owner.PlayerBody ||
                aimHits[i].distance >= aimDistance) continue;
            aimed = aimHits[i];
            aimDistance = aimed.distance;
        }
        if (aimDistance < float.MaxValue)
        {
            var target = CreatureSpatialQuery.Owner(aimed.collider);
            if (owner.CanRetrieve(target)) end = target.AimPosition;
        }
        control = (start + end) * 0.5f + camera.right * Vector3.Distance(start, end) * 0.24f +
            camera.up * Vector3.Distance(start, end) * 0.12f;
        returnBow = (start + end) * 0.5f - camera.right * Vector3.Distance(start, end) * 0.48f +
            camera.up * Vector3.Distance(start, end) * 0.24f;
        duration = Mathf.Lerp(0.30f, 0.85f, charge);
        returnSpeed = Mathf.Max(20f, Vector3.Distance(start, end) / duration * 1.5f);
        owner.Release();
        bodyKinematic = body.isKinematic; bodyGravity = body.useGravity; bodyCollisions = body.detectCollisions;
        bodyMode = body.collisionDetectionMode;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true; body.detectCollisions = false;
        for (int i = 0; i < ownColliders.Length; i++) { ownEnabled[i] = ownColliders[i].enabled; ownColliders[i].enabled = false; }
        flight = Flight.Outbound; elapsed = 0;
        previous = camera.position;
        transform.position = start;
        Sweep(previous, start);
        previous = transform.position;
    }

    private void FixedUpdate()
    {
        if (!InFlight) return;
        if (owner == null || !owner.isActiveAndEnabled || !owner.gameObject.activeInHierarchy ||
            owner.gameObject.scene != gameObject.scene)
        { FinishWithoutOwner(); return; }
        if (flight == Flight.Outbound)
        {
            elapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Vector3 next = (1f - t) * (1f - t) * start + 2f * (1f - t) * t * control + t * t * end;
            if (!Sweep(previous, next)) transform.position = next;
            previous = transform.position;
            if (t >= 1f && flight == Flight.Outbound)
            {
                flight = Flight.ArcingBack;
                elapsed = 0f;
                returnControl = end + (end - control) * 0.5f;
            }
        }
        else if (flight == Flight.ArcingBack)
        {
            elapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float u = 1f - t;
            Vector3 target = owner.CatchPosition;
            Vector3 next = u * u * u * end + 3f * u * u * t * returnControl +
                3f * u * t * t * returnBow + t * t * t * target;
            if (!Sweep(previous, next)) transform.position = next;
            previous = transform.position;
            if (t >= 1f && flight == Flight.ArcingBack) CatchAtHand();
        }
        else if (flight == Flight.Returning)
        {
            Vector3 target = owner.CatchPosition;
            float catchSpeed = returnSpeed + (owner.PlayerBody != null ? owner.PlayerBody.linearVelocity.magnitude : 0f);
            transform.position = Vector3.MoveTowards(transform.position, target, catchSpeed * Time.fixedDeltaTime);
            if (transform.position == target)
            {
                CatchAtHand();
            }
        }
        if (cargo != null) cargo.transform.position = transform.position + Vector3.down * 0.45f;
        if (flight == Flight.Releasing && TryReleaseCargo())
        {
            if (IsCarried || PlaceLooseBlade()) flight = Flight.Idle;
        }
    }

    private void CatchAtHand()
    {
        RestoreBody();
        // Allow the carrier to adopt this component while cargo waits for clearance.
        flight = Flight.Idle;
        owner.TryCarryFromInteraction(this);
        if (!IsCarried)
        {
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            body.detectCollisions = false;
        }
        flight = Flight.Releasing;
    }

    private bool Sweep(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < 0.000001f) return false;
        var physics = gameObject.scene.GetPhysicsScene();
        var hits = new RaycastHit[32];
        int count = physics.SphereCast(from, sweepRadius, delta.normalized, hits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        while (count == hits.Length)
        { hits = new RaycastHit[hits.Length * 2]; count = physics.SphereCast(from, sweepRadius, delta.normalized, hits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore); }
        float nearest = float.MaxValue; RaycastHit contact = default;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform) ||
                owner.PlayerBody != null && hits[i].rigidbody == owner.PlayerBody || hits[i].distance >= nearest) continue;
            nearest = hits[i].distance; contact = hits[i];
        }
        // Keep terrain contact tight to the blade, while giving collectible targets a forgiving sweep.
        int retrievalCount = physics.SphereCast(from, Mathf.Max(sweepRadius, retrievalRadius),
            delta.normalized, hits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        while (retrievalCount == hits.Length)
        {
            hits = new RaycastHit[hits.Length * 2];
            retrievalCount = physics.SphereCast(from, Mathf.Max(sweepRadius, retrievalRadius),
                delta.normalized, hits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        }
        for (int i = 0; i < retrievalCount; i++)
        {
            var target = CreatureSpatialQuery.Owner(hits[i].collider);
            if (hits[i].distance >= nearest || !owner.CanRetrieve(target) ||
                BlockedFromPlayer(from, target.AimPosition, target)) continue;
            nearest = hits[i].distance;
            contact = hits[i];
        }
        if (nearest == float.MaxValue) return false;
        transform.position = from + delta.normalized * Mathf.Max(0f, nearest - 0.02f);
        var candidate = CreatureSpatialQuery.Owner(contact.collider);
        if (owner.CanRetrieve(candidate) && candidate.TryBeginCarry(owner, transform))
        {
            cargo = candidate;
            lastClearCargo = cargo.transform.position;
            cargoSimulation = cargo.SimulationEnabled;
            cargo.SetSimulationEnabled(false);
            AudioManager.PostEvent("Play_PickUp", gameObject);
            cargoColliders.Clear();
            foreach (var collider in cargo.GetComponentsInChildren<Collider>())
            { cargoColliders.Add((collider, collider.enabled)); collider.enabled = false; }
        }
        flight = Flight.Returning;
        return true;
    }

    private bool TryReleaseCargo()
    {
        if (cargo == null) return true;
        if (cargo.IsDead || cargo.IsConsumptionPending) { RestoreCargo(lastClearCargo); return true; }
        if (!CreatureBounds.TryMeasure(cargo.transform, out var bounds)) { RestoreCargo(lastClearCargo); return true; }
        Vector3 centerOffset = bounds.center - cargo.transform.position;
        var physics = gameObject.scene.GetPhysicsScene();
        var overlap = new Collider[1];
        Vector3 origin = owner.PlayerBody != null ? owner.PlayerBody.worldCenterOfMass : owner.transform.position;
        float bottom = owner.PlayerBody != null && owner.PlayerBody.TryGetComponent<Collider>(out var playerCollider)
            ? playerCollider.bounds.min.y : origin.y - 1f;
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + 0.6f;
        for (int i = 0; i < 24; i++)
        {
            float angle = i * Mathf.PI * 0.5f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (radius + i / 8 * 0.6f);
            Vector3 center = origin + offset + Vector3.up * (i % 8 >= 4 ? 0.4f : 0f);
            center.y = Mathf.Max(center.y, bottom + bounds.extents.y + 0.1f);
            if (physics.OverlapBox(center, bounds.extents + Vector3.one * 0.04f, overlap, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore) != 0 ||
                BlockedFromPlayer(origin, center)) continue;
            RestoreCargo(center - centerOffset);
            return true;
        }
        return false;
    }

    private bool BlockedFromPlayer(Vector3 from, Vector3 to, Creature target = null)
    {
        var hits = new RaycastHit[32];
        Vector3 delta = to - from;
        var physics = gameObject.scene.GetPhysicsScene();
        int count = physics.Raycast(from, delta.normalized, hits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return true;
        for (int i = 0; i < count; i++)
            if (hits[i].rigidbody != owner.PlayerBody && !hits[i].transform.IsChildOf(transform) &&
                (target == null || CreatureSpatialQuery.Owner(hits[i].collider) != target)) return true;
        return false;
    }

    private void RestoreCargo(Vector3 position)
    {
        if (cargo == null) return;
        cargo.transform.position = position;
        cargo.EndCarry(owner);
        foreach (var pair in cargoColliders) if (pair.collider != null) pair.collider.enabled = pair.enabled;
        cargo.SetSimulationEnabled(cargoSimulation);
        cargo = null;
        cargoColliders.Clear();
    }

    private void RestoreBody()
    {
        body.isKinematic = bodyKinematic; body.useGravity = bodyGravity; body.detectCollisions = bodyCollisions;
        body.collisionDetectionMode = bodyMode;
        for (int i = 0; i < ownColliders.Length; i++) if (ownColliders[i] != null) ownColliders[i].enabled = ownEnabled[i];
        if (!body.isKinematic) body.linearVelocity = Vector3.zero;
    }

    private bool PlaceLooseBlade()
    {
        Vector3 center = owner.PlayerBody != null ? owner.PlayerBody.worldCenterOfMass : owner.transform.position;
        var physics = gameObject.scene.GetPhysicsScene();
        var overlaps = new Collider[8];
        for (int i = 0; i < 16; i++)
        {
            float angle = i * Mathf.PI * 0.5f;
            Vector3 position = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (1.1f + i / 4 * 0.4f);
            int count = physics.OverlapSphere(position, sweepRadius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            bool blocked = count == overlaps.Length;
            for (int j = 0; j < count; j++)
                if (!overlaps[j].transform.IsChildOf(transform)) blocked = true;
            if (blocked || BlockedFromPlayer(center, position)) continue;
            body.position = position;
            transform.position = position;
            RestoreBody();
            return true;
        }
        return false;
    }

    private void FinishWithoutOwner()
    {
        RestoreCargo(lastClearCargo);
        flight = Flight.Idle;
        RestoreBody();
    }

    protected override void OnDisable()
    {
        if (InFlight) FinishWithoutOwner();
        base.OnDisable();
    }
}
