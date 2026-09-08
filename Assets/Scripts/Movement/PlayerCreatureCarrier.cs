using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the player. Holds any Creature at an anchor without disabling its
/// simulation, colliders, animation, or heat reactions. Hold right click to carry.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class PlayerCreatureCarrier : MonoBehaviour
{
    [SerializeField] private Camera carryCamera;
    [Tooltip("Empty transform in front of the camera. Created automatically if unassigned.")]
    [SerializeField] private Transform carryPoint;
    [Min(0f)] [SerializeField] private float pickupRange = 5f;
    [SerializeField] private LayerMask pickupLayers = ~0;

    public Creature CarriedCreature { get; private set; }

    private void Awake()
    {
        if (carryCamera == null) carryCamera = Camera.main;
        if (carryPoint == null && carryCamera != null)
        {
            carryPoint = new GameObject("CreatureCarryPoint").transform;
            carryPoint.SetParent(carryCamera.transform, false);
            carryPoint.localPosition = new Vector3(0f, -0.35f, 2.5f);
        }
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !Application.isFocused)
        {
            Release();
            return;
        }

        if (mouse.rightButton.wasPressedThisFrame && CarriedCreature == null)
        {
            Creature creature = FindCreature();
            if (creature != null) BeginCarry(creature);
        }

        if (!mouse.rightButton.isPressed) Release();
        if (CarriedCreature == null || !CarriedCreature.isActiveAndEnabled)
            Release();
    }

    private Creature FindCreature()
    {
        if (carryCamera == null) return null;
        Ray ray = carryCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        // Ignore heat-volume triggers: pick the visible creature's solid body.
        if (Physics.Raycast(ray, out RaycastHit hit, pickupRange, pickupLayers, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<Creature>();
        return null;
    }

    private void BeginCarry(Creature creature)
    {
        if (carryPoint == null || !creature.TryBeginCarry(this, carryPoint)) return;
        CarriedCreature = creature;
    }

    private void FixedUpdate()
    {
        if (CarriedCreature == null) return;
        if (carryPoint == null || !CarriedCreature.PullTowards(this, carryPoint)) Release();
    }

    public void Release()
    {
        if (CarriedCreature != null) CarriedCreature.EndCarry(this);
        CarriedCreature = null;
    }

    private void OnDisable() => Release();
    private void OnApplicationFocus(bool focused) { if (!focused) Release(); }
}
