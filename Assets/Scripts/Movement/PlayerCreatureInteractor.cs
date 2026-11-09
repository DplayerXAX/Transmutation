
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Sends an interaction to the Creature under the centre-screen cursor.
/// Attach this component to the Player or its Camera holder.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1100)]
public sealed class PlayerCreatureInteractor : MonoBehaviour
{
    [Header("Interaction")]
    [Tooltip("Camera used for interaction. Camera.main is used when this is left empty.")]
    [SerializeField] private Camera interactionCamera;

    [Tooltip("Maximum distance at which the player can interact with a Creature.")]
    [Min(0f)]
    [SerializeField] private float interactionRange = 5f;

    [Tooltip("Physics layers that can be hit by the interaction ray.")]
    [SerializeField] private LayerMask interactionLayers = ~0;

    [SerializeField] private PlayerCreatureCarrier carrier;

    // Aria: Squirrel hold interaction
    private Squirrel squeezedSquirrel;

    // Daniel: Delayed interaction after pickup
    private Creature pendingHeldUse;
    private float pendingHeldUseUntil;
    [SerializeField, Min(0.1f)] private float honeyCoatHoldTime = 0.45f;
    private GlowHoney pressedHoney;
    private float honeyHeldTime;
    private bool honeyGesture;
    private RaycastHit[] surfaceHits = new RaycastHit[32];

    private void Awake()
    {
        if (carrier == null)
            carrier = GetComponentInParent<PlayerCreatureCarrier>();

        if (carrier == null)
            carrier = transform.root.GetComponentInChildren<PlayerCreatureCarrier>(true);

        if (interactionCamera == null)
            interactionCamera = Camera.main;
    }

    private void Update()
    {
        var bugGrip = carrier != null && carrier.PlayerController != null ? carrier.PlayerController.GetComponent<PlayerBugGrip>() : null;
        if (bugGrip != null && bugGrip.ConsumesInput) { pendingHeldUse = null; UpdateHeldSqueeze(false); return; }
        // Daniel: Disable interaction during cutscenes,
        // bone riding, or when the application loses focus.
        // A mount without tentacle reins leaves left click to a held creature (a fire source lures it on).
        bool ridingReined = BoneRide.Mount != null &&
                            (BoneRide.MountRoot == null || BoneRide.Reins(BoneRide.MountRoot) >= 1 || !HeldHasOwnUse());
        if (BoneAwakeningCutscene.Playing ||
            ridingReined ||
            !Application.isFocused)
        {
            pendingHeldUse = null;
            CancelHoneyGesture();
            UpdateHeldSqueeze(false);
            return;
        }

        // Daniel: Resolve delayed interactions after pickup.
        ResolvePendingHeldUse();

        bool leftHeld = Mouse.current != null &&
                        Mouse.current.leftButton.isPressed;

        // Aria: A carried squirrel consumes left mouse input.
        if (UpdateHeldSqueeze(leftHeld))
            return;

        if (UpdateHoneyGesture(Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame,
            leftHeld, Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame, Time.deltaTime))
            return;

        if (Mouse.current == null ||
            !Mouse.current.leftButton.wasPressedThisFrame)
            return;

        // Left click next to a bone creature climbs on (BoneRide), not an interaction.
        if (BoneRide.Approaching || (BoneRide.Mount == null && !HeldHasOwnUse() && BoneRide.TryNearby(out _, out _)))
            return;

        // Aria: Tentacle uses left mouse for its own actions.
        if (carrier != null && carrier.CarriesTentacle)
            return;

        // Both branches: Avoid conflicting with throwable input.
        // A held creature with its own left-click use (bubble flower, goo) still gets the click.
        if (CreatureThrower.Instance != null &&
            CreatureThrower.Instance.HoldingThrowable &&
            !HeldHasOwnUse())
            return;

        AudioManager.PostEvent("Play_LMB", gameObject);
        TryInteract();
    }

    /// <summary>
    /// Held squirrel use repeats until release;
    /// other creatures retain click interactions.
    /// </summary>
    public bool UpdateHeldSqueeze(bool pressed)
    {
        Squirrel held = carrier != null
            ? carrier.CarriedCreature as Squirrel
            : null;

        if (held != null && held.IsDead)
            held = null;

        if (squeezedSquirrel != null &&
            squeezedSquirrel != held)
        {
            squeezedSquirrel.SetSqueezeHeld(false, default);
        }

        squeezedSquirrel = held;

        if (held == null)
            return false;

        Vector3 direction = interactionCamera != null
            ? interactionCamera.transform.forward
            : transform.forward;

        held.SetSqueezeHeld(
            pressed,
            new CreatureInteraction(
                gameObject,
                held.transform.position,
                direction
            )
        );

        return true;
    }

    private bool HeldHasOwnUse() =>
        carrier != null &&
        carrier.CarriedCreature != null &&
        !carrier.CarriedCreature.IsDead &&
        carrier.CarriedCreature.CanUseWhileCarried;

