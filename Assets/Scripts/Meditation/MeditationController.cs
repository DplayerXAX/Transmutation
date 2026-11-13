using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>Transitions between a world resting place and an isolated reflection visit.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ReflectionSpace), typeof(ReflectionVisitor))]
[RequireComponent(typeof(MeditationHUD))]
public sealed class MeditationController : MonoBehaviour
{
    private enum Phase { World, Entering, Inside, Leaving }

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
    [SerializeField] private float spaceEyeHeight = 1.6f;

    private ReflectionSpace space;
    private Scene worldScene, visitScene;
    private GameObject playerRoot;
    private readonly List<GameObject> suspendedRoots = new();
    private ReflectionProgress progress;
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
    private bool wasKinematic;
    private RigidbodyInterpolation wasInterpolation;

    // Where to come back to.
    private Vector3 worldBodyPosition;
    private Vector3 worldCameraPosition;
    private Quaternion worldCameraRotation;

    public bool IsMeditating => phase != Phase.World;

    public bool Inside => phase == Phase.Inside;
    public bool Transitioning => phase == Phase.Entering || phase == Phase.Leaving;
    internal static bool BlocksPlayerInput => active != null && (active.Transitioning || (active.visitor != null && active.visitor.IsDrawing));
    private static MeditationController active;

    private void Awake()
    {
        space = GetComponent<ReflectionSpace>();
        if (TryGetComponent(out CreatureJournal legacyJournal)) legacyJournal.enabled = false;
        active = this;
        RestSpotSystem.EnsureFor(this);
        visitor = GetComponent<ReflectionVisitor>();
        hud = GetComponent<MeditationHUD>();
        veil = new MeditationVeil(veilMaterial, transform);

    }

    private void OnDestroy()
    {
        if (active == this) active = null;
        veil?.Destroy();
    }

    private void OnDisable()
    {
        if (phase == Phase.World || body == null) return;
        visitor.End();
        StopAllCoroutines();
        if (visitScene.IsValid() && worldScene.isLoaded) RestoreScene();
        MovePlayer(worldBodyPosition);
        cameraHolder.SetPositionAndRotation(worldCameraPosition, worldCameraRotation);
        controller.SetLook(worldCameraRotation.eulerAngles.y, NormalizeAngle(worldCameraRotation.eulerAngles.x));
        controller.SetLook(worldCameraRotation.eulerAngles.y, NormalizeAngle(worldCameraRotation.eulerAngles.x));
        ResumeWorld();
        RestSpotSystem.Instance?.EndVisit();
        hud.SetBlank(0f);
        hud.SetInSpace(false);
        veil.Visible = false;
        phase = Phase.World;
    }

    public void RequestEntry(RestSpot seat)
    {
        if (phase != Phase.World || seat == null || RestSpotSystem.Instance?.SeatedAt != seat || !FindPlayer()) return;
        ReflectionCatalog catalog = ReflectionCatalog.Load();
        if (catalog == null)
        {
            Debug.LogError("Reflection catalog is missing.", this);
            return;
        }
        progress ??= ReflectionProgress.Load();
        progress.Discover(catalog, seat);
        StartCoroutine(Enter());
    }

    public void RequestReturn()
    {
        if (Inside) StartCoroutine(Leave());
    }

    private void FixedUpdate()
    {
        if (visitScene.IsValid() && visitScene.isLoaded) visitScene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
    }

    // ---------- Entering ----------

    private IEnumerator Enter()
    {
        phase = Phase.Entering;
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

        if (!TryBuildVisit()) yield break;
        MovePlayer(new Vector3(0f, 1.05f, -2f));
        controller.SetLook(0f, 0f);
        cameraHolder.SetPositionAndRotation(new Vector3(0f, spaceEyeHeight, -2f), Quaternion.identity);
        visitor.Begin(hud, controller, cameraHolder, viewCamera);
        hud.SetInSpace(true);
        Vector3 centre = new Vector3(0f, spaceEyeHeight, -2f);
        float revealRadius = Mathf.Max(spaceVeilRadius, space.ViewRadius / (1f - veil.Wobble));
        for (float time = 0f; time < spaceRevealTime; time += Time.deltaTime * transitionSpeed)
        {
            float t = time / spaceRevealTime;
            veil.Set(centre, LogLerp(nearRadius, revealRadius, Mathf.SmoothStep(0f, 1f, t)), 1f);
            hud.SetBlank(1f - Mathf.Clamp01(t * 4f));
            yield return null;
        }
        veil.Set(centre, revealRadius, 1f);
        hud.SetBlank(0f);
        ResumePlayer();
        body.isKinematic = wasKinematic;
        body.interpolation = wasInterpolation;
        RestorePlayerPresentation();
        phase = Phase.Inside;
    }

    // ---------- Leaving ----------

