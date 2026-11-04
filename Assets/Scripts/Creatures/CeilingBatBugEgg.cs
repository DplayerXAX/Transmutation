using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class CeilingBatBugEgg : Creature
{
    private enum Outcome { Waiting, Hatching, Hatched, Broken }
    private enum WaitReason { None, Density, Clearance }
    [SerializeField] private CeilingBatBugHabitat habitat;
    [SerializeField] private Vector3 inheritedHome;
    [SerializeField] private float incubationRemaining = 15f, readyWait;
    [SerializeField] private Outcome outcome;
    [SerializeField] private Transform shell, brokenShell;
    [SerializeField] private Transform support;
    [SerializeField] private Vector3 localSupportPoint, localSupportNormal, surfaceNormal = Vector3.up;
    [SerializeField] private bool adhered;
    private WaitReason waitReason;
    private bool attemptedReady;
    private float nextRetry;
    private Rigidbody body;
    private Collider[] nearby = new Collider[64];
    private readonly RaycastHit[] surfaceHits = new RaycastHit[24];
    private readonly HashSet<CeilingBatBug> seen = new HashSet<CeilingBatBug>();
    private MaterialPropertyBlock properties;
    private Renderer look;
    public CeilingBatBugHabitat Habitat => habitat;
    public bool IsReady => outcome == Outcome.Waiting && !IsDead && incubationRemaining <= 0f;
    public float ReadyWait => readyWait;
    public bool IsAdhered => adhered;
    public override bool CanUseWhileCarried => outcome == Outcome.Waiting && !IsDead;
    public override bool CanBeCarried => outcome != Outcome.Hatching && outcome != Outcome.Hatched;
    public override bool CanBeConsumed => outcome != Outcome.Hatching && outcome != Outcome.Hatched;
    public override string InteractionPrompt => !IsDead && outcome == Outcome.Waiting ? "Crack egg" : null;
    public override string CarriedPrompt => !IsDead ? "[LMB] Crack egg  |  [RMB] Drop / hold to throw" : null;

    public void InitializeLaid(CeilingBatBugHabitat data, Vector3 home, RaycastHit hit)
    {
        habitat = data; inheritedHome = home;
        incubationRemaining = UnityEngine.Random.Range(data.incubation.x, data.incubation.y);
        outcome = Outcome.Waiting; readyWait = 0f;
        SetSupport(hit);
    }
    protected override void InitializeCreature()
    {
        SetCreatureName("Bat Bug Egg"); SetFoodAndDiet(FoodTag.Matter, FoodTag.None);
        body = GetComponent<Rigidbody>();
        properties = new MaterialPropertyBlock();
        look = shell != null ? shell.GetComponent<Renderer>() : GetComponentInChildren<Renderer>();
        SetMovementPhysics(adhered, !adhered);
    }
    protected override void Update()
    {
        if (habitat != null && !habitat.Ready(transform.position)) return;
        base.Update();
    }
    protected override void TickCreature(float dt)
    {
        if (outcome != Outcome.Waiting) return;
        incubationRemaining = Mathf.Max(0f, incubationRemaining - dt);
        if (IsReady) readyWait += dt;
        if (look != null)
        {
            properties.SetColor("_BaseColor", IsReady ? new Color(1f, 0.65f, 0.12f) : new Color(0.35f, 0.28f, 0.12f));
            look.SetPropertyBlock(properties);
        }
        if (IsReady && (!attemptedReady || (waitReason == WaitReason.Clearance && Time.time >= nextRetry)))
        { attemptedReady = true; TryHatch(); }
    }
    private void FixedUpdate()
    {
        if (IsDead || outcome != Outcome.Waiting || !SimulationEnabled || IsCarried || IsBubbleCaptured ||
            (TryGetComponent<WebCapture>(out var web) && web.IsCaptured) || CreatureThrower.IsFlying(this) ||
            (habitat != null && !habitat.Ready(transform.position))) return;
        if (adhered && support != null)
        {
            surfaceNormal = support.TransformDirection(localSupportNormal).normalized;
            body.MovePosition(support.TransformPoint(localSupportPoint) + surfaceNormal * 0.15f);
            return;
        }
        if (adhered) { adhered = false; SetMovementPhysics(false, true); }
        if (!body.isKinematic && body.linearVelocity.sqrMagnitude < 1.5f && TrySurface(transform.position + surfaceNormal * 0.15f, -surfaceNormal, 0.5f, out RaycastHit hit))
        {
            if (habitat != null && !habitat.Contains(hit.point)) return;
            SetSupport(hit); SetMovementPhysics(true, false);
            body.position = hit.point + hit.normal * 0.15f;
            attemptedReady = false;
            if (IsReady) TryHatch();
        }
    }
    private bool TrySurface(Vector3 from, Vector3 direction, float distance, out RaycastHit nearest)
    {
        nearest = default; float best = float.PositiveInfinity;
        int count = gameObject.scene.GetPhysicsScene().Raycast(from, direction, surfaceHits, distance, (1 << 7) | (1 << 8), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (surfaceHits[i].distance < best && surfaceHits[i].collider.GetComponentInParent<Creature>() == null)
        { nearest = surfaceHits[i]; best = nearest.distance; }
        return best < float.PositiveInfinity;
    }
    private void SetSupport(RaycastHit hit)
    {
        support = hit.collider.transform; surfaceNormal = hit.normal;
        localSupportPoint = support.InverseTransformPoint(hit.point);
        localSupportNormal = support.InverseTransformDirection(hit.normal);
        adhered = true;
    }
    protected override void OnCarryStarted()
    { adhered = false; support = null; attemptedReady = false; SetMovementPhysics(false, true); }
    protected override void OnCaptureStarted() { adhered = false; support = null; SetMovementPhysics(false, true); }
    protected override void OnSimulationChanged() { if (SimulationEnabled) { attemptedReady = false; nextRetry = Time.time; } }
    public override void ReceiveInteraction(CreatureInteraction interaction) { if (interaction.Source != null) BreakEgg(); }
    public override void ReceiveImpact(CreatureImpact impact) { if (CanReceiveImpact(impact)) BreakEgg(); }
    public void ReceiveCarrierLanding(float normalSpeed) { if (IsCarried && normalSpeed >= MinimumImpactSpeed) BreakEgg(); }
    public bool BreakEgg()
    {
        if (IsDead || outcome != Outcome.Waiting) return false;
        outcome = Outcome.Broken; incubationRemaining = 0f; adhered = false; support = null;
        Die(); return true;
    }
    public override void Die()
    { if (outcome != Outcome.Hatching && outcome != Outcome.Hatched) base.Die(); }
    protected override void OnDeath()
    {
        if (outcome == Outcome.Hatched) return;
        outcome = Outcome.Broken; adhered = false; support = null;
        if (shell != null) shell.gameObject.SetActive(false);
        if (brokenShell != null) brokenShell.gameObject.SetActive(true);
        SetMovementPhysics(false, true);
    }
    public bool TryHatch()
    {
        if (!IsReady || !SimulationEnabled || !isActiveAndEnabled || IsCarried || IsBubbleCaptured || !adhered || support == null ||
            (TryGetComponent<WebCapture>(out var web) && web.IsCaptured) || habitat == null || habitat.bugPrefab == null ||
            !habitat.Ready(transform.position) || !habitat.Contains(transform.position) || CreatureThrower.IsFlying(this)) return false;
        Physics.SyncTransforms();
        int count;
        do
        {
            count = gameObject.scene.GetPhysicsScene().OverlapSphere(transform.position, habitat.sensingRadius, nearby, ~0, QueryTriggerInteraction.Ignore);
            if (count < nearby.Length) break;
            Array.Resize(ref nearby, nearby.Length * 2);
        } while (nearby.Length < 4096);
        seen.Clear();
        for (int i = 0; i < count; i++)
        {
            var bug = nearby[i].GetComponentInParent<CeilingBatBug>();
            if (bug != null && !bug.IsDead && bug.Life != null && bug.Life.Habitat == habitat &&
                Vector3.Distance(bug.transform.position, transform.position) <= habitat.sensingRadius) seen.Add(bug);
        }
        if (seen.Count >= habitat.localLivingLimit) { waitReason = WaitReason.Density; return false; }
        Vector3 side = Vector3.Cross(surfaceNormal, Mathf.Abs(surfaceNormal.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        for (int i = 0; i < 8; i++)
        {
            Vector3 from = transform.position + Quaternion.AngleAxis(i * 45f, surfaceNormal) * side * 0.6f + surfaceNormal * 0.3f;
            if (!TrySurface(from, -surfaceNormal, 1f, out RaycastHit hit)) continue;
            Vector3 spawn = hit.point + hit.normal * 0.23f;
            if (!habitat.Contains(spawn) || Vector3.Distance(spawn, inheritedHome) > habitat.roamRadius) continue;
            int occupied = gameObject.scene.GetPhysicsScene().OverlapSphere(spawn, 0.205f, nearby, ~0, QueryTriggerInteraction.Ignore);
            if (occupied != 0) continue;
            outcome = Outcome.Hatching;
            GameObject staging = new GameObject("Hatch staging"); staging.SetActive(false); SceneManager.MoveGameObjectToScene(staging, gameObject.scene);
            CeilingBatBug child = null;
            try
            {
                child = Instantiate(habitat.bugPrefab, spawn, Quaternion.LookRotation(side, hit.normal), staging.transform);
                child.GetComponent<CeilingBatBugLife>().InitializeNewborn(habitat, inheritedHome, UnityEngine.Random.Range(1, int.MaxValue));
                child.transform.SetParent(null, true);
                Physics.SyncTransforms(); // Expose this child to another egg's query in this same callback.
                outcome = Outcome.Hatched;
                gameObject.SetActive(false); Destroy(gameObject);
                return true;
            }
            catch
            {
                if (child != null) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
                outcome = Outcome.Waiting;
                throw;
            }
            finally { Destroy(staging); }
        }
        waitReason = WaitReason.Clearance; nextRetry = Time.time + 2f;
        return false;
    }
}
