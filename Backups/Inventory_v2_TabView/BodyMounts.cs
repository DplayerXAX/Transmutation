using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The player's inventory is their own body. Six body parts (shoulders, chest, back, hips) can each hold one
/// creature, shrunk and still alive; a nearby pack beast (FuzzPackBeast) adds the spots in its fur.
///
/// Tab opens the inventory view: the player stands still (the world keeps going), a camera turns round to look at
/// the player from the front, everything else is blurred, and each body part shows its creature (an empty part
/// shows a faint ring). It opens on the ground or while climbing, not in mid-air.
/// Left click a part: what is in the hand goes there, and what was there comes to the hand (a swap; either side
/// may be empty). The beast's spots work the same when it is close. Tab, right click or Esc closes the view and
/// the hand takes whatever it now holds. There is no other control and no text.
///
/// Outside the view the stowed creatures stay on the body (showOnBodyWhileWalking) and keep living: heat flows
/// between neighbouring parts, so a fire on the shoulder slowly warms the chest next to it.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1010)] // After PlayerCreatureCarrier and the controller's camera.
public sealed class BodyMounts : MonoBehaviour
{
    [SerializeField] private Key viewKey = Key.Tab;

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

    [SerializeField] private Mount[] mounts =
    {
        new Mount { name = "Right shoulder", offset = new Vector3(0.25f, -0.12f, -0.02f), size = 0.24f, swingRate = 8f, tilt = new Vector3(0f, 0f, -10f) },
        new Mount { name = "Left shoulder", offset = new Vector3(-0.25f, -0.12f, -0.02f), size = 0.24f, swingRate = 7f, tilt = new Vector3(0f, 0f, 10f) },
        new Mount { name = "Chest", offset = new Vector3(0f, -0.42f, 0.3f), size = 0.28f, swingRate = 11f, tilt = new Vector3(-12f, 0f, 0f) },
        new Mount { name = "Back", offset = new Vector3(0f, -0.36f, -0.3f), size = 0.34f, swingRate = 6f, tilt = new Vector3(12f, 0f, 0f) },
        new Mount { name = "Right hip", offset = new Vector3(0.27f, -0.86f, 0.05f), size = 0.26f, swingRate = 9f, tilt = new Vector3(0f, 0f, -12f) },
        new Mount { name = "Left hip", offset = new Vector3(-0.27f, -0.86f, 0.05f), size = 0.26f, swingRate = 8.5f, tilt = new Vector3(0f, 0f, 12f) },
    };

    [Header("Living On The Body")]
    [Tooltip("Off: stowed creatures are hidden outside the inventory view. On: they stay visible on the body.")]
    [SerializeField] private bool showOnBodyWhileWalking = true;
    [Tooltip("Parts closer than this (metres between their mount points) are neighbours and share heat.")]
    [Min(0f)] [SerializeField] private float neighbourDistance = 0.65f;
    [Tooltip("Share of the heat difference that flows between two neighbours each second.")]
    [Range(0f, 2f)] [SerializeField] private float heatShare = 0.25f;
    [Tooltip("Most heat per second that flows between two neighbours.")]
    [Min(0f)] [SerializeField] private float maxHeatFlow = 3f;

    [Header("Inventory View")]
    [Tooltip("How far in front of the player the view camera stands.")]
    [Min(1f)] [SerializeField] private float viewDistance = 2.3f;
    [Tooltip("Degrees the view camera stands to the side of straight in front, and above eye level.")]
    [SerializeField] private Vector2 viewAngles = new Vector2(24f, 8f);
    [Tooltip("A pack beast this close is shown in the view too, and its spots can be used.")]
    [Min(0f)] [SerializeField] private float beastRange = 9f;
    [Tooltip("The spot nearest the cursor within this many pixels (at 1080p) is the one a click uses.")]
    [Min(10f)] [SerializeField] private float hoverPixels = 80f;
    [Tooltip("Where the hand holds a creature while the view is open (from the eye: right, up, forward) and its size.")]
    [SerializeField] private Vector3 handOffset = new Vector3(0.3f, -0.3f, 0.5f);
    [Min(0.05f)] [SerializeField] private float handSize = 0.45f;

