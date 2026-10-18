using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Once the spire is unlocked (its crown seed taken), a strong pillar of light runs from high in the sky,
/// down through the spire's crown and its sinkhole, into the inner world below.
/// Three glowing rings mark the stops: the crown, the lowest ledge at ground level, and the inner-world floor.
/// Stand in a ring and press Space to ride the light up to the next stop, or Ctrl to go down.
/// The ride is quick: across into the beam, along it, and out onto the next ring.
/// Spire code is not touched; this only reads its public data and LandmarkProgress.
/// </summary>
[DisallowMultipleComponent]
public sealed class SpireBeam : MonoBehaviour
{
    [SerializeField] private Material beamMaterial;
    [SerializeField] private Material ringMaterial;
    [Tooltip("Seed that unlocks the beam (the spire's crown seed).")]
    [SerializeField] private string unlockSeed = "spire_seed";
    [Min(0.3f)] [SerializeField] private float beamRadius = 1.3f;
    [Min(0.5f)] [SerializeField] private float ringRadius = 1.4f;
    [Tooltip("Seconds for one ride between two stops.")]
    [Min(0.2f)] [SerializeField] private float rideTime = 1.6f;
    [SerializeField] private Key upKey = Key.Space;
    [SerializeField] private Key downKey = Key.LeftCtrl;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool built;
    [SerializeField] private bool unlocked;
    [SerializeField] private int nearStop = -1;

    private static readonly int FadeId = Shader.PropertyToID("_Fade");

    private ProceduralWorld world;
    private Transform root;
    private Vector3 axis;
    private readonly List<Vector3> stops = new List<Vector3>();
    private Material beamInstance, ringInstance;
    private float fade, waited;
    private Rigidbody player;
    private SmoothFirstPersonController controller;
    private bool riding;
    private string shownPrompt;

    public void Setup(Material beam, Material ring)
    {
        beamMaterial = beam;
        ringMaterial = ring;
    }

    private void Update()
    {
        if (!built)
        {
            if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
            waited += Time.deltaTime;
            HangingSpire spire = FindFirstObjectByType<HangingSpire>();
            if (!RealmSites.ReadyToPlace(world, waited) || spire == null || !spire.Built) return;
            built = true;
            Build(spire);
            return;
        }
        if (root == null) return;

        unlocked = LandmarkProgress.HasSeed(unlockSeed);
        fade = Mathf.MoveTowards(fade, unlocked ? 1f : 0f, Time.deltaTime * 0.5f);
        if (beamInstance != null) beamInstance.SetFloat(FadeId, fade);
        if (ringInstance != null) ringInstance.SetFloat(FadeId, Mathf.Max(fade, 0.15f));

        if (!unlocked || riding) return;
        if (player == null)
        {
            controller = FindFirstObjectByType<SmoothFirstPersonController>();
            if (controller != null) player = controller.GetComponent<Rigidbody>();
            if (player == null) return;
        }

        nearStop = -1;
        for (int i = 0; i < stops.Count; i++)
            if (RealmSites.Flat(player.position - stops[i]).magnitude < ringRadius + 0.4f && Mathf.Abs(player.position.y - stops[i].y) < 2.5f)
                nearStop = i;
        string prompt = nearStop < 0 ? null :
            (nearStop < stops.Count - 1 ? "[Space] Ride the light up" : "") + (nearStop > 0 && nearStop < stops.Count - 1 ? "     " : "") + (nearStop > 0 ? "[Ctrl] Ride it down" : "");
        if (prompt != shownPrompt)
        {
            shownPrompt = prompt;
            if (!string.IsNullOrEmpty(prompt)) LandmarkToast.Show(prompt);
        }
        if (nearStop < 0 || Keyboard.current == null) return;
        if (Keyboard.current[upKey].wasPressedThisFrame && nearStop < stops.Count - 1) StartCoroutine(Ride(nearStop + 1));
        else if (Keyboard.current[downKey].wasPressedThisFrame && nearStop > 0) StartCoroutine(Ride(nearStop - 1));
    }

