using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The player's inventory: four slots, like a hotbar. The selected slot is the hand.
///
/// - Right click picks a creature up as before: it goes into the selected slot (which must be empty, as the hand is).
/// - The mouse wheel selects another slot: whatever the hand holds is put away on the body (each slot has its own
///   body part), and the creature of the newly selected slot comes into the hand.
/// - Putting down or throwing (right click) empties the selected slot.
/// - CreatureHotbarHUD draws four dots along the bottom of the screen (hollow = empty, solid = holding) and an
///   arrow over the selected one. No text.
///
/// Creatures put away stay alive on their body part (StowedCreature) and share heat with their neighbours.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1010)] // After PlayerCreatureCarrier.
public sealed class BodyMounts : MonoBehaviour
{
    [System.Serializable]
    public struct Mount
    {
        public string name;
        [Tooltip("Where its middle sits, from the eye in the body's heading (right, up, forward).")]
        public Vector3 offset;
        [Tooltip("Largest size a creature is shrunk to here (metres).")]
        [Min(0.05f)] public float size;
        [Tooltip("How quickly it swings round after the body turns (lower = more delay).")]
        [Min(0.5f)] public float swingRate;
        [Tooltip("Tilt of the creature on this part (degrees).")]
        public Vector3 tilt;
    }

    [Tooltip("One body part per slot, in the order of the dots on screen (left to right).")]
    [SerializeField] private Mount[] mounts =
    {
        // Low on the body and out to the sides, so nothing hangs in front of the eyes: hips at the sides, the belly
        // low and small, the back behind the shoulders.
        new Mount { name = "Left hip", offset = new Vector3(-0.3f, -0.9f, 0.02f), size = 0.24f, swingRate = 8f, tilt = new Vector3(0f, 0f, 12f) },
        new Mount { name = "Belly", offset = new Vector3(0f, -0.82f, 0.2f), size = 0.2f, swingRate = 11f, tilt = new Vector3(-12f, 0f, 0f) },
        new Mount { name = "Back", offset = new Vector3(0f, -0.5f, -0.32f), size = 0.3f, swingRate = 6f, tilt = new Vector3(12f, 0f, 0f) },
        new Mount { name = "Right hip", offset = new Vector3(0.3f, -0.9f, 0.02f), size = 0.24f, swingRate = 9f, tilt = new Vector3(0f, 0f, -12f) },
    };

    [Header("Wheel")]
    [Tooltip("Scroll needed for one step. The Input System reports about 1 per wheel notch on Windows (older versions " +
             "120), and a touchpad sends many small amounts, so a small threshold plus a short pause works for all.")]
    [Min(0.01f)] [SerializeField] private float wheelThreshold = 0.5f;
    [Tooltip("Shortest time between two steps, so one fast flick or a touchpad swipe does not skip several slots.")]
    [Min(0f)] [SerializeField] private float stepPause = 0.12f;
    [Tooltip("Scrolling down moves the arrow right. Off: scrolling up does.")]
    [SerializeField] private bool downMovesRight = true;

    [Header("Living On The Body")]
    [Tooltip("Parts closer than this (metres between their mount points) are neighbours and share heat.")]
    [Min(0f)] [SerializeField] private float neighbourDistance = 0.65f;
    [Tooltip("Share of the heat difference that flows between two neighbours each second.")]
    [Range(0f, 2f)] [SerializeField] private float heatShare = 0.25f;
    [Tooltip("Most heat per second that flows between two neighbours.")]
    [Min(0f)] [SerializeField] private float maxHeatFlow = 3f;
    [Tooltip("How far (metres per m/s of body speed) the creatures lag behind when the body moves, easing back at Inertia Rate.")]
    [Range(0f, 0.05f)] [SerializeField] private float inertia = 0.012f;
    [Min(0.5f)] [SerializeField] private float inertiaRate = 5f;
    [Tooltip("A creature on the body is hidden while it would show above this height of the screen (0 = bottom, " +
             "1 = top) or closer to the eye than Clear Distance, so it never covers the view. Looking down it shows.")]
    [Range(0f, 1f)] [SerializeField] private float clearAbove = 0.3f;
    [Min(0f)] [SerializeField] private float clearDistance = 0.35f;
    [Tooltip("Looking down more than this many degrees below level, everything on the body shows.")]
    [Range(0f, 90f)] [SerializeField] private float lookDownToSee = 40f;
    [Tooltip("While hand climbing, mounts are pulled this far back so they never touch the wall.")]
    [Min(0f)] [SerializeField] private float climbTuck = 0.2f;