    [Tooltip("How far (metres per m/s of body speed) the creatures lag behind when the body speeds up or turns, " +
             "easing back at Inertia Rate. Small, so they sway like things hung on a belt rather than float.")]
    [Range(0f, 0.05f)] [SerializeField] private float inertia = 0.012f;
    [Min(0.5f)] [SerializeField] private float inertiaRate = 5f;

    [Header("Climbing")]
    [Tooltip("While hand climbing, mounts are pulled this far back so they never touch the wall.")]
    [Min(0f)] [SerializeField] private float climbTuck = 0.2f;

    /// <summary>True while the inventory view is open (and one frame after), so other player input stands still.</summary>
    public static bool ViewOpen => open || Time.frameCount <= closedFrame + 1;
    private static bool open;
    private static int closedFrame = -10;

    private const int Hand = -1;
    private readonly List<StowedCreature> stowed = new List<StowedCreature>();
    private StowedCreature hand;
    private PlayerCreatureCarrier carrier;
    private SmoothFirstPersonController controller;
    private HandClimber climber;
    private InventoryViewCamera view;
    private FuzzPackBeast viewBeast;
    private float tuck;
    private Vector3 sway;          // body-space lag from speeding up, shared by every part
    private Vector3 lastEye;
    private Rigidbody playerBody;
    private Renderer[] shellRenderers; // the player's own capsule: drawn round the body, hidden in the view
    private float lookYaw, lookPitch;
    private bool hoverValid;
    private Spot hover;
    private readonly List<Spot> spots = new List<Spot>();

    private struct Spot
    {
        public FuzzPackBeast beast;  // null = the player's body
        public int index;
    }

    /// <summary>What is on the body now (read only).</summary>
    public IReadOnlyList<StowedCreature> Stowed => stowed;
    public int MountCount => mounts.Length;
    public bool IsViewOpen => open;

    private void Awake()
    {
        carrier = GetComponent<PlayerCreatureCarrier>();
        if (carrier == null) carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        controller = GetComponentInParent<SmoothFirstPersonController>();
        if (controller == null) controller = FindFirstObjectByType<SmoothFirstPersonController>();
        playerBody = controller != null ? controller.GetComponent<Rigidbody>() : null;
        shellRenderers = controller != null ? controller.GetComponents<Renderer>() : new Renderer[0];
        climber = GetComponentInParent<HandClimber>();
        if (climber == null) climber = FindFirstObjectByType<HandClimber>();
    }

    // ---------------- Input ----------------

