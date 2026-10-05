using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hand-over-hand climbing. Instead of pushing the rigidbody up the wall, each hand grabs a real point
/// on the surface and the body is pulled up to hang below the hands. One hand moves at a time, so the
/// player climbs in reaches; at the top, the hands plant on the ledge and the body mantles over.
/// <see cref="FirstPersonBody"/> reads the grips to pose the arms.
///
/// Controls while climbing: W/S up and down, A/D sideways, Space to push off the wall.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1050)]
public sealed class HandClimber : MonoBehaviour
{
    [Header("References (found automatically when empty)")]
    [SerializeField] private SmoothFirstPersonController controller;

    [Header("Detection")]
    [Tooltip("Layers that can be climbed. Generated terrain lives on Ground.")]
    [SerializeField] private LayerMask climbLayers = (1 << 7) | (1 << 8);
    [Tooltip("Surfaces flatter than this (degrees from horizontal) cannot be climbed.")]
    [Range(0f, 90f)] [SerializeField] private float minWallSteepness = 55f;
    [Min(0.1f)] [SerializeField] private float detectDistance = 0.9f;
    [Tooltip("Largest angle between where the player faces and the wall.")]
    [Range(0f, 90f)] [SerializeField] private float maxFacingAngle = 55f;

    [Header("Body")]
    [Tooltip("Distance from the wall to the player's centre while hanging.")]
    [Min(0.1f)] [SerializeField] private float wallOffset = 0.5f;
    [Tooltip("How far above the player's centre the hands grip.")]
    [SerializeField] private float handHeight = 0.6f;
    [Min(0f)] [SerializeField] private float handSpread = 0.3f;
    [Tooltip("How fast the arms pull the body towards its hanging spot.")]
    [Min(0.1f)] [SerializeField] private float pullSpeed = 2.4f;

    [Header("Reaching")]
    [Min(0.1f)] [SerializeField] private float reachStep = 0.5f;
    [Min(0.05f)] [SerializeField] private float reachTime = 0.22f;
    [Tooltip("Hands let go if the body ends up this far from them.")]
    [Min(0.5f)] [SerializeField] private float maxHandDistance = 1.4f;

    [Header("Exits")]
    [Min(0.05f)] [SerializeField] private float mantleTime = 0.5f;
    [SerializeField] private Vector2 jumpOffVelocity = new Vector2(5f, 5f);
    [Min(0f)] [SerializeField] private float regrabDelay = 0.35f;

    [Header("Keys")]
    [SerializeField] private Key upKey = Key.W;
    [SerializeField] private Key downKey = Key.S;
    [SerializeField] private Key leftKey = Key.A;
    [SerializeField] private Key rightKey = Key.D;
    [SerializeField] private Key jumpOffKey = Key.Space;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool climbing;
    [SerializeField] private bool mantling;
    [SerializeField] private bool canStartClimb;

    private struct Hand
    {
        public Vector3 hold, normal;
        public bool moving;
        public Vector3 from, to, toNormal;
        public float t;
    }

    private readonly Hand[] hands = new Hand[2];
    private readonly Vector3[] feet = new Vector3[2];
    private Rigidbody body;
    private Transform orientation;
    private Vector3 wallNormal;
    private Vector3 bodyTarget;
    private float regrabTimer;
    private float halfHeight = 1f;

    // Mantle path.
    private Vector3 mantleFrom, mantleLift, mantleTo;
    private float mantleT;

    public bool IsClimbing => climbing;
    public bool IsMantling => mantling;
    public bool CanStartClimb => canStartClimb;

    /// <summary>World grip point for a hand (0 = left, 1 = right). False when not climbing.</summary>
    public bool GetHand(int index, out Vector3 hold, out Vector3 normal, out bool gripping)
    {
        hold = default;
        normal = Vector3.up;
        gripping = false;
        if (!climbing) return false;
        Hand hand = hands[index];
        if (hand.moving)
        {
            float t = Mathf.SmoothStep(0f, 1f, hand.t);
            // Swing the hand away from the wall while it travels.
            hold = Vector3.Lerp(hand.from, hand.to, t) + hand.toNormal * (Mathf.Sin(t * Mathf.PI) * 0.15f);
            normal = Vector3.Slerp(hand.normal, hand.toNormal, t);
            return true;
        }
        hold = hand.hold;
        normal = hand.normal;
        gripping = true;
        return true;
    }

    public bool GetFoot(int index, out Vector3 foothold)
    {
        foothold = feet[index];
        return climbing;
    }

    private void Awake()
    {
        if (controller == null) controller = FindFirstObjectByType<SmoothFirstPersonController>();
        if (controller == null) return;
        body = controller.GetComponent<Rigidbody>();
        orientation = controller.Orientation != null ? controller.Orientation : controller.transform;
        var capsule = controller.GetComponent<CapsuleCollider>();
        if (capsule != null) halfHeight = capsule.height * 0.5f * controller.transform.lossyScale.y;
    }

