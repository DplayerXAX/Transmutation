using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Drawing and exhibit fixtures use the same click as normal creature interaction.</summary>
[DefaultExecutionOrder(-40)]
[DisallowMultipleComponent]
public sealed class ReflectionVisitor : MonoBehaviour
{
    [SerializeField] private float reach = 3.5f;
    [SerializeField] private float brushPixels = 3.2f;
    [SerializeField] private float eraserPixels = 14f;
    [SerializeField] private float focusTime = 0.6f;
    private MeditationHUD hud;
    private SmoothFirstPersonController controller;
    private Transform cameraHolder;
    private Camera viewCamera;
    private InkSurface focusSurface;
    private SlabJournal focusJournal;
    private bool openJournal, suppressRightInk;
    private SlabJournalButton.Action initialTool;
    private readonly List<Renderer> hiddenPlayerVisuals = new();
    private Vector3 focusPosition, normalPosition;
    private Quaternion focusRotation, normalRotation;
    private float focusBlend;
    private bool active, focusLeaving, hasLastUv;
    private Vector2 lastUv, lastMouse;
    private static int ConsumedFrame = -1;
    public static bool ConsumedClick => ConsumedFrame == Time.frameCount;
    public bool IsDrawing => focusSurface != null;

    public void Begin(MeditationHUD display, SmoothFirstPersonController movement, Transform holder, Camera view)
    {
        hud = display;
        controller = movement;
        cameraHolder = holder;
        viewCamera = view;
        active = true;
    }

    public void End()
    {
        EndFocusNow();
        active = false;
        hud.SetHint(null, null);
    }