    private StowedCreature[] stowed;   // per slot; the selected slot's creature is in the hand instead
    private Creature[] inHand;          // per slot: what the hand held while that slot was selected
    private int selected;
    private float scrollAccumulated;
    private float nextStepAt;
    private PlayerCreatureCarrier carrier;
    private SmoothFirstPersonController controller;
    private HandClimber climber;
    private Rigidbody playerBody;
    private float tuck;
    private Vector3 sway;
    private Vector3 lastEye;

    public int SlotCount => mounts.Length;
    public int Selected => selected;
    /// <summary>True when slot i holds a creature (in the hand or on the body).</summary>
    public bool IsFilled(int i) => i == selected ? HandCreature() != null : Ensure() && stowed[i] != null && stowed[i].Creature != null;
    /// <summary>The creature of slot i (in the hand or on the body), or null.</summary>
    public Creature SlotCreature(int i) => i == selected ? HandCreature() : Ensure() ? stowed[i]?.Creature : null;
    /// <summary>Last time the selection changed or a slot filled / emptied, for the HUD's little pulse.</summary>
    public float LastChange { get; private set; } = -10f;

    private void Awake()
    {
        Ensure();
        carrier = GetComponent<PlayerCreatureCarrier>();
        if (carrier == null) carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        controller = GetComponentInParent<SmoothFirstPersonController>();
        if (controller == null) controller = FindFirstObjectByType<SmoothFirstPersonController>();
        playerBody = controller != null ? controller.GetComponent<Rigidbody>() : null;
        climber = GetComponentInParent<HandClimber>();
        if (climber == null) climber = FindFirstObjectByType<HandClimber>();
        if (FindFirstObjectByType<CreatureHotbarHUD>() == null) gameObject.AddComponent<CreatureHotbarHUD>().Bind(this);
    }

    /// <summary>
    /// The slot arrays. Unity cannot keep them over a script reload in Play mode (StowedCreature is not serializable)
    /// and Awake does not run again, so they are made on first use. Creatures that were on the body at a reload
    /// stay where they are (still owned by the carrier) until picked up again.
    /// </summary>
    private bool Ensure()
    {
        if (stowed == null || stowed.Length != mounts.Length) stowed = new StowedCreature[mounts.Length];
        if (inHand == null || inHand.Length != mounts.Length) inHand = new Creature[mounts.Length];
        selected = Mathf.Clamp(selected, 0, mounts.Length - 1);
        return true;
    }

    private Creature HandCreature() => carrier != null ? carrier.CarriedCreature : null;

    private void Update()
    {
        if (carrier == null) return;
        Ensure();
        Prune();
        // The hand is the selected slot: note pickups, putdowns and throws.
        Creature hand = HandCreature();
        if (hand != inHand[selected]) { inHand[selected] = hand; LastChange = Time.time; }

        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        if (BoneAwakeningCutscene.Playing || RestSpotSystem.BlocksInteraction || MeditationController.BlocksPlayerInput) { scrollAccumulated = 0f; return; }
        if (!Application.isFocused && !Application.isBatchMode) return;
        // Dragging with a held left click uses the wheel for distance (PlayerCreaturePetter).
        if (mouse.leftButton.isPressed) { scrollAccumulated = 0f; return; }
        scrollAccumulated += mouse.scroll.ReadValue().y;
        if (Time.unscaledTime < nextStepAt) { scrollAccumulated = 0f; return; }
        if (Mathf.Abs(scrollAccumulated) < wheelThreshold) return;
        nextStepAt = Time.unscaledTime + stepPause;
        int step = scrollAccumulated > 0f ? 1 : -1;
        if (downMovesRight) step = -step;
        scrollAccumulated = 0f;
        Select((selected + step + mounts.Length) % mounts.Length);
    }

    /// <summary>Makes slot i the hand. Public so tests can drive it.</summary>
    public bool Select(int index)
    {
        if (carrier == null) return false;
        Ensure();
        index = Mathf.Clamp(index, 0, mounts.Length - 1);
        if (index == selected || carrier.PendingCreature != null || carrier.TentacleLockedForRide) return false;
        Creature hand = HandCreature();
        // A tentacle creature rides on the back with its own rig; it is never put away, so the slot stays.
        if (hand != null && carrier.CarriesTentacle) return false;

        // Put away what the hand holds onto this slot's body part.
        if (hand != null)
        {
            carrier.Release();
            if (StowedCreature.CanStow(hand))
                stowed[selected] = new StowedCreature(hand, carrier, selected, mounts[selected].size, BodyYaw());
        }
        inHand[selected] = null;
        selected = index;
        LastChange = Time.time;

        // Take this slot's creature into the hand.
        StowedCreature next = stowed[selected];
        stowed[selected] = null;
        if (next != null && next.Creature != null)
        {
            next.Restore();
            if (carrier.TryCarryFromInteraction(next.Creature)) inHand[selected] = next.Creature;
        }
        AudioManager.PostEvent("Play_PickUp", gameObject);
        return true;
    }

