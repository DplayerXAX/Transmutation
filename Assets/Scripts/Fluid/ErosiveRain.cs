using UnityEngine;

/// <summary>
/// Erosive rain. Storms appear at random places, rain pooled droplets from the shared fluid system
/// for a while, drift with the wind, then clear and reappear elsewhere. Drops take this object's
/// ErosiveFluidSource settings, so they erode terrain and age creatures like any other water.
/// The source's Emitting flag is the rain switch, so F1 starts and stops rain with the other water.
/// </summary>
[RequireComponent(typeof(ErosiveFluidSource))]
public sealed class ErosiveRain : MonoBehaviour
{
    [Tooltip("Storms pick spots within this distance of the main camera; 0 picks anywhere in the world.")]
    [Min(0f)] [SerializeField] private float roamRadius = 35f;
    [Min(0.5f)] [SerializeField] private float stormRadius = 6f;
    [Tooltip("Cloud height above the highest ground under the storm.")]
    [Min(1f)] [SerializeField] private float cloudHeight = 14f;
    [Min(0f)] [SerializeField] private float dropsPerSecond = 20f;
    [Tooltip("Random storm length in seconds (min, max).")]
    [SerializeField] private Vector2 stormDuration = new Vector2(12f, 25f);
    [Tooltip("Random dry spell between storms in seconds (min, max).")]
    [SerializeField] private Vector2 clearDuration = new Vector2(8f, 20f);
    [Min(0f)] [SerializeField] private float firstStormDelay = 3f;
    [Min(0f)] [SerializeField] private float windSpeed = 0.6f;
    [Min(0f)] [SerializeField] private float fallSpeed = 3f;
    private static readonly Vector3[] ring = { Vector3.zero, Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
        new Vector3(0.7f, 0f, 0.7f), new Vector3(-0.7f, 0f, 0.7f), new Vector3(0.7f, 0f, -0.7f), new Vector3(-0.7f, 0f, -0.7f) };
    private ErosiveFluidSource source;
    private float timer, stormLength, accumulator, heightTimer;
    private bool raining;
    private Vector3 centre, wind;
    public bool Raining => raining;
    /// <summary>Cloud centre; drops start at this height.</summary>
    public Vector3 StormCentre => centre;
    public float StormRadius => stormRadius;
    /// <summary>Seconds until the current storm ends, or until the next one starts.</summary>
    public float TimeRemaining => Mathf.Max(0f, timer);
    /// <summary>0..1 storm strength, ramping up and down at both ends.</summary>
    public float Intensity { get; private set; }

    private void Awake() { source = GetComponent<ErosiveFluidSource>(); source.SetDriven(true); timer = firstStormDelay; }
    private void Reset() => GetComponent<ErosiveFluidSource>().ConfigureForRain();

    /// <summary>Starts a storm over the given ground point now, replacing any current storm.</summary>
    public bool StartStorm(Vector3 groundPoint)
    {
        ErosiveFluidSystem fluid = source.Fluid;
        if (fluid == null || fluid.World == null || !fluid.World.LayoutReady || !source.Emitting || !CloudHeight(fluid.World, groundPoint, out float y)) return false;
        Begin(new Vector3(groundPoint.x, y, groundPoint.z));
        return true;
    }