    // Delay ground honey's click until release so a long press never drinks first.
    internal bool UpdateHoneyGesture(bool pressed, bool held, bool released, float deltaTime)
    {
        if (pressed && !honeyGesture && carrier != null && carrier.CarriedCreature == null &&
            carrier.PendingCreature == null && !BoneRide.Busy &&
            !BoneRide.TryNearby(out _, out _) && FindCreature() is GlowHoney honey && !honey.IsCarried)
        {
            pressedHoney = honey;
            honeyHeldTime = 0f;
            honeyGesture = true;
        }
        if (!honeyGesture) return false;
        if (pressedHoney != null)
        {
            if (carrier == null || carrier.CarriedCreature != null || carrier.PendingCreature != null ||
                pressedHoney.IsCarried || pressedHoney.IsDead || FindCreature() != pressedHoney)
                pressedHoney = null;
            else
            {
                honeyHeldTime += Mathf.Max(0f, deltaTime);
                if (honeyHeldTime >= honeyCoatHoldTime)
                {
                    pressedHoney.TryUse(GlowHoney.Use.Coat, carrier);
                    pressedHoney = null;
                }
                else if (released)
                    pressedHoney.TryUse(GlowHoney.Use.Drink, carrier);
            }
        }
        if (released || !held) CancelHoneyGesture();
        return true;
    }

    private void CancelHoneyGesture()
    {
        pressedHoney = null;
        honeyHeldTime = 0f;
        honeyGesture = false;
    }

    private void OnDisable()
    {
        if (squeezedSquirrel != null)
            squeezedSquirrel.SetSqueezeHeld(false, default);

        squeezedSquirrel = null;
        pendingHeldUse = null;
        CancelHoneyGesture();
    }

    /// <summary>
    /// Resolves a click buffered while a creature
    /// is being picked up.
    /// </summary>
    private void ResolvePendingHeldUse()
    {
        if (pendingHeldUse == null)
            return;

        Creature pending = pendingHeldUse;

        if (Time.time > pendingHeldUseUntil ||
            carrier == null ||
            (carrier.CarriedCreature != pending &&
             carrier.PendingCreature != pending))
        {
            pendingHeldUse = null;
        }
        else if (carrier.CarriedCreature == pending)
        {
            pendingHeldUse = null;

            // Prevent delayed use from interfering
            // with squirrel or tentacle input.
            if (carrier.CarriesTentacle ||
                carrier.CarriedCreature is Squirrel ||
                (CreatureThrower.Instance != null &&
                 CreatureThrower.Instance.HoldingThrowable &&
                 !HeldHasOwnUse()))
                return;

            TryInteract();
        }
    }

    /// <summary>Nearest solid under the crosshair, including non-creatures. Never reaches through an obstruction.</summary>
    public bool TryGetSurfaceHit(out RaycastHit hit)
    {
        hit = default;
        if (interactionCamera == null) return false;
        Ray ray = interactionCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        var physics = gameObject.scene.GetPhysicsScene();
        int count;
        while ((count = physics.Raycast(ray.origin, ray.direction, surfaceHits, interactionRange,
                   interactionLayers, QueryTriggerInteraction.Ignore)) == surfaceHits.Length)
            System.Array.Resize(ref surfaceHits, surfaceHits.Length * 2);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider candidate = surfaceHits[i].collider;
            if (candidate.GetComponentInParent<SmoothFirstPersonController>() != null ||
                (carrier != null && carrier.CarriedCreature != null &&
                 candidate.transform.IsChildOf(carrier.CarriedCreature.transform))) continue;
            if (surfaceHits[i].distance >= nearest) continue;
            hit = surfaceHits[i];
            nearest = hit.distance;
        }
        return hit.collider != null;
    }

    /// <summary>Uses the held creature, or interacts with the first creature under the crosshair.</summary>
    public void TryInteract()
    {
        if (interactionCamera == null)
            return;

        if (carrier != null && carrier.CarriesTentacle)
            return;

        if (carrier != null &&
            carrier.CarriedCreature == null &&
            carrier.PendingCreature != null &&
            carrier.PendingCreature.CanUseWhileCarried)
        {
            // Keep the click while the reaching hand
            // finishes picking up the creature.
            pendingHeldUse = carrier.PendingCreature;
            pendingHeldUseUntil = Time.time + 0.3f;
            return;
        }

        Creature held = carrier != null
            ? carrier.CarriedCreature
            : null;

        if (held != null &&
            !held.IsDead &&
            held.CanUseWhileCarried)
        {
            Ray aim = interactionCamera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f, 0f)
            );

            if (held is GlowHoney honey)
            {
                Creature target = FindCreature(out RaycastHit coatHit, out _);
                if (coatHit.collider != null)
                {
                    GameObject owner = target != null ? target.gameObject
                        : coatHit.rigidbody != null ? coatHit.rigidbody.gameObject : coatHit.collider.gameObject;
                    honey.TryCoat(owner, coatHit.point + coatHit.normal * 0.05f);
                }
                return;
            }

            held.ReceiveInteraction(
                new CreatureInteraction(
                    gameObject,
                    aim.GetPoint(interactionRange),
                    aim.direction
                )
            );

            return;
        }

        Creature creature = FindCreature(
            out RaycastHit hit,
            out Ray ray
        );

        SkateShoesPickup shoes = FindSkateShoes();
        if (shoes != null)
        {
            var player = carrier != null ? carrier.PlayerController : GetComponentInParent<SmoothFirstPersonController>();
            shoes.TryEquip(player);
            return;
        }

        if (creature == null)
            return;

        CreatureInteraction interaction =
            new CreatureInteraction(
                gameObject,
                hit.point,
                ray.direction
            );

        creature.ReceiveInteraction(interaction);
    }

    /// <summary>
    /// The Creature a left-click would interact with now,
    /// or null.
    /// </summary>
    public Creature FindCreature()
    {
        return FindCreature(out _, out _);
    }

    public SkateShoesPickup FindSkateShoes()
    {
        if (interactionCamera == null || (carrier != null &&
            (carrier.CarriedCreature != null || carrier.PendingCreature != null))) return null;
        Ray ray = interactionCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        CreatureAim.Find(gameObject.scene.GetPhysicsScene(), ray, interactionRange,
            interactionLayers, carrier, out RaycastHit hit);
        return hit.collider != null ? hit.collider.GetComponentInParent<SkateShoesPickup>() : null;
    }

    private Creature FindCreature(
        out RaycastHit hit,
        out Ray ray)
    {
        hit = default;
        ray = default;

        if (interactionCamera == null)
            return null;

        ray = interactionCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f)
        );

        Creature creature = CreatureAim.Find(
            gameObject.scene.GetPhysicsScene(),
            ray,
            interactionRange,
            interactionLayers,
            carrier,
            out hit
        );

        return creature != null && !creature.IsDead
            ? creature
            : null;
    }
}

