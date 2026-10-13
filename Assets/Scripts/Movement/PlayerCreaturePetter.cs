using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Left-hand petting. Hold the left mouse button on a creature within arm's reach: the left hand
/// reaches out and strokes it, and the creature is told through Creature.BeginPet / ContinuePet /
/// EndPet so it can react. A quick click still does the normal left-click interaction.
/// Added automatically by FirstPersonBody when missing.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerCreaturePetter : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;
    [Tooltip("How far from the eyes a creature can be petted (about arm's reach).")]
    [Min(0.3f)] [SerializeField] private float petRange = 1.7f;
    [Tooltip("Seconds the button must be held before a click becomes petting.")]
    [Min(0f)] [SerializeField] private float holdDelay = 0.2f;
    [Tooltip("Seconds the view may slip off the creature before petting stops.")]
    [Min(0f)] [SerializeField] private float loseGrace = 0.3f;
    [SerializeField] private LayerMask petLayers = ~0;

    private PlayerCreatureCarrier carrier;
    private float heldFor;
    private float lostFor;

    public Creature Petted { get; private set; }
    public bool IsPetting => Petted != null;
    /// <summary>Where the hand touches the creature, and the surface direction there.</summary>
    public Vector3 TouchPoint { get; private set; }
    public Vector3 TouchNormal { get; private set; } = Vector3.up;

    private void Awake()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        bool held = mouse != null && mouse.leftButton.isPressed && Application.isFocused;
        heldFor = held ? heldFor + Time.deltaTime : 0f;

        RaycastHit hit = default;
        Creature target = held && heldFor >= holdDelay ? FindPettable(out hit) : null;
        if (target != null)
        {
            lostFor = 0f;
            TouchPoint = hit.point;
            TouchNormal = hit.normal;
            if (target != Petted)
            {
                Stop();
                Petted = target;
                Petted.BeginPet(TouchPoint, TouchNormal);
            }
            else Petted.ContinuePet(TouchPoint, TouchNormal, Time.deltaTime);
            return;
        }

        if (Petted == null) return;
        // Brief slips (the creature moving under the hand) do not end it; letting go does.
        lostFor += Time.deltaTime;
        if (!held || lostFor > loseGrace || !Petted.isActiveAndEnabled) Stop();
        else Petted.ContinuePet(TouchPoint, TouchNormal, Time.deltaTime);
    }

    /// <summary>The creature in the middle of the view that the left hand could pet now, or null.</summary>
    public Creature FindPettable() => FindPettable(out _);

    private Creature FindPettable(out RaycastHit hit)
    {
        hit = default;
        if (viewCamera == null) return null;
        Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out hit, petRange, petLayers, QueryTriggerInteraction.Ignore)) return null;
        Creature creature = hit.collider.GetComponentInParent<Creature>();
        // The creature already held in the right hand is not petted.
        if (creature == null || (carrier != null && carrier.CarriedCreature == creature)) return null;
        return creature;
    }

    private void Stop()
    {
        if (Petted != null) Petted.EndPet();
        Petted = null;
    }

    private void OnDisable() => Stop();
}