    private void Update()
    {
        if (controller == null || body == null) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        regrabTimer = Mathf.Max(0f, regrabTimer - Time.deltaTime);

        if (mantling)
        {
            UpdateMantle();
            return;
        }

        if (!climbing)
        {
            canStartClimb = regrabTimer <= 0f && FindWall(out RaycastHit wall);
            if (canStartClimb && keyboard[upKey].isPressed && !controller.Grounded)
                TryStartClimb();
            else if (canStartClimb && keyboard[upKey].isPressed && controller.Grounded && WallIsTall())
                TryStartClimb();
            return;
        }

        canStartClimb = false;
        if (keyboard[jumpOffKey].wasPressedThisFrame)
        {
            Exit(wallNormal * jumpOffVelocity.x + Vector3.up * jumpOffVelocity.y);
            return;
        }

        float vertical = (keyboard[upKey].isPressed ? 1f : 0f) - (keyboard[downKey].isPressed ? 1f : 0f);
        float horizontal = (keyboard[rightKey].isPressed ? 1f : 0f) - (keyboard[leftKey].isPressed ? 1f : 0f);

        // Climbing down onto the floor ends the climb.
        if (vertical < 0f && Physics.Raycast(body.position, Vector3.down, halfHeight + 0.15f, climbLayers, QueryTriggerInteraction.Ignore))
        {
            Exit(Vector3.zero);
            return;
        }

        AdvanceHands();
        if (vertical != 0f || horizontal != 0f) TryReach(vertical, horizontal);
        UpdateBodyTarget();
        UpdateFeet();

        if ((hands[0].hold - body.position).magnitude > maxHandDistance &&
            (hands[1].hold - body.position).magnitude > maxHandDistance)
            Exit(Vector3.zero);
    }

    private void FixedUpdate()
    {
        if (!climbing || mantling || body == null) return;
        Vector3 next = Vector3.MoveTowards(body.position, bodyTarget, pullSpeed * Time.fixedDeltaTime);
        body.MovePosition(next);
    }

    // ---------------- Starting ----------------

    private bool FindWall(out RaycastHit hit)
    {
        Vector3 origin = controller.transform.position + Vector3.up * 0.3f;
        if (!Physics.SphereCast(origin, 0.25f, orientation.forward, out hit, detectDistance, climbLayers, QueryTriggerInteraction.Ignore))
            return false;
        if (Vector3.Angle(hit.normal, Vector3.up) < minWallSteepness) return false;
        Vector3 flatNormal = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
        return flatNormal.sqrMagnitude > 1e-4f && Vector3.Angle(orientation.forward, -flatNormal) <= maxFacingAngle;
    }

    /// <summary>From the ground, only grab walls that go higher than a step.</summary>
    private bool WallIsTall()
    {
        Vector3 origin = controller.transform.position + Vector3.up * (handHeight + 0.4f);
        return Physics.Raycast(origin, orientation.forward, detectDistance + 0.4f, climbLayers, QueryTriggerInteraction.Ignore);
    }

    private void TryStartClimb()
    {
        if (!FindWall(out RaycastHit wall)) return;
        wallNormal = wall.normal;
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            Vector3 guess = controller.transform.position + WallUp() * handHeight + WallRight() * (handSpread * side);
            if (!ProbeWall(guess, out RaycastHit grip)) return;
            hands[i] = new Hand { hold = grip.point, normal = grip.normal };
        }

