using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Press the meditate key to sit down and slowly erase the world (terrain, creatures, sky)
/// into blank paper. The player then wakes in the reflection space to draw on steles and
/// arrange casts of creatures they have met. Press the key again to return.
/// Needs a ReflectionSpace, CreatureJournal, ReflectionVisitor and MeditationHUD next to it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ReflectionSpace), typeof(CreatureJournal), typeof(ReflectionVisitor))]
[RequireComponent(typeof(MeditationHUD))]
public sealed class MeditationController : MonoBehaviour
{
    private enum Phase { World, Entering, Inside, Leaving }

    [SerializeField] private Key meditateKey = Key.M;
    [SerializeField] private Material veilMaterial;

    [Header("Timing (seconds)")]
    [Min(0.1f)] [SerializeField] private float settleTime = 1.5f;
    [Min(0.1f)] [SerializeField] private float skyEraseTime = 1.8f;
    [Min(0.1f)] [SerializeField] private float worldEraseTime = 4f;
    [Min(0.1f)] [SerializeField] private float spaceRevealTime = 2.5f;
    [Min(0.1f)] [SerializeField] private float spaceEraseTime = 2f;
    [Min(0.1f)] [SerializeField] private float worldRevealTime = 3.5f;
    [Tooltip("Plays every transition faster (2 = twice as fast).")]
    [Min(0.1f)] [SerializeField] private float transitionSpeed = 2f;

    [Header("Feel")]
    [Tooltip("How far the view sinks when sitting down.")]
    [Min(0f)] [SerializeField] private float sinkDepth = 0.5f;
    [Tooltip("Mouse look strength while settling (1 = normal).")]
    [Range(0f, 1f)] [SerializeField] private float settleLook = 0.3f;
    [SerializeField] private float lookSensitivity = 0.12f;
    [Tooltip("Veil radius around the reflection space.")]
    [Min(10f)] [SerializeField] private float spaceVeilRadius = 26f;
    [Tooltip("Height above the meditation spot where the reflection space is placed.")]
    [SerializeField] private float spaceHeight = 3000f;
    [SerializeField] private float spaceEyeHeight = 1.6f;

    private ReflectionSpace space;
    private CreatureJournal journal;
    private ReflectionVisitor visitor;
    private MeditationHUD hud;
    private MeditationVeil veil;
    private Phase phase = Phase.World;

    // Player parts, found when meditation starts.
    private SmoothFirstPersonController controller;
    private Rigidbody body;
    private Transform player;
    private Transform cameraHolder;
    private Camera viewCamera;
    private readonly List<Behaviour> pausedBehaviours = new List<Behaviour>();
    private readonly List<GameObject> hiddenObjects = new List<GameObject>();
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private readonly List<Creature> pausedCreatures = new List<Creature>();
    private bool wasKinematic;
    private RigidbodyInterpolation wasInterpolation;

    // Where to come back to.
    private Vector3 worldBodyPosition;
    private Vector3 worldCameraPosition;
    private Quaternion worldCameraRotation;

    public bool IsMeditating => phase != Phase.World;

    private void Awake()
    {
        space = GetComponent<ReflectionSpace>();
        journal = GetComponent<CreatureJournal>();
        visitor = GetComponent<ReflectionVisitor>();
        hud = GetComponent<MeditationHUD>();
        veil = new MeditationVeil(veilMaterial, null);
        journal.CreatureRecorded += entry => hud.Toast("Remembered:  " + entry.displayName);
    }

