using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player control inside the reflection space: a slow walk, mouse look, drawing on
/// steles and tablets, and moving creature casts. The normal player controller is
/// switched off while this runs, so it drives the player transform and camera itself.
/// </summary>
[DisallowMultipleComponent]
public sealed class ReflectionVisitor : MonoBehaviour
{
    [Header("Walk")]
    [Min(0.1f)] [SerializeField] private float walkSpeed = 2.4f;
    [SerializeField] private float lookSensitivity = 0.12f;

    [Header("Reach")]
    [Min(0.5f)] [SerializeField] private float reach = 3.5f;
    [SerializeField] private Key interactKey = Key.E;

    [Header("Drawing")]
    [Min(0.5f)] [SerializeField] private float brushPixels = 3.2f;
    [Min(0.5f)] [SerializeField] private float eraserPixels = 14f;
    [Tooltip("Seconds to walk the camera up to a face or back.")]
    [Min(0.05f)] [SerializeField] private float focusTime = 0.6f;

    private ReflectionSpace space;
    private CreatureJournal journal;
    private MeditationHUD hud;
    private Transform player;
    private Rigidbody body;
    private Transform cameraHolder;
    private Camera viewCamera;
    private Vector3 bodyToCamera;

    private float yaw, pitch;
    private bool active;
    private bool frozen;

    // Drawing focus.
    private InkSurface focusSurface;
    private float focusBlend;
    private bool focusLeaving;
    private Vector3 focusPosition;
    private Quaternion focusRotation;
    private bool hasLastUv;
    private Vector2 lastUv;
    private Vector2 lastMouse;

    // Cast being moved.
    private ReflectionCast heldCast;

    public bool IsDrawing => focusSurface != null;

    public void Begin(ReflectionSpace reflectionSpace, CreatureJournal creatureJournal, MeditationHUD meditationHud,
        Transform playerTransform, Rigidbody playerBody, Transform holder, Camera cam, Vector3 cameraOffset,
        float startYaw, float startPitch)
    {
        space = reflectionSpace;
        journal = creatureJournal;
        hud = meditationHud;
        player = playerTransform;
        body = playerBody;
        cameraHolder = holder;
        viewCamera = cam;
        bodyToCamera = cameraOffset;
        yaw = startYaw;
        pitch = Mathf.Clamp(startPitch, -60f, 60f);
        focusSurface = null;
        heldCast = null;
        frozen = false;
        active = true;
        PlaceCamera();
    }

    /// <summary>Stops input but keeps the camera where it is (used while leaving).</summary>
    public void Freeze()
    {
        EndFocusNow();
        DropCast();
        frozen = true;
        hud?.SetHint(null, null);
    }

    public void End()
    {
        Freeze();
        active = false;
    }

    public Vector3 CameraPosition => cameraHolder != null ? cameraHolder.position : player.position + bodyToCamera;

    private void Update()
    {
        if (!active || frozen) return;
        if (focusSurface != null)
        {
            UpdateDrawing();
            return;
        }

        Look();
        Walk();
        UpdateAim();
    }

    private void LateUpdate()
    {
        if (!active) return;
        if (focusSurface != null) UpdateFocusCamera();
        else if (!frozen) PlaceCamera();
        if (heldCast != null) CarryCast();
    }

