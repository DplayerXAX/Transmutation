
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
        // Daniel: Disable interaction during cutscenes,
        // bone riding, or when the application loses focus.
        if (BoneAwakeningCutscene.Playing ||
            BoneRide.Mount != null ||
            !Application.isFocused)
        {
            pendingHeldUse = null;
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

        if (Mouse.current == null ||
            !Mouse.current.leftButton.wasPressedThisFrame)
            return;

        // Aria: Tentacle uses left mouse for its own actions.
        if (carrier != null && carrier.CarriesTentacle)
            return;

        // Both branches: Avoid conflicting with throwable input.
        if (CreatureThrower.Instance != null &&
            CreatureThrower.Instance.HoldingThrowable)
            return;

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

    private void OnDisable()
    {
        if (squeezedSquirrel != null)
            squeezedSquirrel.SetSqueezeHeld(false, default);

        squeezedSquirrel = null;
        pendingHeldUse = null;
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
                 CreatureThrower.Instance.HoldingThrowable))
                return;

            TryInteract();
        }
    }

    /// <summary>
    /// Raycasts through screen centre and notifies
    /// the first Creature hit.
    /// </summary>
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
    public static Creature Find(
        PhysicsScene scene,
        Ray ray,
        float range,
        int layers,
        PlayerCreatureCarrier carrier,
        out RaycastHit hit)
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
            0.18f,
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