    private void OnDestroy()
    {
        veil?.Destroy();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard[meditateKey].wasPressedThisFrame) return;
        if (phase == Phase.World) StartCoroutine(Enter());
        else if (phase == Phase.Inside) StartCoroutine(Leave());
    }

    // ---------- Entering ----------

    private IEnumerator Enter()
    {
        if (!FindPlayer())
        {
            Debug.LogWarning("MeditationController: no SmoothFirstPersonController / camera found.");
            yield break;
        }
        if (!controller.Grounded)
        {
            hud.Toast("Find still ground to meditate");
            yield break;
        }

        phase = Phase.Entering;
        journal.Scanning = false;
        worldBodyPosition = body.position;
        worldCameraPosition = cameraHolder.position;
        worldCameraRotation = cameraHolder.rotation;
        PauseWorld();

        Vector3 euler = cameraHolder.rotation.eulerAngles;
        float yaw = euler.y;
        float pitch = NormalizeAngle(euler.x);
        float farRadius = viewCamera.farClipPlane * 0.7f / (1f + veil.Wobble);
        float nearRadius = MinimumRadius();
        veil.Visible = true;
        veil.Set(cameraHolder.position, farRadius, 0f);

        float total = settleTime + skyEraseTime + worldEraseTime;
        float skyStart = settleTime * 0.5f;
        float closeStart = skyStart + skyEraseTime * 0.6f;
        for (float time = 0f; time < total; time += Time.deltaTime * transitionSpeed)
        {
            // Sit down and look around slowly.
            float sink = Mathf.SmoothStep(0f, 1f, time / settleTime);
            ReadLook(ref yaw, ref pitch, settleLook);
            cameraHolder.SetPositionAndRotation(worldCameraPosition + Vector3.down * sinkDepth * sink,
                Quaternion.Euler(pitch, yaw, 0f));

            float cover = Mathf.SmoothStep(0f, 1f, (time - skyStart) / skyEraseTime);
            float close = Mathf.Clamp01((time - closeStart) / (total - closeStart));
            float radius = LogLerp(farRadius, nearRadius, Mathf.SmoothStep(0f, 1f, close));
            veil.Set(cameraHolder.position, radius, cover);
            hud.SetBlank(Mathf.InverseLerp(0.85f, 1f, close));
            yield return null;
        }
        hud.SetBlank(1f);
        yield return null;

        // Jump to the reflection space behind the blank.
        Vector3 anchor = worldBodyPosition + Vector3.up * spaceHeight;
        float spaceYaw = yaw;
        space.Place(anchor, spaceYaw);
        space.SyncCasts(journal);
        // Keep the body's own camera offset so the hidden capsule stands on the floor.
        float cameraAboveBody = worldCameraPosition.y - worldBodyPosition.y;
        Vector3 bodyToCamera = Vector3.up * cameraAboveBody;
        MovePlayer(anchor + Vector3.up * (spaceEyeHeight - cameraAboveBody));
        visitor.Begin(space, journal, hud, player, body, cameraHolder, viewCamera, bodyToCamera, spaceYaw, 0f);
        hud.SetInSpace(true);
        hud.SetCorner("[" + meditateKey + "]  Return");

        Vector3 centre = anchor + Vector3.up * spaceEyeHeight;
        for (float time = 0f; time < spaceRevealTime; time += Time.deltaTime * transitionSpeed)
        {
            float t = time / spaceRevealTime;
            veil.Set(centre, LogLerp(nearRadius, spaceVeilRadius, Mathf.SmoothStep(0f, 1f, t)), 1f);
            hud.SetBlank(1f - Mathf.Clamp01(t * 4f));
            yield return null;
        }
        veil.Set(centre, spaceVeilRadius, 1f);
        hud.SetBlank(0f);
        phase = Phase.Inside;
    }

    // ---------- Leaving ----------

    private IEnumerator Leave()
    {
        phase = Phase.Leaving;
        visitor.Freeze();
        space.SaveDrawings();
        journal.Save();
        hud.SetCorner(null);

        float nearRadius = MinimumRadius();
        Vector3 spaceCentre = space.Root.position + Vector3.up * spaceEyeHeight;
        for (float time = 0f; time < spaceEraseTime; time += Time.deltaTime * transitionSpeed)
        {
            float t = Mathf.SmoothStep(0f, 1f, time / spaceEraseTime);
            float radius = LogLerp(spaceVeilRadius, nearRadius, t);
            // Drift the centre onto the camera as the veil closes so the camera stays inside.
            Vector3 eye = visitor.CameraPosition;
            float keep = (radius - nearRadius) / (spaceVeilRadius - nearRadius) * 0.4f;
            veil.Set(eye + (spaceCentre - eye) * keep, radius, 1f);
            hud.SetBlank(Mathf.InverseLerp(0.8f, 1f, t));
            yield return null;
        }
        hud.SetBlank(1f);
        yield return null;

        visitor.End();
        space.Hide();
        hud.SetInSpace(false);
        MovePlayer(worldBodyPosition);
        Vector3 sunk = worldCameraPosition + Vector3.down * sinkDepth;
        cameraHolder.SetPositionAndRotation(sunk, worldCameraRotation);

        float farRadius = viewCamera.farClipPlane * 0.7f / (1f + veil.Wobble);
        bool resumed = false;
        for (float time = 0f; time < worldRevealTime; time += Time.deltaTime * transitionSpeed)
        {
            float t = time / worldRevealTime;
            float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.8f));
            float cover = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, t));
            hud.SetBlank(1f - Mathf.Clamp01(t * 5f));

            // Stand up during the first half, then hand control back.
            if (!resumed)
            {
                float stand = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.45f));
                cameraHolder.position = Vector3.Lerp(sunk, worldCameraPosition, stand);
                if (t >= 0.45f)
                {
                    ResumeWorld();
                    resumed = true;
                }
            }
            veil.Set(cameraHolder.position, LogLerp(nearRadius, farRadius, open), cover);
            yield return null;
        }
        if (!resumed) ResumeWorld();
        hud.SetBlank(0f);
        veil.Visible = false;
        journal.Scanning = true;
        phase = Phase.World;
    }

    // ---------- Player and world ----------

    private bool FindPlayer()
    {
        if (controller == null) controller = FindFirstObjectByType<SmoothFirstPersonController>();
        if (controller == null) return false;
        player = controller.transform;
        body = controller.GetComponent<Rigidbody>();
        viewCamera = Camera.main;
        if (viewCamera == null || body == null) return false;
        cameraHolder = viewCamera.transform.parent != null ? viewCamera.transform.parent : viewCamera.transform;
        return true;
    }

    // Stops the player controls, the first-person body, the prompt HUD and every creature.
    private void PauseWorld()
    {
        var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (carrier != null && carrier.CarriedCreature != null) carrier.Drop();

        pausedBehaviours.Clear();
        foreach (MonoBehaviour behaviour in player.GetComponentsInChildren<MonoBehaviour>())
            Pause(behaviour);
        Pause(FindFirstObjectByType<FirstPersonBody>());
        Pause(carrier);
        Pause(FindFirstObjectByType<PlayerCreatureInteractor>());
        Pause(FindFirstObjectByType<HandClimber>());

        hiddenObjects.Clear();
        Hide(GameObject.Find("First Person Body"));
        var prompt = FindFirstObjectByType<InteractionPromptHUD>();
        if (prompt != null) Hide(prompt.gameObject);

        // The player capsule mesh would show in front of the camera while drawing.
        hiddenRenderers.Clear();
        foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled) continue;
            renderer.enabled = false;
            hiddenRenderers.Add(renderer);
        }

        pausedCreatures.Clear();
        foreach (Creature creature in FindObjectsByType<Creature>(FindObjectsSortMode.None))
        {
            if (!creature.SimulationEnabled) continue;
            creature.SetSimulationEnabled(false);
            pausedCreatures.Add(creature);
        }

        wasKinematic = body.isKinematic;
        wasInterpolation = body.interpolation;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.interpolation = RigidbodyInterpolation.None;
        body.isKinematic = true;
    }

    private void ResumeWorld()
    {
        body.isKinematic = wasKinematic;
        body.interpolation = wasInterpolation;
        if (!body.isKinematic) body.linearVelocity = Vector3.zero;

        foreach (Behaviour behaviour in pausedBehaviours)
            if (behaviour != null) behaviour.enabled = true;
        pausedBehaviours.Clear();
        foreach (GameObject go in hiddenObjects)
            if (go != null) go.SetActive(true);
        hiddenObjects.Clear();
        foreach (Renderer renderer in hiddenRenderers)
            if (renderer != null) renderer.enabled = true;
        hiddenRenderers.Clear();
        foreach (Creature creature in pausedCreatures)
            if (creature != null) creature.SetSimulationEnabled(true);
        pausedCreatures.Clear();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Pause(Behaviour behaviour)
    {
        if (behaviour == null || !behaviour.enabled || behaviour == this) return;
        // Leave audio components alone.
        if (behaviour.GetType().Name.StartsWith("Ak")) return;
        if (pausedBehaviours.Contains(behaviour)) return;
        behaviour.enabled = false;
        pausedBehaviours.Add(behaviour);
    }

    private void Hide(GameObject go)
    {
        if (go == null || !go.activeSelf) return;
        go.SetActive(false);
        hiddenObjects.Add(go);
    }

    private void MovePlayer(Vector3 position)
    {
        player.position = position;
        body.position = position;
        Physics.SyncTransforms();
    }

    // ---------- Helpers ----------

    private void ReadLook(ref float yaw, ref float pitch, float strength)
    {
        if (Mouse.current == null) return;
        Vector2 delta = Mouse.current.delta.ReadValue() * lookSensitivity * strength;
        yaw += delta.x;
        pitch = Mathf.Clamp(pitch - delta.y, -80f, 80f);
    }

    // Smallest veil that still covers the whole view.
    private float MinimumRadius()
    {
        return Mathf.Max(0.5f, viewCamera.nearClipPlane * 4f) / (1f - veil.Wobble);
    }

    private static float LogLerp(float a, float b, float t)
    {
        return Mathf.Exp(Mathf.Lerp(Mathf.Log(a), Mathf.Log(b), t));
    }

    private static float NormalizeAngle(float angle) => angle > 180f ? angle - 360f : angle;
}