    private void Look()
    {
        if (Mouse.current == null) return;
        Vector2 delta = Mouse.current.delta.ReadValue();
        yaw += delta.x * lookSensitivity;
        pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -80f, 80f);
    }

    private void Walk()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
        Vector3 move = heading * new Vector3(x, 0f, z);
        if (move.sqrMagnitude > 1f) move.Normalize();
        Vector3 next = space.Constrain(player.position + move * walkSpeed * Time.deltaTime, 0.3f);
        player.position = next;
        if (body != null) body.position = next;
    }

    private void PlaceCamera()
    {
        if (cameraHolder == null) return;
        cameraHolder.position = player.position + bodyToCamera;
        cameraHolder.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    // What the crosshair is on, and the E / LMB actions for it.
    private void UpdateAim()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (heldCast != null)
        {
            hud?.SetHint(heldCast.Entry?.displayName, "[" + interactKey + "]  Set down");
            if (keyboard != null && keyboard[interactKey].wasPressedThisFrame) DropCast();
            return;
        }

        if (!AimAtSpace(out RaycastHit hit))
        {
            hud?.SetHint(null, null);
            return;
        }

        var surface = hit.collider.GetComponent<InkSurface>();
        if (surface != null)
        {
            hud?.SetHint(null, "[LMB]  Draw");
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) StartFocus(surface);
            return;
        }

        var cast = hit.collider.GetComponentInParent<ReflectionCast>();
        if (cast != null)
        {
            string title = cast.Entry != null ? cast.Entry.displayName + "   ·   met " + cast.Entry.firstSeen : null;
            hud?.SetHint(title, "[" + interactKey + "]  Move");
            if (keyboard != null && keyboard[interactKey].wasPressedThisFrame) heldCast = cast;
            return;
        }
        hud?.SetHint(null, null);
    }

    // Nearest hit that belongs to the reflection space (skips the player's own colliders).
    private bool AimAtSpace(out RaycastHit best)
    {
        best = default;
        Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, reach, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (hit.distance >= nearest || !hit.collider.transform.IsChildOf(space.Root)) continue;
            nearest = hit.distance;
            best = hit;
        }
        return nearest < float.MaxValue;
    }

    private void CarryCast()
    {
        Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 floor = space.Root.position;
        var box = heldCast.GetComponent<BoxCollider>();
        float reachOut = 1f + (box != null ? Mathf.Max(box.size.x, box.size.z) * 0.6f : 0.3f);
        Vector3 target = player.position + forward * reachOut;
        target = space.Constrain(target, 0f);
        target.y = floor.y + 0.35f + Mathf.Sin(Time.time * 2.2f) * 0.03f;
        heldCast.transform.position = Vector3.Lerp(heldCast.transform.position, target, 1f - Mathf.Exp(-10f * Time.deltaTime));
        Quaternion facing = Quaternion.Euler(0f, yaw + 180f, 0f);
        heldCast.transform.rotation = Quaternion.Slerp(heldCast.transform.rotation, facing, 1f - Mathf.Exp(-6f * Time.deltaTime));
    }

    private void DropCast()
    {
        if (heldCast == null) return;
        Vector3 position = heldCast.transform.position;
        position.y = space.Root.position.y;
        heldCast.transform.position = position;
        space.StoreCast(heldCast);
        journal?.Save();
        heldCast = null;
    }

    // ---------- Drawing ----------

    private void StartFocus(InkSurface surface)
    {
        focusSurface = surface;
        focusBlend = 0f;
        focusLeaving = false;
        hasLastUv = false;

        // Stand back far enough to see the whole face.
        Transform face = surface.transform;
        Bounds bounds = surface.Collider.bounds;
        float halfFov = viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float fitHeight = surface.Size.y * 0.5f * 1.2f / Mathf.Tan(halfFov);
        float fitWidth = surface.Size.x * 0.5f * 1.2f / (Mathf.Tan(halfFov) * viewCamera.aspect);
        float distance = Mathf.Max(fitHeight, fitWidth, 0.6f);
        Vector3 normal = face.forward;
        focusPosition = bounds.center + normal * distance;
        focusRotation = Quaternion.LookRotation(-normal, Vector3.up);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        hud?.SetHint(null, "[LMB]  Draw     [RMB]  Erase     [" + interactKey + "]  Step back");
    }

    private void UpdateDrawing()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (focusLeaving) return;

        if (keyboard != null && (keyboard[interactKey].wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame))
        {
            LeaveFocus();
            return;
        }
        if (mouse == null || focusBlend < 0.95f) return;

        bool drawing = mouse.leftButton.isPressed;
        bool erasing = mouse.rightButton.isPressed;
        Vector2 mousePosition = mouse.position.ReadValue();
        if (!drawing && !erasing)
        {
            hasLastUv = false;
            lastMouse = mousePosition;
            return;
        }

        Ray ray = viewCamera.ScreenPointToRay(mousePosition);
        if (!focusSurface.Collider.Raycast(ray, out RaycastHit hit, 10f))
        {
            hasLastUv = false;
            return;
        }

        Vector2 uv = hit.textureCoord;
        // Faster strokes get thinner, like a brush.
        float speed = (mousePosition - lastMouse).magnitude / Mathf.Max(Time.deltaTime, 0.001f);
        float radius = erasing ? eraserPixels : brushPixels * Mathf.Lerp(1.25f, 0.55f, Mathf.Clamp01(speed / 2500f));
        focusSurface.Stroke(hasLastUv ? lastUv : uv, uv, radius, erasing);
        lastUv = uv;
        hasLastUv = true;
        lastMouse = mousePosition;
    }

    private void UpdateFocusCamera()
    {
        focusBlend = Mathf.MoveTowards(focusBlend, focusLeaving ? 0f : 1f, Time.deltaTime / focusTime);
        float t = Mathf.SmoothStep(0f, 1f, focusBlend);
        Vector3 walkPosition = player.position + bodyToCamera;
        Quaternion walkRotation = Quaternion.Euler(pitch, yaw, 0f);
        cameraHolder.position = Vector3.Lerp(walkPosition, focusPosition, t);
        cameraHolder.rotation = Quaternion.Slerp(walkRotation, focusRotation, t);

        if (focusLeaving && focusBlend <= 0f) FinishFocus();
    }

    private void LeaveFocus()
    {
        focusLeaving = true;
        focusSurface.Save();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        hud?.SetHint(null, null);
    }

    private void FinishFocus()
    {
        focusSurface = null;
        focusLeaving = false;
    }

    private void EndFocusNow()
    {
        if (focusSurface == null) return;
        focusSurface.Save();
        focusSurface = null;
        focusLeaving = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        PlaceCamera();
    }
}
