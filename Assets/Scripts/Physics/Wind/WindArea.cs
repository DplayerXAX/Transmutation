using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A box of wind flowing along this transform's forward (+Z, the blue arrow). Everything with a Rigidbody
/// inside it is pushed by WindField's shared rule. A wind wall is just a thin area with a speed between
/// walking and sprinting: walking into it stops you, anything faster gets through, and slowing down
/// partway pushes you back out.
/// </summary>
[DisallowMultipleComponent]
public sealed class WindArea : MonoBehaviour
{
    private static readonly List<WindArea> active = new List<WindArea>();
    /// <summary>Enabled areas in play. Read-only for consumers.</summary>
    public static IReadOnlyList<WindArea> Active => active;

    [Header("Shape")]
    [Tooltip("Box size in local space. Wind flows along local +Z.")]
    [SerializeField] private Vector3 size = new Vector3(6f, 4f, 1.5f);
    [Tooltip("Metres inside each face over which the wind fades in. 0 = a hard edge.")]
    [Min(0f)] [SerializeField] private float edgeSoftness = 0.35f;

    [Header("Flow")]
    [Tooltip("Wind speed (m/s). For a wall this is also the push-through speed: you must move against it faster than this. " +
        "The player walks at 5.25 and sprints at 7.5 (PlayerObjects: walk 7 / sprint 10 x moveSpeedScale 0.75), so 6.5 holds walking and lets sprinting through.")]
    [Min(0f)] [SerializeField] private float speed = 6.5f;
    [Tooltip("How quickly bodies take on the wind (1/s). High = a hard wall, low = a soft drifting breeze.")]
    [Min(0.1f)] [SerializeField] private float grip = 12f;
    [Tooltip("Layers whose rigidbodies the wind looks for. The player is always sampled by its controller.")]
    [SerializeField] private LayerMask affects = ~0;

    [Header("Look")]
    [SerializeField] private bool showVisuals = true;
    [Tooltip("Optional material for the streaks; otherwise one is made from Capstone/Wind/Streak.")]
    [SerializeField] private Material streakMaterial;
    [SerializeField] private Color streakColor = new Color(0.93f, 0.97f, 1f, 0.9f);
    [SerializeField] private Color inkColor = new Color(0.08f, 0.1f, 0.16f, 1f);

    private readonly Collider[] hits = new Collider[64];
    private WindVisuals visuals;

    public Vector3 Size { get => size; set { size = Vector3.Max(value, Vector3.one * 0.01f); visuals?.Rebuild(); } }
    public float Speed { get => speed; set { speed = Mathf.Max(0f, value); visuals?.Rebuild(); } }
    public float Grip { get => grip; set => grip = Mathf.Max(0.1f, value); }
    public float EdgeSoftness { get => edgeSoftness; set => edgeSoftness = Mathf.Max(0f, value); }
    public Vector3 Direction => transform.forward;
    public bool ShowVisuals => showVisuals;
    public Material StreakMaterial => streakMaterial;
    public Color StreakColor => streakColor;
    public Color InkColor => inkColor;

    /// <summary>0 outside, 1 deep inside, easing in over edgeSoftness at each face.</summary>
    public float Weight(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        Vector3 scale = transform.lossyScale;
        Vector3 half = size * 0.5f;
        float weight = 1f;
        for (int axis = 0; axis < 3; axis++)
        {
            float inset = (half[axis] - Mathf.Abs(local[axis])) * Mathf.Abs(scale[axis]);
            if (inset <= 0f) return 0f;
            if (edgeSoftness > 0f && inset < edgeSoftness)
            {
                float t = inset / edgeSoftness;
                weight *= t * t * (3f - 2f * t);
            }
        }
        return weight;
    }

    private void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);
        if (Application.isPlaying && showVisuals)
        {
            if (visuals == null) visuals = new WindVisuals(this);
            visuals.Rebuild();
        }
    }

    private void OnDisable()
    {
        active.Remove(this);
        visuals?.Destroy();
        visuals = null;
    }

    private void OnValidate()
    {
        size = Vector3.Max(size, Vector3.one * 0.01f);
        if (Application.isPlaying) visuals?.Rebuild();
    }

    private void FixedUpdate()
    {
        if (speed <= 0f) return;
        // Find bodies entering the area; each WindBody then samples every area itself until it settles.
        Vector3 scale = transform.lossyScale;
        Vector3 half = Vector3.Scale(size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        int count = Physics.OverlapBoxNonAlloc(transform.position, half, hits, transform.rotation, affects,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Rigidbody body = hits[i].attachedRigidbody;
            hits[i] = null;
            if (body == null) continue;
            if (!body.TryGetComponent(out WindBody wind))
            {
                // The player controller blends wind into its own steering.
                if (body.TryGetComponent<SmoothFirstPersonController>(out _)) continue;
                wind = body.gameObject.AddComponent<WindBody>();
            }
            if (!wind.enabled) wind.enabled = true;
        }
    }

    private void OnDrawGizmos() => DrawGizmo(false);
    private void OnDrawGizmosSelected() => DrawGizmo(true);

    private void DrawGizmo(bool selected)
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Color color = new Color(0.55f, 0.85f, 1f, selected ? 0.9f : 0.35f);
        Gizmos.color = color;
        Gizmos.DrawWireCube(Vector3.zero, size);
        if (selected)
        {
            Gizmos.color = new Color(color.r, color.g, color.b, 0.06f);
            Gizmos.DrawCube(Vector3.zero, size);
        }
        // A grid of flow arrows across the upwind face, each running the length of the box.
        Gizmos.color = color;
        int columns = Mathf.Clamp(Mathf.RoundToInt(size.x / 1.5f), 1, 6);
        int rows = Mathf.Clamp(Mathf.RoundToInt(size.y / 1.5f), 1, 4);
        float length = size.z * 0.8f;
        float head = Mathf.Min(0.4f, length * 0.3f);
        for (int x = 0; x < columns; x++)
        for (int y = 0; y < rows; y++)
        {
            Vector3 start = new Vector3((x + 0.5f) / columns - 0.5f, (y + 0.5f) / rows - 0.5f, 0f);
            start = Vector3.Scale(start, size) - Vector3.forward * (length * 0.5f);
            Vector3 end = start + Vector3.forward * length;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawLine(end, end + new Vector3(head * 0.5f, 0f, -head));
            Gizmos.DrawLine(end, end + new Vector3(-head * 0.5f, 0f, -head));
            Gizmos.DrawLine(end, end + new Vector3(0f, head * 0.5f, -head));
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}