    private void Update()
    {
        Prune();
        if (carrier == null) return;
        Keyboard keys = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (open)
        {
            if (keys != null && (keys[viewKey].wasPressedThisFrame || keys.escapeKey.wasPressedThisFrame) ||
                mouse != null && mouse.rightButton.wasPressedThisFrame)
            { CloseView(); return; }
            UpdateHover(mouse);
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && hoverValid) Click(hover);
            return;
        }
        if (keys == null || !keys[viewKey].wasPressedThisFrame) return;
        if (BoneAwakeningCutscene.Playing || RestSpotSystem.BlocksInteraction || MeditationController.BlocksPlayerInput) return;
        if (!Application.isFocused && !Application.isBatchMode) return;
        if (carrier.PendingCreature != null || carrier.TentacleLockedForRide || BoneRide.Busy) return;
        if (controller != null && !controller.Grounded && (climber == null || !climber.IsClimbing)) return;
        OpenView();
    }

    // ---------------- View ----------------

    /// <summary>Opens the inventory view. Public so tests can drive it.</summary>
    public void OpenView()
    {
        if (open || carrier.CarryCamera == null) return;
        open = true;
        view ??= new InventoryViewCamera(carrier.CarryCamera);
        if (controller != null)
        {
            lookYaw = controller.LookYaw;
            lookPitch = controller.LookPitch;
            controller.SetFreeze(true);
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // What is in the hand becomes the hand spot of the view (a tentacle creature stays put on the back).
        Creature held = carrier.CarriedCreature;
        if (held != null && !carrier.CarriesTentacle)
        {
            carrier.Release();
            if (StowedCreature.CanStow(held)) hand = new StowedCreature(held, carrier, Hand, handSize, EyeRotation());
        }

        viewBeast = FuzzPackBeast.Nearest(carrier.transform.position, beastRange);
        FrameView();
        view.Open();
    }

    /// <summary>Closes the view: the player can move again and the hand takes what it holds. Public for tests.</summary>
    public void CloseView()
    {
        if (!open) return;
        open = false;
        closedFrame = Time.frameCount;
        if (controller != null) controller.SetFreeze(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (hand != null)
        {
            StowedCreature taken = hand;
            hand = null;
            taken.Restore();
            carrier.TryCarryFromInteraction(taken.Creature);
        }
        view?.Close();
        hoverValid = false;
    }

    private void FrameView()
    {
        Camera cam = carrier.CarryCamera;
        Vector3 chest = cam.transform.position + Vector3.down * 0.45f;
        Vector3 forward = BodyYaw() * Vector3.forward;
        Vector3 target = chest;
        float distance = viewDistance;
        float side = viewAngles.x;
        if (viewBeast != null)
        {
            Vector3 beast = viewBeast.transform.position;
            target = Vector3.Lerp(chest, beast, 0.45f);
            distance = Mathf.Max(viewDistance, Vector3.Distance(chest, beast) * 0.85f + 1.8f);
            // Stand on the side that keeps both the player and the beast in the picture.
            Vector3 toBeast = Vector3.ProjectOnPlane(beast - chest, Vector3.up);
            side = Vector3.Dot(toBeast, BodyYaw() * Vector3.right) > 0f ? -viewAngles.x - 20f : viewAngles.x + 20f;
        }
        Vector3 dir = Quaternion.AngleAxis(side, Vector3.up) * forward;
        dir = Quaternion.AngleAxis(-viewAngles.y, Vector3.Cross(Vector3.up, dir).normalized) * dir;
        distance = ClearDistance(target, dir, distance);
        Vector3 position = target + dir * distance;
        view.Frame(position, Quaternion.LookRotation(target - position, Vector3.up), Vector3.Distance(position, target));
    }

    private readonly RaycastHit[] viewHits = new RaycastHit[16];

    /// <summary>Pulls the view camera in when rock is between it and the player.</summary>
    private float ClearDistance(Vector3 from, Vector3 dir, float distance)
    {
        int count = Physics.SphereCastNonAlloc(from, 0.25f, dir, viewHits, distance, ~(1 << 2), QueryTriggerInteraction.Ignore);
        float clear = distance;
        for (int i = 0; i < count; i++)
        {
            Collider c = viewHits[i].collider;
            if (c == null || viewHits[i].distance <= 0f || c.transform.root == transform.root) continue;
            if (c.GetComponentInParent<Creature>() != null) continue;
            clear = Mathf.Min(clear, viewHits[i].distance);
        }
        return Mathf.Max(0.9f, clear - 0.1f);
    }

    private readonly HashSet<Renderer> occluders = new HashSet<Renderer>();
    private readonly List<Renderer> occluderScratch = new List<Renderer>();

    /// <summary>
    /// Creatures standing between the view camera and the player (or the beast) are hidden while the view shows,
    /// so nothing blocks the body. Everything comes back when the view closes.
    /// </summary>
    private void HideOccluders(bool showing)
    {
        occluderScratch.Clear();
        if (showing && view != null)
        {
            Vector3 from = view.Camera.transform.position;
            Vector3 chest = carrier.CarryCamera.transform.position + Vector3.down * 0.45f;
            Collect(from, chest);
            if (viewBeast != null) Collect(from, viewBeast.transform.position);
        }
        foreach (Renderer r in occluders)
            if (r != null && !occluderScratch.Contains(r)) r.forceRenderingOff = false;
        occluders.Clear();
        foreach (Renderer r in occluderScratch) { r.forceRenderingOff = true; occluders.Add(r); }
    }

    private readonly Collider[] nearCamera = new Collider[64];

    /// <summary>Every creature (not stowed, not the framed beast) nearer the camera than what is framed, in front of it.</summary>
    private void Collect(Vector3 from, Vector3 to)
    {
        float reach = Vector3.Distance(from, to) - 0.4f;
        if (reach <= 0f) return;
        Vector3 look = view.Camera.transform.forward;
        int count = Physics.OverlapSphereNonAlloc(from, reach, nearCamera, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Creature c = nearCamera[i] != null ? nearCamera[i].GetComponentInParent<Creature>() : null;
            if (c == null || c.IsCarried || (viewBeast != null && c == viewBeast.Fuzz)) continue;
            Vector3 closest = nearCamera[i].ClosestPoint(from);
            if (Vector3.Dot(closest - from, look) < -0.2f) continue; // behind the camera
            foreach (Renderer r in c.GetComponentsInChildren<Renderer>())
                if (!occluderScratch.Contains(r)) occluderScratch.Add(r);
        }
    }

    private void UpdateHover(Mouse mouse)
    {
        hoverValid = false;
        if (mouse == null || view == null || view.Blend < 0.9f) return;
        Vector2 cursor = mouse.position.ReadValue();
        float best = hoverPixels * Screen.height / 1080f;
        foreach (Spot s in spots)
        {
            Vector3 p = view.Camera.WorldToScreenPoint(SpotCentre(s));
            if (p.z <= 0f) continue;
            float d = Vector2.Distance(cursor, p);
            if (d < best) { best = d; hover = s; hoverValid = true; }
        }
    }

    /// <summary>Uses a spot as a click would: the hand and the spot swap what they hold. Public for tests.</summary>
    public void UseSpot(FuzzPackBeast beast, int index) => Click(new Spot { beast = beast, index = index });

    private void Click(Spot spot)
    {
        StowedCreature there = Get(spot);
        StowedCreature held = hand;
        if (there == null && held == null) return;
        if (held != null && spot.beast != null && !spot.beast.Accepts(held.Creature)) return;
        if (there != null) Remove(spot);
        hand = null;
        if (held != null) Place(spot, held);
        if (there != null) { there.MoveTo(Hand, handSize, EyeRotation()); hand = there; }
        AudioManager.PostEvent(held != null ? "Play_Drop" : "Play_PickUp", gameObject);
    }

    // ---------------- Spots ----------------

    private StowedCreature Get(Spot spot)
    {
        if (spot.beast != null) return spot.beast.At(spot.index);
        foreach (StowedCreature s in stowed) if (s.Slot == spot.index) return s;
        return null;
    }

    private void Remove(Spot spot)
    {
        if (spot.beast != null) { spot.beast.Take(spot.index); return; }
        stowed.RemoveAll(s => s.Slot == spot.index);
    }

    private void Place(Spot spot, StowedCreature item)
    {
        if (spot.beast != null) { spot.beast.Put(item, spot.index); return; }
        item.MoveTo(spot.index, mounts[spot.index].size, BodyYaw());
        stowed.Add(item);
    }

    private Vector3 SpotCentre(Spot spot)
    {
        if (spot.beast != null) return spot.beast.SpotCentre(spot.index);
        StowedCreature s = Get(spot);
        if (s != null) return s.Centre;
        return carrier.CarryCamera.transform.position + BodyYaw() * MountOffset(spot.index);
    }

    private float SpotRadius(Spot spot) => spot.beast != null ? spot.beast.SpotRadius : mounts[spot.index].size * 0.6f;

    private Vector3 MountOffset(int index) => mounts[index].offset + Vector3.back * (climbTuck * tuck);

    // ---------------- Living ----------------

    private void Prune()
    {
        // Something that died or was taken by something else falls off the body.
        for (int i = stowed.Count - 1; i >= 0; i--)
        {
            if (stowed[i].Valid) continue;
            stowed[i].Restore();
            stowed.RemoveAt(i);
        }
        if (hand != null && !hand.Valid) { hand.Restore(); hand = null; }
    }

    private void ShareHeat(float deltaTime)
    {
        if (heatShare <= 0f || deltaTime <= 0f) return;
        float k = 1f - Mathf.Exp(-heatShare * deltaTime);
        for (int a = 0; a < stowed.Count; a++)
        for (int b = a + 1; b < stowed.Count; b++)
        {
            StowedCreature x = stowed[a], y = stowed[b];
            if (x.Creature == null || y.Creature == null) continue;
            if (Vector3.Distance(mounts[x.Slot].offset, mounts[y.Slot].offset) > neighbourDistance) continue;
            HeatFlow.Share(x.Creature, y.Creature, k, maxHeatFlow * deltaTime);
        }
    }

    private Quaternion BodyYaw()
    {
        Camera cam = carrier != null ? carrier.CarryCamera : null;
        Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : transform.forward;
        return forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
    }

    private Quaternion EyeRotation() => carrier.CarryCamera != null ? carrier.CarryCamera.transform.rotation : transform.rotation;

    // After the camera has moved, so the mounts never trail the view.
    private void LateUpdate()
    {
        if (carrier == null || carrier.CarryCamera == null) return;
        bool showing = view != null && view.Step(Time.unscaledDeltaTime);
        foreach (Renderer r in shellRenderers) if (r != null) r.forceRenderingOff = showing;
        HideOccluders(showing);
        if (open)
        {
            // The mouse moves the cursor, not the eyes; the view follows the player and the beast as they move.
            if (controller != null) controller.SetLook(lookYaw, lookPitch);
            if (viewBeast != null && (viewBeast.Fuzz == null || viewBeast.Fuzz.IsDead)) viewBeast = null;
            FrameView();
        }
        float dt = Time.deltaTime;
        ShareHeat(Time.deltaTime);
        tuck = Mathf.MoveTowards(tuck, climber != null && climber.IsClimbing ? 1f : 0f, dt * 3f);

        Vector3 eye = carrier.CarryCamera.transform.position;
        Quaternion yaw = BodyYaw();
        // Inertia: speeding up leaves the creatures a little behind, then they ease back (never further than 8 cm).
        Vector3 velocity = playerBody != null ? playerBody.linearVelocity : (dt > 0f ? (eye - lastEye) / dt : Vector3.zero);
        lastEye = eye;
        Vector3 wantSway = Vector3.ClampMagnitude(Quaternion.Inverse(yaw) * -velocity * inertia, 0.08f);
        sway = Vector3.Lerp(sway, wantSway, 1f - Mathf.Exp(-inertiaRate * dt));
        bool hide = !showing && !showOnBodyWhileWalking;
        foreach (StowedCreature s in stowed)
        {
            if (s.Creature == null) continue;
            Mount m = mounts[Mathf.Clamp(s.Slot, 0, mounts.Length - 1)];
            float grow = hoverValid && hover.beast == null && hover.index == s.Slot ? 1.18f : 1f;
            s.Ride(eye, yaw, MountOffset(s.Slot) + sway, Quaternion.Euler(m.tilt), m.swingRate, 0f, dt, grow);
            s.SetHidden(hide && s.Arrived);
        }
        if (hand != null) hand.Ride(eye, EyeRotation(), handOffset, Quaternion.identity, 30f, 0f, dt);

        if (!showing) return;
        // Rings on every spot: dim when empty, bright under the cursor.
        spots.Clear();
        for (int i = 0; i < mounts.Length; i++) spots.Add(new Spot { index = i });
        if (viewBeast != null && open)
            for (int i = 0; i < viewBeast.SpotCount; i++) spots.Add(new Spot { beast = viewBeast, index = i });
        for (int i = 0; i < spots.Count; i++)
        {
            Spot s = spots[i];
            bool hovered = hoverValid && hover.beast == s.beast && hover.index == s.index;
            float brightness = hovered ? 1f : Get(s) == null ? 0.5f : 0.2f;
            view.Ring(i, SpotCentre(s), SpotRadius(s) * (hovered ? 1.15f : 1f), brightness);
        }
        view.ShowRings(spots.Count);
    }

    private void OnDisable()
    {
        if (open) CloseView();
        foreach (StowedCreature s in stowed) s.Restore();
        stowed.Clear();
        if (hand != null) { hand.Restore(); hand = null; }
    }

    private void OnDestroy() => view?.Destroy();
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