    private IEnumerator Leave()
    {
        phase = Phase.Leaving;
        visitor.End();
        FindFirstObjectByType<PlayerCreatureCarrier>()?.Release();
        RestSpotSystem.Instance?.StandForReturn();
        PausePlayer();
        space.SaveDrawings();
        hud.SetCorner(null);

        float nearRadius = MinimumRadius();
        Vector3 spaceCentre = space.Root.position + Vector3.up * spaceEyeHeight;
        float revealRadius = Mathf.Max(spaceVeilRadius, space.ViewRadius / (1f - veil.Wobble));
        for (float time = 0f; time < spaceEraseTime; time += Time.deltaTime * transitionSpeed)
        {
            float t = Mathf.SmoothStep(0f, 1f, time / spaceEraseTime);
            float radius = LogLerp(revealRadius, nearRadius, t);
            // Drift the centre onto the camera as the veil closes so the camera stays inside.
            Vector3 eye = cameraHolder.position;
            float keep = (radius - nearRadius) / (revealRadius - nearRadius) * 0.4f;
            veil.Set(eye + (spaceCentre - eye) * keep, radius, 1f);
            hud.SetBlank(Mathf.InverseLerp(0.8f, 1f, t));
            yield return null;
        }
        hud.SetBlank(1f);
        yield return null;

        RestoreScene();
        hud.SetInSpace(false);
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
                    resumed = true;
                }
            }
            veil.Set(cameraHolder.position, LogLerp(nearRadius, farRadius, open), cover);
            yield return null;
        }
        ResumeWorld();
        RestSpotSystem.Instance?.EndVisit();
        hud.SetBlank(0f);
        veil.Visible = false;
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

        wasKinematic = RestSpotSystem.Instance != null && RestSpotSystem.Instance.Sitting
            ? RestSpotSystem.Instance.PlayerWasKinematic : body.isKinematic;
        wasInterpolation = body.interpolation;
        PausePlayer();

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

        body.interpolation = RigidbodyInterpolation.None;
        body.isKinematic = true;
    }

    private void ResumeWorld()
    {
        body.isKinematic = wasKinematic;
        body.interpolation = wasInterpolation;
        if (!body.isKinematic) body.linearVelocity = Vector3.zero;

        ResumePlayer();
        RestorePlayerPresentation();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void RestorePlayerPresentation()
    {
        foreach (GameObject go in hiddenObjects)
            if (go != null) go.SetActive(true);
        hiddenObjects.Clear();
        foreach (Renderer renderer in hiddenRenderers)
            if (renderer != null) renderer.enabled = true;
        hiddenRenderers.Clear();
    }

    private void PausePlayer()
    {
        foreach (MonoBehaviour behaviour in player.GetComponentsInChildren<MonoBehaviour>())
            Pause(behaviour);
        Pause(controller);
        Pause(FindFirstObjectByType<FirstPersonBody>());
        Pause(FindFirstObjectByType<PlayerCreatureCarrier>());
        Pause(FindFirstObjectByType<PlayerCreatureInteractor>());
        Pause(FindFirstObjectByType<HandClimber>());
        body.isKinematic = true;
    }

    private void ResumePlayer()
    {
        foreach (Behaviour behaviour in pausedBehaviours)
            if (behaviour != null) behaviour.enabled = true;
        pausedBehaviours.Clear();
    }

    private bool TryBuildVisit()
    {
        try
        {
            BeginScene();
            space.BuildVisit(ReflectionCatalog.Load(), progress);
            return true;
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
            if (visitScene.IsValid()) RestoreScene();
            ResumeWorld();
            RestSpotSystem.Instance?.EndVisit();
            cameraHolder.SetPositionAndRotation(worldCameraPosition, worldCameraRotation);
            hud.SetBlank(0f);
            veil.Visible = false;
            phase = Phase.World;
            return false;
        }
    }

    private void BeginScene()
    {
        worldScene = player.gameObject.scene;
        playerRoot = player.root.gameObject;
        RestSpotSystem.Instance?.BeginVisit();
        body.isKinematic = true;
        visitScene = SceneManager.CreateScene("Reflection Visit", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        SceneManager.MoveGameObjectToScene(playerRoot, visitScene);
        SceneManager.SetActiveScene(visitScene);
        ReflectionPhysics.Visit = visitScene;
        ReflectionPhysics.SwitchingWorldRoots = true;
        try
        {
            foreach (GameObject root in worldScene.GetRootGameObjects())
            {
                if (root == transform.root.gameObject ||
                    (RestSpotSystem.Instance != null && root == RestSpotSystem.Instance.transform.root.gameObject)) continue;
                SuspendRoot(root);
            }
            if (RestSpotSystem.Instance != null)
                foreach (RestSpot seat in RestSpotSystem.Instance.Spots)
                    if (seat != null) SuspendRoot(seat.gameObject);
        }
        finally { ReflectionPhysics.SwitchingWorldRoots = false; }
    }

    private void SuspendRoot(GameObject root)
    {
        if (!root.activeSelf) return;
        suspendedRoots.Add(root);
        root.SetActive(false);
    }

    private void RestoreScene()
    {
        SceneManager.SetActiveScene(worldScene);
        ReflectionPhysics.Visit = default;
        if (playerRoot != null && playerRoot.scene == visitScene)
            SceneManager.MoveGameObjectToScene(playerRoot, worldScene);
        if (body != null) MovePlayer(worldBodyPosition);
        ReflectionPhysics.SwitchingWorldRoots = true;
        try
        {
            foreach (GameObject root in suspendedRoots)
                if (root != null) root.SetActive(true);
        }
        finally
        {
            suspendedRoots.Clear();
            ReflectionPhysics.SwitchingWorldRoots = false;
        }
        space.Hide();
        SceneManager.UnloadSceneAsync(visitScene);
        visitScene = default;
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
