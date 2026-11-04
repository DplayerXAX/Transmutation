using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>One moving anchor, swept upright capsule, and independent pickup controls.</summary>
[DefaultExecutionOrder(1040)]
[DisallowMultipleComponent]
[RequireComponent(typeof(SmoothFirstPersonController))]
public sealed class PlayerBugGrip : MonoBehaviour
{
    [SerializeField] private float reach = 2.2f, maxStretch = 2.6f, followSpeed = 8f;
    private SmoothFirstPersonController controller;
    private PlayerCreatureCarrier carrier;
    private HandClimber climber;
    private Rigidbody body;
    private CapsuleCollider capsule;
    private CeilingBatBug target;
    private Vector3 localAnchor, lastAnchor, anchorVelocity;
    private bool wasKinematic, wasGravity, wasRestricted, ownsMovement;
    private float nextGripAt;
    public bool? ScriptedHold { get; set; }
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private readonly Collider[] overlaps = new Collider[32];
    public bool IsGripping => ownsMovement;
    public bool ConsumesInput { get; private set; }
    public Vector3 Anchor => target != null ? target.transform.TransformPoint(localAnchor) : body.position;
    public CeilingBatBug Target => target;
    private void Awake()
    {
        controller = GetComponent<SmoothFirstPersonController>(); body = GetComponent<Rigidbody>(); capsule = GetComponent<CapsuleCollider>();
        carrier = transform.root.GetComponentInChildren<PlayerCreatureCarrier>();
    }
    private void Start()
    { foreach (var c in transform.root.GetComponentsInChildren<HandClimber>()) if (c.Controls(controller)) { climber = c; break; } }
    private void OnEnable() { if (controller == null) controller = GetComponent<SmoothFirstPersonController>(); controller.Landed += ForwardLanding; }
    private void OnDisable() { Release(false); if (controller != null) controller.Landed -= ForwardLanding; }
    private void ForwardLanding(float speed)
    { if (carrier != null && carrier.CarriedCreature is CeilingBatBugEgg egg) egg.ReceiveCarrierLanding(speed); }
    private void Update()
    {
        ConsumesInput = false;
        if (!controller.enabled || controller.BubbleMovementBlocked || BoneAwakeningCutscene.Playing ||
            (carrier != null && (carrier.CarriedCreature != null || carrier.PendingCreature != null))) { Release(false); return; }
        bool held = ScriptedHold ?? (Application.isFocused && Mouse.current != null && Mouse.current.leftButton.isPressed);
        if (IsGripping && (!held || target == null || !target.CanSupportPlayer)) { Release(false); return; }
        if (!held) return;
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && IsGripping)
        { controller.SuppressJumpForCurrentFrame(); Release(true); ConsumesInput = true; return; }
        var camera = controller.PlayerCamera;
        if (camera != null)
        {
            var aimed = CreatureAim.Find(gameObject.scene.GetPhysicsScene(), new Ray(camera.transform.position, camera.transform.forward), reach + 0.5f, ~0, carrier, out _);
            if (aimed is CeilingBatBug bug && bug != target) TryAttach(bug);
        }
        ConsumesInput = IsGripping;
        if (IsGripping && Keyboard.current != null && Keyboard.current.wKey.isPressed && climber != null) climber.TryAcceptBugTransfer();
    }
    public bool CanReach(CeilingBatBug bug)
    {
        if (body == null || controller == null || !controller.enabled || controller.BubbleMovementBlocked || bug == null || !bug.CanSupportPlayer || bug.gameObject.scene != gameObject.scene) return false;
        Vector3 hand = body.position + Vector3.up * 0.65f, delta = bug.AimPosition - hand;
        if (delta.magnitude > reach) return false;
        int count = gameObject.scene.GetPhysicsScene().Raycast(hand, delta.normalized, hits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (!hits[i].collider.transform.IsChildOf(transform.root) &&
            hits[i].collider.GetComponentInParent<Creature>() != bug && hits[i].distance < delta.magnitude - 0.15f) return false;
        return true;
    }
    public static bool InReach(CeilingBatBug bug)
    {
        var camera = Camera.main;
        var grip = camera != null ? camera.transform.root.GetComponentInChildren<PlayerBugGrip>() : null;
        return grip != null && grip.CanReach(bug);
    }
    public bool TryAttach(CeilingBatBug bug)
    {
        if (Time.time < nextGripAt || !CanReach(bug) || controller.BubbleMovementBlocked || !controller.enabled ||
            (carrier != null && (carrier.CarriedCreature != null || carrier.PendingCreature != null)) || !ClearMove(body.position, DesiredBody(bug, bug.AimPosition), bug)) return false;
        if (!IsGripping)
        {
            climber?.StopForBubbleCapture(); GetComponent<AdvancedFirstPersonTraversal>()?.StopForBubbleCapture();
            wasKinematic = body.isKinematic; wasGravity = body.useGravity; wasRestricted = controller.Restricted;
            if (!body.isKinematic) body.linearVelocity = Vector3.zero;
        }
        else Unsubscribe();
        target = bug; ownsMovement = true; localAnchor = bug.transform.InverseTransformPoint(bug.AimPosition);
        lastAnchor = Anchor; anchorVelocity = Vector3.zero;
        target.SupportInvalidated += TargetInvalidated; IgnoreSupport(true);
        body.isKinematic = true; body.useGravity = false; controller.SetRestricted(true); controller.SetClimbing(true);
        return true;
    }
    private void FixedUpdate()
    {
        if (!IsGripping) return;
        if (target == null || !target.CanSupportPlayer || !controller.enabled || controller.BubbleMovementBlocked) { Release(false); return; }
        Vector3 anchor = Anchor;
        anchorVelocity = Vector3.ClampMagnitude((anchor - lastAnchor) / Time.fixedDeltaTime, 4f); lastAnchor = anchor;
        if (Vector3.Distance(anchor, body.position + Vector3.up * 0.65f) > maxStretch) { Release(false); return; }
        Vector3 desired = Vector3.MoveTowards(body.position, DesiredBody(target, anchor), followSpeed * Time.fixedDeltaTime);
        if (!ClearMove(body.position, desired, target)) { Release(false); return; }
        body.MovePosition(desired);
    }
    private Vector3 DesiredBody(CeilingBatBug bug, Vector3 anchor)
    {
        Vector3 normal = bug.GetComponent<CeilingBatBugSurfaceMotor>().SurfaceNormal;
        Vector3 hang = anchor + Vector3.ProjectOnPlane(normal, Vector3.up).normalized * 0.55f - Vector3.up * Mathf.Lerp(0.65f, 1.2f, Mathf.Clamp01(-normal.y));
        Vector3 forward = controller.Orientation != null ? controller.Orientation.forward : transform.forward;
        return Vector3.Lerp(hang, anchor + Vector3.up * 1.05f - forward * 0.35f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.9f, normal.y)));
    }
    private bool ClearMove(Vector3 from, Vector3 to, CeilingBatBug ignore)
    {
        if (capsule == null) return false;
        float radius = capsule.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) * 0.95f;
        float half = Mathf.Max(radius, capsule.height * transform.lossyScale.y * 0.5f);
        Vector3 center = transform.TransformVector(capsule.center), delta = to - from;
        Vector3 a = from + center + Vector3.up * (half - radius), b = from + center - Vector3.up * (half - radius);
        int count = delta.sqrMagnitude > 0.000001f ? Physics.CapsuleCastNonAlloc(a, b, radius, delta.normalized, hits, delta.magnitude, (1 << 7) | (1 << 8), QueryTriggerInteraction.Ignore) : 0;
        for (int i = 0; i < count; i++) if (hits[i].collider.GetComponentInParent<Creature>() != ignore && Vector3.Dot(delta, hits[i].normal) < -0.001f && hits[i].distance < delta.magnitude - 0.015f) return false;
        count = Physics.OverlapCapsuleNonAlloc(a + delta, b + delta, radius, overlaps, (1 << 7) | (1 << 8), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (overlaps[i].GetComponentInParent<Creature>() != ignore && Physics.ComputePenetration(capsule, to, transform.rotation,
            overlaps[i], overlaps[i].transform.position, overlaps[i].transform.rotation, out _, out float depth) && depth > 0.04f) return false;
        return true;
    }
    private void IgnoreSupport(bool ignore)
    { if (target != null) foreach (var c in target.GetComponentsInChildren<Collider>()) foreach (var p in GetComponents<Collider>()) Physics.IgnoreCollision(p, c, ignore); }
    private void Unsubscribe() { if (target != null) { target.SupportInvalidated -= TargetInvalidated; IgnoreSupport(false); } }
    private void TargetInvalidated() => Release(false);
    public void Release(bool jump, bool transfer = false)
    {
        if (!ownsMovement) return;
        if (body == null) { Unsubscribe(); target = null; ownsMovement = false; return; }
        Vector3 velocity = Vector3.ClampMagnitude(anchorVelocity + (jump ? Vector3.up * 5f + (body.position - Anchor).normalized * 2f : Vector3.zero), 7f);
        Unsubscribe(); target = null; ownsMovement = false; ConsumesInput = false;
        if (jump) nextGripAt = Time.time + 0.4f;
        if (transfer || body == null) return;
        if (!controller.BubbleMovementBlocked)
        {
            body.isKinematic = wasKinematic; body.useGravity = wasGravity;
            controller.SetRestricted(wasRestricted); controller.SetClimbing(false);
            if (!body.isKinematic) body.linearVelocity = velocity;
        }
    }
    public bool GetHand(int index, out Vector3 point, out Vector3 normal, out bool gripping)
    { point = Anchor; normal = IsGripping ? (body.position - Anchor).normalized : Vector3.up; gripping = IsGripping; return IsGripping; }
    public bool GetFrame(out Vector3 eye, out Vector3 right, out Vector3 up, out Vector3 forward)
    {
        eye = body.position + Vector3.up * 0.7f; up = Vector3.up;
        forward = IsGripping ? Vector3.ProjectOnPlane(Anchor - body.position, up).normalized : transform.forward;
        if (forward.sqrMagnitude < 0.1f) forward = controller.Orientation != null ? controller.Orientation.forward : transform.forward;
        right = Vector3.Cross(up, forward); return IsGripping;
    }
}
