using UnityEngine;

/// <summary>
/// The big seed at the spire's crown. It floats high above the battlements so it can be seen from the ground,
/// breathes slowly, and every few seconds cracks apart: its pieces burst out, hang flickering in the air for a
/// moment, then snap back together. When the player reaches the top it sinks down to the beam to be taken.
/// </summary>
public sealed class SeedEruption : MonoBehaviour
{
    [SerializeField] private LandmarkPickup pickup;
    [SerializeField] private Transform core;
    [Tooltip("Height of the seed itself (metres).")]
    [SerializeField] private float size = 4.5f;
    [Tooltip("How far above the beam it floats until the player is up there (metres).")]
    [SerializeField] private float floatHeight = 10f;
    [Tooltip("Its size once it has come down to the beam, as a share of its floating size.")]
    [Range(0.2f, 1f)] [SerializeField] private float nearScale = 0.5f;

    [Header("Cracking")]
    [SerializeField] private int shardCount = 8;
    [Tooltip("Seconds between cracks (random in this range).")]
    [SerializeField] private Vector2 crackInterval = new Vector2(2.2f, 4.5f);
    [Tooltip("How far the pieces burst out, as a share of the seed's height.")]
    [SerializeField] private Vector2 burstDistance = new Vector2(0.35f, 0.8f);
    [Tooltip("Size of each piece, as a share of the whole seed.")]
    [SerializeField] private Vector2 shardSize = new Vector2(0.3f, 0.55f);

    private struct Shard
    {
        public Transform transform;
        public Vector3 direction;
        public Quaternion spin;
        public float distance, scale, flicker;
    }

    private Shard[] shards;
    private Vector3 coreScale;
    private Vector3 coreHome;
    private Vector3 visualCentre; // seed centre in the core's local space
    private Vector3 lowHome, highHome;
    private float lowered;
    private Transform player;
    private float nextCrack;
    private float crackTime = -1f;
    private float glitchUntil;
    private Vector3 glitchOffset;

    /// <summary>Sets everything up (called by HangingSpire right after it makes the pickup).</summary>
    public void Init(LandmarkPickup seedPickup, Transform seedCore, float height, float hoverAbove)
    {
        pickup = seedPickup;
        core = seedCore;
        size = height;
        floatHeight = hoverAbove;
    }

    private void Start()
    {
        if (pickup == null || core == null) { enabled = false; return; }
        coreScale = core.localScale;
        coreHome = core.localPosition;
        visualCentre = core.InverseTransformPoint(pickup.transform.position);
        // Down on the beam it is smaller, so its middle sits lower.
        lowHome = pickup.transform.position - Vector3.up * (size * 0.5f * (1f - nearScale));
        highHome = lowHome + Vector3.up * floatHeight;
        pickup.SetHome(highHome);

        shards = new Shard[shardCount];
        for (int i = 0; i < shards.Length; i++)
        {
            GameObject copy = Instantiate(core.gameObject, transform);
            copy.name = "Shard";
            foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(behaviour);
            copy.SetActive(false);
            shards[i].transform = copy.transform;
        }
        nextCrack = Time.time + 1.5f;

        var controller = FindFirstObjectByType<SmoothFirstPersonController>();
        if (controller != null) player = controller.transform;
    }

    private bool SeedThere => pickup != null && pickup.isActiveAndEnabled && pickup.transform.localScale.x > 0.9f;

