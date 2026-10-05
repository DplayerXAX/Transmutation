using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Sends an interaction to the Creature under the centre-screen cursor.
/// Attach this component to the Player or its Camera holder.
/// </summary>
[DisallowMultipleComponent]
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

    /// <summary>Finds the main Camera when no Camera has been assigned manually.</summary>
    private void Awake()
    {
        if (interactionCamera == null)
        {
            interactionCamera = Camera.main;
        }
    }

    /// <summary>Checks for one left-click press and attempts a centre-screen interaction.</summary>
    private void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            TryInteract();
        }
    }

    /// <summary>Raycasts through screen centre and notifies the first Creature hit.</summary>
    public void TryInteract()
    {
        Creature creature = FindCreature(out RaycastHit hit, out Ray ray);

        if (creature == null)
        {
            return;
        }

        CreatureInteraction interaction = new CreatureInteraction(
            gameObject,
            hit.point,
            ray.direction
        );

        creature.ReceiveInteraction(interaction);
    }

    /// <summary>The Creature a left-click would interact with now, or null.</summary>
    public Creature FindCreature()
    {
        return FindCreature(out _, out _);
    }

    private Creature FindCreature(out RaycastHit hit, out Ray ray)
    {
        hit = default;
        ray = default;

        if (interactionCamera == null)
        {
            return null;
        }

        ray = interactionCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (!Physics.Raycast(
                ray,
                out hit,
                interactionRange,
                interactionLayers,
                QueryTriggerInteraction.Collide))
        {
            return null;
        }

        return hit.collider.GetComponentInParent<Creature>();
    }
}