/// <summary>
/// Shared picking for clicks and holds:
/// ignore sensor volumes, retain solid occlusion,
/// forgive small aim errors.
/// </summary>
internal static class CreatureAim
{
    // Width of the forgiving second cast (metres): wide enough for flowers and small creatures, still exact enough to choose.
    private const float AimRadius = 0.3f;

    public static Creature Find(
        PhysicsScene scene,
        Ray ray,
        float range,
        int layers,
        PlayerCreatureCarrier carrier,
        out RaycastHit hit,
        float aimRadius = AimRadius)
    {
        hit = default;

        RaycastHit[] results = selectionHits;

        int count = scene.Raycast(
            ray.origin,
            ray.direction,
            results,
            range,
            layers,
            QueryTriggerInteraction.Ignore
        );

        float wall = range;
        Creature direct = null;

        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = results[i];

            if (Ignore(candidate.collider, carrier))
                continue;

            if (candidate.distance >= wall)
                continue;

            wall = candidate.distance;
            hit = candidate;

            direct = candidate.collider
                .GetComponentInParent<Creature>();
        }

        if (direct != null)
            return direct;

        count = scene.SphereCast(
            ray.origin,
            aimRadius,
            ray.direction,
            results,
            wall,
            layers,
            QueryTriggerInteraction.Ignore
        );

        float best = float.PositiveInfinity;
        Creature picked = null;

        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = results[i];

            if (Ignore(candidate.collider, carrier) ||
                candidate.distance >= best)
                continue;

            Creature creature = candidate.collider
                .GetComponentInParent<Creature>();

            if (creature == null)
                continue;

            // The wider selection must not reach around a wall.
            Vector3 delta = candidate.point - ray.origin;

            int blockers = scene.Raycast(
                ray.origin,
                delta.normalized,
                resultsForVisibility,
                delta.magnitude,
                layers,
                QueryTriggerInteraction.Ignore
            );

            bool blocked = false;

            for (int b = 0; b < blockers; b++)
            {
                if (!Ignore(resultsForVisibility[b].collider, carrier) &&
                    resultsForVisibility[b].collider
                        .GetComponentInParent<Creature>() != creature)
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked)
                continue;

            picked = creature;
            best = candidate.distance;
            hit = candidate;
        }

        return picked;
    }

    private static readonly RaycastHit[] selectionHits =
        new RaycastHit[128];

    private static readonly RaycastHit[] resultsForVisibility =
        new RaycastHit[128];

    private static bool Ignore(
        Collider collider,
        PlayerCreatureCarrier carrier) =>
        collider.GetComponentInParent<SmoothFirstPersonController>() != null ||
        (carrier != null &&
         carrier.CarriedCreature != null &&
         collider.transform.IsChildOf(
             carrier.CarriedCreature.transform
         ));
}