    private void Update()
    {
        if (!active || GetComponent<MeditationController>().Transitioning) return;
        if (IsDrawing) { UpdateJournal(); return; }
        if (RestSpotSystem.BlocksInteraction || Mouse.current == null) { hud.SetHint(null, null); return; }
        var carrier = controller.GetComponentInChildren<PlayerCreatureCarrier>();
        if (carrier != null && (carrier.CarriedCreature != null || carrier.PendingCreature != null))
        { hud.SetHint(null, null); return; }

        Ray aim = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        var hits = ReflectionPhysics.RaycastAll(aim.origin, aim.direction, reach, ~(1 << 2), QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(controller.transform.root)) continue;
            var control = hit.collider.GetComponent<SlabJournalButton>();
            if (control != null)
            {
                hud.SetHint(null, "[LMB] " + control.Command);
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    ConsumedFrame = Time.frameCount;
                    switch (control.Command)
                    {
                        case SlabJournalButton.Action.Previous: control.Journal.Turn(-1); break;
                        case SlabJournalButton.Action.Next: control.Journal.Turn(1); break;
                        default: StartFocus(control.Journal.GetComponent<InkSurface>(), control.Command); break;
                    }
                }
                return;
            }
            var ink = hit.collider.GetComponent<InkSurface>();
            var beam = hit.collider.GetComponentInParent<ReflectionSpotlight>();
            var creature = hit.collider.GetComponentInParent<Creature>();
            if (beam == null && creature != null) beam = ReflectionSpotlight.ForFrozen(creature);
            if (ink != null || beam != null)
            {
                hud.SetHint(null, ink != null ? "[LMB] Open journal" : beam.IsLit ? "[LMB] Release exhibit" : "[LMB] Turn on light");
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    ConsumedFrame = Time.frameCount;
                    if (ink != null) StartFocus(ink);
                    else beam.Toggle();
                }
                return;
            }
            break;
        }
        hud.SetHint(null, null);
    }

    private void LateUpdate()
    {
        if (IsDrawing) UpdateFocusCamera();
    }

    private void StartFocus(InkSurface surface, SlabJournalButton.Action tool = SlabJournalButton.Action.Type)
    {
        normalPosition = cameraHolder.position;
        normalRotation = cameraHolder.rotation;
        controller.SetRestricted(true);
        controller.enabled = false;
        focusSurface = surface;
        focusJournal = surface.GetComponent<SlabJournal>();
        openJournal = false;
        initialTool = tool;
        suppressRightInk = false;
        HidePlayerVisuals(controller.transform.root);
        var firstPerson = FindFirstObjectByType<FirstPersonBody>();
        if (firstPerson != null) HidePlayerVisuals(firstPerson.VisualRoot);
        focusBlend = 0f;
        focusLeaving = false;
        hasLastUv = false;

        // Stand back far enough to see the whole face.
        Transform face = surface.transform;
        Bounds bounds = surface.Collider.bounds;
        float halfFov = viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float fitHeight = surface.Size.y * 0.5f * 1.2f / Mathf.Tan(halfFov);
        float fitWidth = surface.Size.x * 0.5f * 1.2f / (Mathf.Tan(halfFov) * viewCamera.aspect);
        float distance = Mathf.Min(14f, Mathf.Max(fitHeight, fitWidth, 0.6f));
        Vector3 normal = face.forward;
        focusPosition = bounds.center + normal * distance;
        focusRotation = Quaternion.LookRotation(-normal, Vector3.up);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        hud?.SetHint(null, null);
    }

    private void UpdateJournal()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (focusLeaving) return;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (focusJournal != null && focusJournal.CancelSticker()) return;
            LeaveFocus();
            return;
        }
        if (!openJournal && focusBlend >= 1f)
        {
            focusJournal?.BeginEditing(initialTool, viewCamera);
            openJournal = true;
            hud?.SetCorner("[Esc] Step back");
        }
        if (mouse != null && !mouse.rightButton.isPressed) suppressRightInk = false;
        if (openJournal && focusJournal != null && mouse?.rightButton.wasPressedThisFrame == true &&
            focusJournal.DiscardHeldSticker())
        {
            suppressRightInk = true;
            hasLastUv = false;
            return;
        }
        if (mouse == null || !openJournal || focusJournal == null ||
            !focusJournal.AcceptsInk(mouse.position.ReadValue()))
        {
            hasLastUv = false;
            return;
        }

        bool drawing = mouse.leftButton.isPressed;
        bool erasing = (!suppressRightInk && mouse.rightButton.isPressed) || (drawing && focusJournal.Erasing);
        Vector2 mousePosition = mouse.position.ReadValue();
        if (!drawing && !erasing)
        {
            hasLastUv = false;
            lastMouse = mousePosition;
            return;
        }

        Ray ray = viewCamera.ScreenPointToRay(mousePosition);
        if (!focusSurface.Collider.Raycast(ray, out RaycastHit hit, 30f))
        {
            hasLastUv = false;
            return;
        }

        Vector2 uv = hit.textureCoord;
        // Faster strokes get thinner, like a brush.
        float speed = (mousePosition - lastMouse).magnitude / Mathf.Max(Time.deltaTime, 0.001f);
        float radius = erasing ? eraserPixels : brushPixels * Mathf.Lerp(1.25f, 0.55f, Mathf.Clamp01(speed / 2500f));
        focusSurface.Stroke(hasLastUv ? lastUv : uv, uv, radius, erasing);
        focusJournal.MarkInkDirty();
        lastUv = uv;
        hasLastUv = true;
        lastMouse = mousePosition;
    }

    private void UpdateFocusCamera()
    {
        focusBlend = Mathf.MoveTowards(focusBlend, focusLeaving ? 0f : 1f, Time.deltaTime / focusTime);
        float t = Mathf.SmoothStep(0f, 1f, focusBlend);
        Vector3 walkPosition = normalPosition;
        Quaternion walkRotation = normalRotation;
        cameraHolder.position = Vector3.Lerp(walkPosition, focusPosition, t);
        cameraHolder.rotation = Quaternion.Slerp(walkRotation, focusRotation, t);

        if (focusLeaving && focusBlend <= 0f) FinishFocus();
    }

    private void LeaveFocus()
    {
        focusLeaving = true;
        focusJournal?.EndEditing();
        hud?.SetCorner(null);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        hud?.SetHint(null, null);
    }

    private void FinishFocus()
    {
        RestorePlayerVisuals();
        controller.enabled = true;
        controller.SetRestricted(false);
        ConsumedFrame = Time.frameCount;
        focusSurface = null;
        focusJournal = null;
        focusLeaving = false;
    }

    private void EndFocusNow()
    {
        if (focusSurface == null) return;
        focusJournal?.EndEditing();
        hud?.SetCorner(null);
        focusSurface = null;
        focusJournal = null;
        focusLeaving = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        cameraHolder.SetPositionAndRotation(normalPosition, normalRotation);
        RestorePlayerVisuals();
    }

    private void HidePlayerVisuals(Transform visuals)
    {
        if (visuals == null) return;
        foreach (Renderer renderer in visuals.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.forceRenderingOff) continue;
            renderer.forceRenderingOff = true;
            hiddenPlayerVisuals.Add(renderer);
        }
    }

    private void RestorePlayerVisuals()
    {
        foreach (Renderer renderer in hiddenPlayerVisuals)
            if (renderer != null) renderer.forceRenderingOff = false;
        hiddenPlayerVisuals.Clear();
    }

    private void OnDisable() => RestorePlayerVisuals();
}