    private void Prune()
    {
        // Something that died or was taken by something else falls off the body.
        for (int i = 0; i < stowed.Length; i++)
        {
            if (stowed[i] == null || stowed[i].Valid) continue;
            stowed[i].Restore();
            stowed[i] = null;
            LastChange = Time.time;
        }
    }

    private void ShareHeat(float deltaTime)
    {
        if (heatShare <= 0f || deltaTime <= 0f) return;
        float k = 1f - Mathf.Exp(-heatShare * deltaTime);
        for (int a = 0; a < stowed.Length; a++)
        for (int b = a + 1; b < stowed.Length; b++)
        {
            if (stowed[a] == null || stowed[b] == null) continue;
            if (Vector3.Distance(mounts[a].offset, mounts[b].offset) > neighbourDistance) continue;
            HeatFlow.Share(stowed[a].Creature, stowed[b].Creature, k, maxHeatFlow * deltaTime);
        }
    }

    private Quaternion BodyYaw()
    {
        Camera cam = carrier != null ? carrier.CarryCamera : null;
        Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : transform.forward;
        return forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
    }

    // After the camera has moved, so the mounts never trail the view.
    private void LateUpdate()
    {
        Ensure();
        if (carrier == null || carrier.CarryCamera == null) return;
        float dt = Time.deltaTime;
        ShareHeat(dt);
        tuck = Mathf.MoveTowards(tuck, climber != null && climber.IsClimbing ? 1f : 0f, dt * 3f);
        Vector3 eye = carrier.CarryCamera.transform.position;
        Quaternion yaw = BodyYaw();
        // Inertia: moving leaves the creatures a little behind, then they ease back (never further than 8 cm).
        Vector3 velocity = playerBody != null ? playerBody.linearVelocity : (dt > 0f ? (eye - lastEye) / dt : Vector3.zero);
        lastEye = eye;
        Vector3 wantSway = Vector3.ClampMagnitude(Quaternion.Inverse(yaw) * -velocity * inertia, 0.08f);
        sway = Vector3.Lerp(sway, wantSway, 1f - Mathf.Exp(-inertiaRate * dt));
        for (int i = 0; i < stowed.Length; i++)
        {
            StowedCreature s = stowed[i];
            if (s == null || s.Creature == null) continue;
            Mount m = mounts[i];
            s.Ride(eye, yaw, m.offset + Vector3.back * (climbTuck * tuck) + sway, Quaternion.Euler(m.tilt), m.swingRate, 0f, dt);
            s.SetHidden(BlocksView(s));
        }
    }

    /// <summary>True when a creature on the body would sit in the middle of the view or right against the eye.</summary>
    private bool BlocksView(StowedCreature s)
    {
        Camera cam = carrier.CarryCamera;
        Vector3 to = s.Centre - cam.transform.position;
        if (to.magnitude - s.Radius < clearDistance) return true;
        // Looking well down at the body is looking at what it carries: then everything on it shows.
        if (Vector3.Angle(cam.transform.forward, Vector3.down) < 90f - lookDownToSee) return false;
        Vector3 vp = cam.WorldToViewportPoint(s.Centre);
        if (vp.z <= 0f) return false;                                      // behind the camera: not seen
        if (vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f) return false;     // off screen
        // Its top edge in the view: hidden when that reaches above the low band of the screen.
        float top = cam.WorldToViewportPoint(s.Centre + cam.transform.up * s.Radius).y;
        return top > clearAbove + 0.25f;
    }

    private void OnDisable()
    {
        if (stowed == null) return;
        for (int i = 0; i < stowed.Length; i++)
        {
            if (stowed[i] != null) stowed[i].Restore();
            stowed[i] = null;
        }
    }
}

/// <summary>
/// Heat moving between two creatures that touch: the warmer gives the colder part of the difference, at most
/// maxAmount (so a big fire next to a small fuzz warms it steadily instead of all at once).
/// </summary>
public static class HeatFlow
{
    public static void Share(Creature a, Creature b, float fraction, float maxAmount)
    {
        if (a == null || b == null || a.IsDead || b.IsDead) return;
        float flow = Mathf.Clamp((a.Heat - b.Heat) * 0.5f * Mathf.Clamp01(fraction), -maxAmount, maxAmount);
        if (Mathf.Abs(flow) < 1e-5f) return;
        if (flow > 0f) { a.SetHeat(a.Heat - flow); b.AddHeat(flow); }
        else { b.SetHeat(b.Heat + flow); a.AddHeat(-flow); }
    }
}
