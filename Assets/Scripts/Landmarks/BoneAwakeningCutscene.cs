using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Temporary cinematic camera; leaves the gameplay camera and carried creature in place.</summary>
[DefaultExecutionOrder(2000)]
public sealed class BoneAwakeningCutscene : MonoBehaviour
{
    private static BoneAwakeningCutscene current;
    public static bool Playing => current != null && current.running;
    private BoneKiln mother;
    private Camera gameplayCamera, shot;
    private bool cameraEnabled, running, wasKinematic;
    private Rigidbody player;
    private readonly List<Behaviour> paused = new List<Behaviour>();
    private Vector3 cameraVelocity;
    private float elapsed, settled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => current = null;

    public static void Play(BoneKiln owner)
    {
        if (Playing || owner == null || Camera.main == null) return;
        var scene = new GameObject("Bone Mother Awakening Cutscene").AddComponent<BoneAwakeningCutscene>();
        scene.Begin(owner, Camera.main);
    }

    private void Begin(BoneKiln owner, Camera camera)
    {
        mother = owner; gameplayCamera = camera;
        current = this; running = true;
        BoneRide.CancelApproach();
        TentacleTether.Instance?.PauseInput();
        var controller = FindFirstObjectByType<SmoothFirstPersonController>();
        if (controller != null)
        {
            controller.GetComponent<AdvancedFirstPersonTraversal>()?.StopForBubbleCapture();
            foreach (var climber in FindObjectsByType<HandClimber>(FindObjectsSortMode.None))
                if (climber.Controls(controller)) { climber.StopForBubbleCapture(); Pause(climber); }
            Pause(controller.GetComponent<AdvancedFirstPersonTraversal>());
            Pause(controller);
            player = controller.GetComponent<Rigidbody>();
        }
        if (player != null)
        {
            wasKinematic = player.isKinematic;
            if (!wasKinematic) { player.linearVelocity = Vector3.zero; player.angularVelocity = Vector3.zero; }
            player.isKinematic = true;
        }
        cameraEnabled = camera.enabled;
        shot = new GameObject("Awakening Camera").AddComponent<Camera>();
        shot.CopyFrom(camera);
        shot.fieldOfView = 55f;
        shot.nearClipPlane = 0.15f;
        shot.depth = camera.depth + 1f;
        shot.tag = "MainCamera";
        camera.enabled = false;
        PoseCamera(1f, true);
    }

    private void Pause(Behaviour component)
    {
        if (component == null || !component.enabled) return;
        paused.Add(component); component.enabled = false;
    }

    private void LateUpdate()
    {
        if (!running) return;
        if (mother == null || !mother.isActiveAndEnabled) { Finish(); return; }
        elapsed += Time.deltaTime;
        Keyboard keys = Keyboard.current;
        // Skipping restores control; the same smooth world animation continues without teleporting her.
        if (keys != null && (keys.escapeKey.wasPressedThisFrame || keys.spaceKey.wasPressedThisFrame)) { Finish(); return; }
        PoseCamera(Time.deltaTime, false);
        settled = mother.Emerging ? 0f : settled + Time.deltaTime;
        if (settled >= 2f || elapsed > 60f) Finish();
    }

    private void PoseCamera(float deltaTime, bool immediate)
    {
        Bounds frame = mother.AwakeningFrame();
        Vector3 focus = frame.center + Vector3.up * 2f;
        // Follow a close section of the animal rather than retreating to fit the entire long skeleton.
        float distance = Mathf.Clamp(frame.extents.magnitude * 1.45f, 16f, 40f);
        float orbit = Mathf.SmoothStep(-55f, 35f, elapsed / 20f);
        Vector3 direction = Quaternion.AngleAxis(orbit, Vector3.up) * mother.ExitDirection;
        Vector3 desired = focus + (direction + Vector3.up * 0.32f).normalized * distance;
        desired = BoneFlightCollision.Move(focus, desired, 0.4f, mother.Root);
        shot.transform.position = immediate ? desired : Vector3.SmoothDamp(shot.transform.position, desired,
            ref cameraVelocity, 0.7f, float.PositiveInfinity, deltaTime);
        Vector3 sight = focus - shot.transform.position;
        if (sight.sqrMagnitude > 0.01f)
        {
            Quaternion rotation = Quaternion.LookRotation(sight, Vector3.up);
            shot.transform.rotation = immediate ? rotation : Quaternion.Slerp(shot.transform.rotation, rotation, 1f - Mathf.Exp(-3f * deltaTime));
        }
    }

    public void Finish()
    {
        if (!running) return;
        running = false;
        if (current == this) current = null;
        if (shot != null) { shot.enabled = false; Destroy(shot.gameObject); }
        if (gameplayCamera != null) gameplayCamera.enabled = cameraEnabled;
        if (player != null) player.isKinematic = BoneRide.Mount != null || wasKinematic;
        foreach (Behaviour component in paused) if (component != null) component.enabled = true;
        paused.Clear();
        Destroy(gameObject);
    }

    private void OnDisable() => Finish();
    private void OnGUI()
    {
        if (!running) return;
        GUI.color = Color.black;
        float bar = Screen.height * 0.085f;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, bar), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0f, Screen.height - bar, Screen.width, bar), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(Screen.width - 200f, Screen.height - bar + 12f, 190f, 28f), "[Esc / Space] Skip");
    }
}