    private void Update()
    {
        if (shards == null) return;
        if (!SeedThere)
        {
            foreach (Shard s in shards) if (s.transform != null) s.transform.gameObject.SetActive(false);
            enabled = false;
            return;
        }
        float time = Time.time;
        float deltaTime = Time.deltaTime;

        // Sink to the beam while the player is up on the crown, rise again when they leave.
        bool playerUp = player != null
            && player.position.y > lowHome.y - 3f
            && new Vector2(player.position.x - lowHome.x, player.position.z - lowHome.z).magnitude < 9f;
        lowered = Mathf.MoveTowards(lowered, playerUp ? 1f : 0f, deltaTime * 0.35f);
        pickup.SetHome(Vector3.Lerp(highHome, lowHome, Mathf.SmoothStep(0f, 1f, lowered)));

        if (crackTime < 0f && time >= nextCrack) StartCrack();

        float near = Mathf.Lerp(1f, nearScale, Mathf.SmoothStep(0f, 1f, lowered));
        float swell = near * (1f + 0.04f * Mathf.Sin(time * 1.7f));
        Vector3 offset = Vector3.zero;
        if (crackTime >= 0f)
        {
            float t = time - crackTime;
            // 0-0.15 s: shudder and swell. 0.15-0.3: burst. 0.3-0.75: hang, flickering. 0.75-1.0: snap back.
            if (t < 0.15f)
            {
                swell *= 1f + t / 0.15f * 0.15f;
                offset = Random.insideUnitSphere * (size * 0.03f);
            }
            else if (t < 1f)
            {
                float open = t < 0.3f ? EaseOut((t - 0.15f) / 0.15f)
                    : t < 0.75f ? 1f
                    : 1f - EaseIn((t - 0.75f) / 0.25f);
                // The whole seed shrinks to a small bright knot while its pieces are out.
                swell *= Mathf.Lerp(1f, 0.35f, open);
                PlaceShards(open, t, near);
            }
            else
            {
                // Snap back with a little overshoot.
                float back = t - 1f;
                swell *= 1f + 0.18f * Mathf.Exp(-back * 8f) * Mathf.Cos(back * 30f);
                if (back > 0.4f)
                {
                    crackTime = -1f;
                    nextCrack = time + Random.Range(crackInterval.x, crackInterval.y);
                }
                HideShards();
            }
        }
        else if (time > glitchUntil + 0.8f && Random.value < deltaTime * 0.4f)
        {
            // Between cracks it now and then glitches a short way sideways.
            glitchUntil = time + Random.Range(0.05f, 0.12f);
            glitchOffset = Random.insideUnitSphere * (size * 0.08f);
        }
        if (time < glitchUntil) offset += glitchOffset;

        core.localScale = coreScale * swell;
        // Keep the seed's middle in place while it swells.
        core.localPosition = coreHome * swell + offset;
    }

    private void StartCrack()
    {
        crackTime = Time.time;
        for (int i = 0; i < shards.Length; i++)
        {
            // Spread the pieces round the seed, a few more sideways than up or down.
            Vector3 direction = Random.onUnitSphere;
            direction.y *= 0.6f;
            shards[i].direction = direction.normalized;
            shards[i].distance = Random.Range(burstDistance.x, burstDistance.y) * size;
            shards[i].scale = Random.Range(shardSize.x, shardSize.y);
            shards[i].spin = Random.rotationUniform;
            shards[i].flicker = Random.value * 10f;
        }
    }

    private void PlaceShards(float open, float t, float near)
    {
        Vector3 centre = pickup.transform.position;
        for (int i = 0; i < shards.Length; i++)
        {
            ref Shard s = ref shards[i];
            // While hanging, pieces blink out now and then like a broken signal.
            bool show = open > 0.02f && Mathf.PerlinNoise(s.flicker, t * 14f) > 0.28f;
            s.transform.gameObject.SetActive(show);
            if (!show) continue;
            float scale = s.scale * near * Mathf.Lerp(0.6f, 1f, open);
            Quaternion rotation = Quaternion.Slerp(core.rotation, s.spin * core.rotation, open * 0.6f);
            Vector3 position = centre + s.direction * (s.distance * near * open) + Random.insideUnitSphere * (size * 0.01f);
            s.transform.localScale = coreScale * scale;
            s.transform.SetPositionAndRotation(position - rotation * Vector3.Scale(visualCentre, coreScale * scale), rotation);
        }
    }

    private void HideShards()
    {
        foreach (Shard s in shards) if (s.transform.gameObject.activeSelf) s.transform.gameObject.SetActive(false);
    }

    private static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
    private static float EaseIn(float x) { x = Mathf.Clamp01(x); return x * x * x; }

    /// <summary>Size of the mesh's bounds after it is turned by 'rotation' (x = width, y = height).</summary>
    public static Vector3 UprightSize(Mesh mesh, Quaternion rotation)
    {
        if (mesh == null) return Vector3.one;
        Bounds b = mesh.bounds;
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            corner = rotation * corner;
            min = Vector3.Min(min, corner);
            max = Vector3.Max(max, corner);
        }
        Vector3 size = max - min;
        return new Vector3(Mathf.Max(size.x, size.z), size.y, Mathf.Max(size.x, size.z));
    }
}