        climbing = true;
        body.linearVelocity = Vector3.zero;
        body.isKinematic = true;
        controller.SetRestricted(true);
        controller.SetClimbing(true);
        for (int i = 0; i < 2; i++) feet[i] = body.position + Vector3.down * halfHeight * 0.85f;
        UpdateBodyTarget();
    }

    // ---------------- Reaching ----------------

    private void AdvanceHands()
    {
        for (int i = 0; i < 2; i++)
        {
            if (!hands[i].moving) continue;
            hands[i].t += Time.deltaTime / reachTime;
            if (hands[i].t < 1f) continue;
            hands[i].moving = false;
            hands[i].hold = hands[i].to;
            hands[i].normal = hands[i].toNormal;
        }
    }

    private void TryReach(float vertical, float horizontal)
    {
        if (hands[0].moving || hands[1].moving) return;

        Vector3 up = WallUp();
        Vector3 right = WallRight();
        Vector3 direction = (up * vertical + right * horizontal).normalized;
        Vector3 anchor = (hands[0].hold + hands[1].hold) * 0.5f;

        // Move the trailing hand: the one furthest behind in the direction of travel.
        int index = Vector3.Dot(hands[0].hold - anchor, direction) <= Vector3.Dot(hands[1].hold - anchor, direction) ? 0 : 1;
        float side = index == 0 ? -1f : 1f;
        Vector3 guess = anchor + right * (handSpread * side) + direction * reachStep;

        if (ProbeWall(guess, out RaycastHit grip))
        {
            hands[index].moving = true;
            hands[index].t = 0f;
            hands[index].from = hands[index].hold;
            hands[index].to = grip.point;
            hands[index].toNormal = grip.normal;
            return;
        }

        // No wall above: if there is floor up there, climb over the top.
        if (vertical > 0f && FindLedgeTop(anchor, out Vector3 top)) StartMantle(top);
    }

    /// <summary>Finds the climbable surface near a guessed grip point.</summary>
    private bool ProbeWall(Vector3 guess, out RaycastHit hit)
    {
        Vector3 origin = guess + wallNormal * 0.6f;
        if (!Physics.Raycast(origin, -wallNormal, out hit, 1.4f, climbLayers, QueryTriggerInteraction.Ignore))
            return false;
        return Vector3.Angle(hit.normal, Vector3.up) >= minWallSteepness * 0.8f;
    }

    private bool FindLedgeTop(Vector3 anchor, out Vector3 top)
    {
        Vector3 origin = anchor + Vector3.up * 1.0f - wallNormal * 0.5f;
        top = default;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1.6f, climbLayers, QueryTriggerInteraction.Ignore))
            return false;
        if (hit.normal.y < 0.6f) return false;
        // Room to stand?
        if (Physics.Raycast(hit.point + Vector3.up * 0.1f, Vector3.up, halfHeight * 2f, climbLayers, QueryTriggerInteraction.Ignore))
            return false;
        top = hit.point;
        return true;
    }

    // ---------------- Body ----------------

    private void UpdateBodyTarget()
    {
        Vector3 normal = (hands[0].normal + hands[1].normal).normalized;
        if (normal.sqrMagnitude < 0.5f) normal = wallNormal;
        wallNormal = normal;
        Vector3 grips = (hands[0].hold + hands[1].hold) * 0.5f;
        bodyTarget = grips - WallUp() * handHeight + wallNormal * wallOffset;
    }

    private void UpdateFeet()
    {
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            // Feet lag behind the hands and press into the wall below the body.
            Vector3 target = body.position - WallUp() * (halfHeight * 0.8f) + WallRight() * (0.16f * side) - wallNormal * (wallOffset - 0.15f);
            float lag = hands[1 - i].moving ? 3f : 10f;
            feet[i] = Vector3.Lerp(feet[i], target, 1f - Mathf.Exp(-lag * Time.deltaTime));
        }
    }

    private Vector3 WallUp()
    {
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, wallNormal);
        return up.sqrMagnitude > 1e-4f ? up.normalized : Vector3.up;
    }

    private Vector3 WallRight() => Vector3.Cross(wallNormal, WallUp()).normalized;

    // ---------------- Exits ----------------

    private void StartMantle(Vector3 top)
    {
        mantling = true;
        mantleT = 0f;
        mantleFrom = body.position;
        mantleTo = top + Vector3.up * (halfHeight + 0.05f);
        mantleLift = new Vector3(mantleFrom.x, mantleTo.y + 0.15f, mantleFrom.z);

        // Plant both hands on the ledge.
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            Vector3 hold = top + WallRight() * (handSpread * side) + wallNormal * 0.1f;
            hands[i] = new Hand { moving = true, from = hands[i].hold, to = hold, normal = hands[i].normal, toNormal = Vector3.up, t = 0f };
        }
    }

    private void UpdateMantle()
    {
        mantleT += Time.deltaTime / mantleTime;
        AdvanceHands();
        float t = Mathf.Clamp01(mantleT);
        // Up first, then over the edge.
        Vector3 position = t < 0.6f
            ? Vector3.Lerp(mantleFrom, mantleLift, Mathf.SmoothStep(0f, 1f, t / 0.6f))
            : Vector3.Lerp(mantleLift, mantleTo, Mathf.SmoothStep(0f, 1f, (t - 0.6f) / 0.4f));
        body.MovePosition(position);
        UpdateFeet();

        if (mantleT >= 1f)
        {
            mantling = false;
            Exit(Vector3.zero);
        }
    }

    private void Exit(Vector3 velocity)
    {
        climbing = false;
        mantling = false;
        regrabTimer = regrabDelay;
        if (body != null)
        {
            body.isKinematic = false;
            body.linearVelocity = velocity;
        }
        if (controller != null)
        {
            controller.SetRestricted(false);
            controller.SetClimbing(false);
        }
    }

    private void OnDisable()
    {
        if (climbing) Exit(Vector3.zero);
    }
}
