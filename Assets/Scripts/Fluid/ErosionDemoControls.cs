using UnityEngine;
using UnityEngine.InputSystem;

public sealed class ErosionDemoControls : MonoBehaviour
{
    [SerializeField] private ErosiveFluidSystem fluid;
    [SerializeField] private Vector3 fixedBrushPosition = new Vector3(-12f, 4f, -8f);
    [SerializeField] private bool brushRelativeToWorld = true;
    private bool digging, emitting = true, menu;
    private SmoothFirstPersonController player;
    private void Start() { player = FindFirstObjectByType<SmoothFirstPersonController>(); }
    private void Update()
    {
        var keys = Keyboard.current;
        if (keys != null)
        {
            if (keys.f1Key.wasPressedThisFrame) ToggleWater();
            if (keys.f2Key.wasPressedThisFrame) digging = !digging;
            if (keys.f3Key.wasPressedThisFrame) fluid.ClearDroplets();
            if (keys.f4Key.wasPressedThisFrame) Reset();
            if (keys.tabKey.wasPressedThisFrame)
            { menu = !menu; Cursor.lockState = menu ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = menu; if (player != null) player.SetFreeze(menu); }
        }
    }
    private void FixedUpdate()
    { if (digging && fluid.World.LayoutReady) fluid.World.Erode(fixedBrushPosition + (brushRelativeToWorld ? fluid.World.transform.position : Vector3.zero), 1.25f, 4f * Time.fixedDeltaTime); }
    private void ToggleWater() { emitting = !emitting; fluid.SetEmitting(emitting); }
    private void Reset() { fluid.ClearDroplets(); fluid.World.ResetErosion(); }
    private void OnGUI()
    {
        if (fluid == null || fluid.World == null) return;
        GUILayout.BeginArea(new Rect(12, 12, 390, menu ? 400 : 175), GUI.skin.box);
        GUILayout.Label("EROSIVE FLUID PROTOTYPE  |  Tab: controls");
        GUILayout.Label($"Water {fluid.ActiveCount}/{fluid.Capacity}  |  Edited samples {fluid.World.ModifiedSampleCount}");
        GUILayout.Label($"Budget {fluid.World.DensityBudgetPerSecond:F1} density/s  |  Removed {fluid.World.TotalDensityRemoved:F1}");
        GUILayout.Label($"Pending {fluid.World.PendingSampleCount}  Workers {fluid.World.ActiveWorkers}  Stale rejected {fluid.World.StaleResultsRejected}");
        GUILayout.Label($"Upload {fluid.World.LastUploadMilliseconds:F2} ms  Bake {fluid.World.LastBakeMilliseconds:F2} ms  Commit {fluid.World.LastCommitMilliseconds:F2} ms");
        if (fluid.World.LastUpdateError != null) GUILayout.Label("Terrain update failed. F4 retries from the initial terrain.");
        ErosiveRain rain = fluid.Rain;
        if (rain != null)
            GUILayout.Label(rain.Raining ? $"Rain at ({rain.StormCentre.x:F0}, {rain.StormCentre.z:F0})  {rain.Intensity:P0}  ends in {rain.TimeRemaining:F0}s"
                : $"Rain clear  |  next storm in {rain.TimeRemaining:F0}s");
        GUILayout.Label("F1 Water   F2 Fixed dig   F3 Clear drops   F4 Reset");
        if (menu)
        {
            if (GUILayout.Button(emitting ? "Stop water (existing drops remain)" : "Start water")) ToggleWater();
            if (GUILayout.Button(digging ? "Stop fixed dig" : "Dig fixed test point")) digging = !digging;
            if (GUILayout.Button("Clear droplets")) fluid.ClearDroplets();
            if (GUILayout.Button("Reset terrain erosion")) Reset();
            if (rain != null && player != null && GUILayout.Button("Rain near me"))
                rain.StartStorm(player.Body.position + player.transform.forward * 6f);
            if (GUILayout.Button("Go to water source"))
            {
                var source = fluid.GetComponentInChildren<ErosiveFluidSource>();
                if (source != null && player != null)
                {
                    Vector3 p = source.transform.position + Vector3.left * 3f + Vector3.back * 2f;
                    if (Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 60f, 1 << 7)) p.y = hit.point.y + 1.2f;
                    player.Body.position = p; player.Body.linearVelocity = Vector3.zero;
                }
            }
        }
        GUILayout.EndArea();
    }
}
