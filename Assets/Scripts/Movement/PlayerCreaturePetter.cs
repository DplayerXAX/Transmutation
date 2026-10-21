using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

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

    [Tooltip("A tentacle creature held with the left hand is dragged along, kept this far in front of the eyes.")]
    [Min(0.3f)] [SerializeField] private float dragDistance = 1.4f;

    private PlayerCreatureCarrier carrier;
    private float heldFor;
    private float lostFor;
    private Creature pressedTarget;
    private RaycastHit pressedHit;

    public Creature Petted { get; private set; }
    public bool IsPetting => Petted != null;
    /// <summary>Where the hand touches the creature, and the surface direction there.</summary>
    private Vector3 touchPoint;
    private Vector3 touchNormal = Vector3.up;
    public bool DraggingBody => Petted is TentacleCreature tentacle && tentacle.Dragged;
    public Vector3 DragTarget { get; private set; }
    public Vector3 TouchPoint
    {
        get => DraggingBody && viewCamera != null ? ((TentacleCreature)Petted).BodyGrip(viewCamera.transform.position) : touchPoint;
        private set => touchPoint = value;
    }
    public Vector3 TouchNormal
    {
        get => DraggingBody && viewCamera != null ? (viewCamera.transform.position - Petted.transform.position).normalized : touchNormal;
        private set => touchNormal = value;
    }

    private void Awake()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
    }

    private void Update()
    {
        if (BoneAwakeningCutscene.Playing) { Stop(); return; }
        if (Petted != null && Petted.IsDead) Stop();
        Mouse mouse = Mouse.current;
        bool held = mouse != null && mouse.leftButton.isPressed && Application.isFocused && BoneRide.Mount == null
            && !(CreatureThrower.Instance != null && CreatureThrower.Instance.HoldingThrowable)
            && !(carrier != null && carrier.CarriesTentacle)
            && !(carrier != null && carrier.CarriedCreature != null && carrier.CarriedCreature.CanUseWhileCarried);
        StepHold(held, Time.deltaTime, mouse != null ? mouse.scroll.ReadValue().y : 0f);
    }

    private void StepHold(bool held, float deltaTime, float scroll)
    {
        if (held && heldFor == 0f) pressedTarget = FindPettable(out pressedHit);
        heldFor = held ? heldFor + deltaTime : 0f;
        if (!held) { pressedTarget = null; Stop(); return; }

        // Lock a tentacle from the initial press. Looking away or crossing another creature never transfers the hold.
        if (Petted is TentacleCreature dragged)
        {
            if (!dragged.isActiveAndEnabled || dragged.IsCarried) { Stop(); return; }
            dragDistance = Mathf.Clamp(dragDistance + scroll * 0.0025f, 0.7f, 3f);
            DragTarget = viewCamera.transform.position + viewCamera.transform.forward * dragDistance;
            dragged.Dragged = true;
            dragged.ContinuePet(DragTarget, TouchNormal, deltaTime);
            return;
        }

        RaycastHit hit = default;
        Creature target = null;
        if (heldFor >= holdDelay)
        {
            if (pressedTarget is TentacleCreature && pressedTarget.isActiveAndEnabled && !pressedTarget.IsCarried)
            { target = pressedTarget; hit = pressedHit; }
            else target = FindPettable(out hit);
        }
        if (target != null)
        {
            lostFor = 0f;
            TouchPoint = hit.point;
            TouchNormal = hit.normal;
            if (target != Petted)
            {
                Stop();
                Petted = target;
                if (Petted is TentacleCreature tentacle)
                {
                    // Set this before BeginPet so its petting reaction cannot grab an arm instead of the body.
                    tentacle.Dragged = true;
                    DragTarget = tentacle.transform.position;
                    dragDistance = Mathf.Clamp(Vector3.Distance(viewCamera.transform.position, tentacle.transform.position), 0.7f, 3f);
                    Petted.BeginPet(DragTarget, TouchNormal);
                }
                else Petted.BeginPet(TouchPoint, TouchNormal);
            }
            else Petted.ContinuePet(TouchPoint, TouchNormal, deltaTime);
            return;
        }

        if (Petted == null) return;
        // Brief slips (the creature moving under the hand) do not end it; letting go does.
        lostFor += deltaTime;
        if (!held || lostFor > loseGrace || !Petted.isActiveAndEnabled) Stop();
        else Petted.ContinuePet(TouchPoint, TouchNormal, deltaTime);
    }

    /// <summary>The creature in the middle of the view that the left hand could pet now, or null.</summary>
    public Creature FindPettable() => FindPettable(out _);

    private Creature FindPettable(out RaycastHit hit)
    {
        hit = default;
        if (viewCamera == null) return null;
        Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Creature creature = CreatureAim.Find(gameObject.scene.GetPhysicsScene(), ray, petRange, petLayers, carrier, out hit);
        // The creature already held in the right hand is not petted.
        if (creature == null || creature.IsDead || (carrier != null && carrier.CarriedCreature == creature)) return null;
        return creature;
    }

    private void Stop()
    {
        if (Petted is TentacleCreature dragged) dragged.Dragged = false;
        if (Petted != null) Petted.EndPet();
        Petted = null;
    }

    private void OnDisable() => Stop();
}