    private void FixedUpdate()
    {
        ErosiveFluidSystem fluid = source.Fluid;
        if (fluid == null || fluid.World == null || !fluid.World.LayoutReady || !source.Emitting)
        {
            // Switching water back on brings rain after the first delay, not after a long dry spell.
            if (raining || timer > firstStormDelay) timer = firstStormDelay;
            raining = false; Intensity = 0f; accumulator = 0f;
            return;
        }
        float dt = Time.fixedDeltaTime;
        timer -= dt;
        if (!raining)
        {
            if (timer > 0f) return;
            if (TryPickSite(fluid.World, out Vector3 site)) Begin(site); else timer = 1f;
            return;
        }
        if (timer <= 0f)
        {
            raining = false; Intensity = 0f; accumulator = 0f;
            timer = Random.Range(clearDuration.x, Mathf.Max(clearDuration.x, clearDuration.y));
            return;
        }
        float ramp = Mathf.Min(2f, stormLength * 0.25f);
        Intensity = Mathf.Clamp01(Mathf.Min(stormLength - timer, timer) / ramp);
        centre += wind * dt;
        // Drifting over higher ground lifts the cloud, so drops never start inside a hill.
        heightTimer -= dt;
        if (heightTimer <= 0f)
        {
            heightTimer = 1f;
            if (CloudHeight(fluid.World, centre, out float y)) centre.y = Mathf.Max(centre.y, y);
        }
        accumulator += dropsPerSecond * Intensity * dt;
        int count = Mathf.Min(16, Mathf.FloorToInt(accumulator));
        accumulator -= count;
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = Random.insideUnitCircle * stormRadius;
            Vector3 point = centre + new Vector3(offset.x, 0f, offset.y);
            if (fluid.InBounds(point)) fluid.Spawn(source, point, Vector3.down * fallSpeed + wind);
        }
    }

    private void Begin(Vector3 site)
    {
        centre = site; raining = true; accumulator = 0f; heightTimer = 1f; Intensity = 0f;
        stormLength = timer = Random.Range(stormDuration.x, Mathf.Max(stormDuration.x, stormDuration.y));
        Vector2 heading = Random.insideUnitCircle.normalized;
        wind = new Vector3(heading.x, 0f, heading.y) * windSpeed;
    }

    private bool TryPickSite(ProceduralWorld world, out Vector3 site)
    {
        Bounds bounds = world.VolumeBounds;
        Camera view = Camera.main;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector3 point;
            if (roamRadius > 0f && view != null)
            {
                Vector2 offset = Random.insideUnitCircle * roamRadius;
                point = view.transform.position + new Vector3(offset.x, 0f, offset.y);
            }
            else point = new Vector3(Random.Range(bounds.min.x, bounds.max.x), 0f, Random.Range(bounds.min.z, bounds.max.z));
            // Keep the whole storm over the world.
            float margin = Mathf.Min(stormRadius, bounds.extents.x, bounds.extents.z);
            point.x = Mathf.Clamp(point.x, bounds.min.x + margin, bounds.max.x - margin);
            point.z = Mathf.Clamp(point.z, bounds.min.z + margin, bounds.max.z - margin);
            if (!CloudHeight(world, point, out float y)) continue;
            site = new Vector3(point.x, y, point.z);
            if (world.IsRegionReady(new Vector3(point.x, y - cloudHeight, point.z), stormRadius)) return true;
        }
        site = default;
        return false;
    }

    // Highest generated ground under the storm footprint, plus the cloud height.
    private bool CloudHeight(ProceduralWorld world, Vector3 point, out float height)
    {
        Bounds bounds = world.VolumeBounds;
        float top = bounds.max.y + 1f, highest = float.NegativeInfinity;
        int mask = 1 << world.TerrainLayer;
        foreach (Vector3 direction in ring)
        {
            Vector3 from = new Vector3(point.x, top, point.z) + direction * stormRadius;
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, bounds.size.y + 2f, mask, QueryTriggerInteraction.Ignore)
                && world.OwnsCollider(hit.collider)) highest = Mathf.Max(highest, hit.point.y);
        }
        height = highest + cloudHeight;
        return !float.IsNegativeInfinity(highest);
    }

    private void OnDrawGizmosSelected()
    {
        if (!raining) return;
        Gizmos.color = new Color(0.2f, 0.8f, 0.8f, 0.6f);
        Gizmos.DrawWireSphere(centre, stormRadius);
        Gizmos.DrawLine(centre, centre + Vector3.down * cloudHeight);
    }
}