    private System.Collections.IEnumerator Ride(int to)
    {
        riding = true;
        // The same lock a bubble capture uses: no walking, jumping or climbing during the ride.
        controller.SuppressJumpForCurrentFrame();
        controller.BeginBubbleMovementLock();
        bool wasKinematic = player.isKinematic;
        if (!wasKinematic) player.linearVelocity = Vector3.zero;
        player.isKinematic = true;
        Vector3 start = player.position;
        Vector3 target = stops[to] + Vector3.up * 1.1f;
        Vector3 inStart = new Vector3(axis.x, start.y, axis.z);
        Vector3 inEnd = new Vector3(axis.x, target.y, axis.z);
        float time = 0f;
        while (time < rideTime)
        {
            time += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, time / rideTime);
            // Step into the beam, travel along it, step out onto the ring.
            Vector3 p = t < 0.15f ? Vector3.Lerp(start, inStart, t / 0.15f)
                : t < 0.85f ? Vector3.Lerp(inStart, inEnd, Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.7f))
                : Vector3.Lerp(inEnd, target, (t - 0.85f) / 0.15f);
            player.position = p;
            player.transform.position = p;
            yield return null;
        }
        player.position = target;
        player.transform.position = target;
        player.isKinematic = wasKinematic;
        if (!wasKinematic) player.linearVelocity = Vector3.zero;
        controller.EndBubbleMovementLock();
        riding = false;
    }

    private void Build(HangingSpire spire)
    {
        root = new GameObject("Spire Beam").transform;
        Vector3 hole = world.SinkholeCentre(spire.SinkholeIndex, world.transform.position.y, out _);
        world.ColumnHeights(hole, out float surface, out _, out float innerGround);
        axis = new Vector3(hole.x, surface, hole.z);

        // Stops, bottom to top: inner-world floor, the lowest ledge, the crown.
        Vector3 inner = new Vector3(hole.x, innerGround, hole.z);
        // The real inner floor: look straight down the shaft from just below the surface.
        // Skip the spire's own parts (they share the ground layer) and take the highest real floor hit.
        float best = float.MinValue;
        foreach (RaycastHit floor in Physics.RaycastAll(new Vector3(hole.x, surface - 2f, hole.z), Vector3.down, 250f, 1 << world.TerrainLayer, QueryTriggerInteraction.Ignore))
            if (!floor.collider.transform.IsChildOf(spire.transform) && floor.collider.transform.root.name != "Hanging Spire (runtime)" && floor.point.y > best)
            {
                best = floor.point.y;
                inner = floor.point;
            }
        stops.Add(inner + Vector3.up * 0.05f);
        Vector3 lowest = Vector3.zero;
        float lowestY = float.MaxValue;
        foreach (var landing in spire.Landings)
            if (landing.top.y < lowestY) { lowestY = landing.top.y; lowest = landing.top; }
        if (lowestY < float.MaxValue) stops.Add(lowest + Vector3.up * 0.05f);
        if (spire.CrownSeat(out Vector3 crown, out _)) stops.Add(crown + Vector3.up * 0.05f);

        float bottom = inner.y - 2f;
        float top = (stops.Count > 0 ? stops[stops.Count - 1].y : surface) + 120f;
        beamInstance = beamMaterial != null ? new Material(beamMaterial) : null;
        ringInstance = ringMaterial != null ? new Material(ringMaterial) : null;

        var beam = new RealmMeshes();
        beam.Tube(new List<Vector3> { new Vector3(axis.x, bottom, axis.z), new Vector3(axis.x, top, axis.z) }, _ => beamRadius, 24, 0f, 1f, 0f);
        beam.Tube(new List<Vector3> { new Vector3(axis.x, bottom, axis.z), new Vector3(axis.x, top, axis.z) }, _ => beamRadius * 2.6f, 24, 0f, 1f, 0.5f);
        RealmSites.Part("Light Pillar", root, beam.Build("Light Pillar"), beamInstance, 2, false).transform.position = Vector3.zero;

        var rings = new RealmMeshes();
        foreach (Vector3 stop in stops)
        {
            var circle = new List<Vector3>();
            for (int k = 0; k <= 40; k++)
            {
                float a = k / 40f * Mathf.PI * 2f;
                circle.Add(stop + new Vector3(Mathf.Cos(a), 0.06f, Mathf.Sin(a)) * ringRadius);
            }
            rings.Tube(circle, _ => 0.06f, 6, 0f, 1f, 0f);
        }
        RealmSites.Part("Light Rings", root, rings.Build("Light Rings"), ringInstance, 2, false).transform.position = Vector3.zero;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
        if (beamInstance != null) Destroy(beamInstance);
        if (ringInstance != null) Destroy(ringInstance);
    }
}
