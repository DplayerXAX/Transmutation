using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A small cup of bone on the ground near the Bone Kiln that now and then lets a fire bubble float up out of it.
/// The colossus's children go after these bubbles, which shows the player that fire and the bone children belong
/// together. Bubbles from a nest pop before they could sink and root as fire flowers, unless someone holds them
/// (carried by the player, or cradled in a child's ribs). Built by BoneKiln.
/// </summary>
public sealed class KilnBubbleNest : MonoBehaviour
{
    [Tooltip("Fire bubble to copy. Empty = the one fire flowers in the scene release, or a copy of a bubble already in the scene.")]
    public FireBubble bubblePrefab;
    [Min(2f)] public float interval = 9f;
    [Range(1, 6)] public int maxAlive = 3;
    [Tooltip("Seconds a bubble from the nest lasts before it pops (if nobody holds it).")]
    [Min(2f)] public float popAfter = 24f;
    [Tooltip("Bubbles drift over towards this point (the mother's skull end) and hang about it...")]
    public Vector3 drawTo;
    [Tooltip("...but keep away from here (her heart: a bubble there would wake her by itself).")]
    public Vector3 keepFrom;
    [Min(0f)] public float drawStrength = 5f;

    private readonly List<(FireBubble bubble, float born)> alive = new List<(FireBubble, float)>();
    private float timer;

    public static KilnBubbleNest Build(Transform parent, Vector3 position, Material bone, int seed, Vector3 drawTo, Vector3 keepFrom)
    {
        var go = new GameObject("Kiln Bubble Nest");
        go.transform.SetParent(parent, true);
        go.transform.position = position;
        var nest = go.AddComponent<KilnBubbleNest>();
        // A little cup of fused bone.
        var cup = new GameObject("Cup") { layer = 7 };
        cup.transform.SetParent(go.transform, false);
        cup.transform.localScale = Vector3.one * 0.45f;
        Mesh mesh = BoneShapes.Kiln(1.8f, (seed % 97) / 97f);
        cup.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = cup.AddComponent<MeshRenderer>();
        r.sharedMaterial = bone;
        r.shadowCastingMode = ShadowCastingMode.On;
        cup.AddComponent<MeshCollider>().sharedMesh = mesh;
        nest.timer = 2f + seed % 5;
        nest.drawTo = drawTo;
        nest.keepFrom = keepFrom;
        nest.settleUntil = Time.time + 40f;
        return nest;
    }

    private float settleUntil, nextSettle;

    private void Update()
    {
        // The ground here may be built after the nest was placed: sit on it.
        if (Time.time < settleUntil && Time.time >= nextSettle)
        {
            nextSettle = Time.time + 1f;
            if (Physics.Raycast(transform.position + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 80f, 1 << 7, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform))
                transform.position = hit.point;
        }
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            FireBubble b = alive[i].bubble;
            if (b == null) { alive.RemoveAt(i); continue; }
            bool held = b.IsCarried || BoneChild.IsCradled(b);
            // Held ones are the player's (or a child's) now: no longer the nest's to pop.
            if (held) { alive.RemoveAt(i); continue; }
            if (Time.time - alive[i].born > popAfter) { Destroy(b.gameObject); alive.RemoveAt(i); }
        }
        timer -= Time.deltaTime;
        if (timer > 0f || alive.Count >= maxAlive) return;
        timer = interval;
        FireBubble prefab = ResolvePrefab();
        if (prefab == null) return;
        FireBubble bubble = Instantiate(prefab, transform.position + Vector3.up * 0.8f, Quaternion.identity);
        bubble.name = "Fire Bubble (kiln nest)";
        bubble.gameObject.SetActive(true);
        alive.Add((bubble, Time.time));
    }

    private void FixedUpdate()
    {
        // Free bubbles from the nest drift over to the mother and wander about above her, away from her heart.
        foreach (var (bubble, born) in alive)
        {
            if (bubble == null || bubble.IsCarried || BoneChild.IsCradled(bubble)) continue;
            var rb = bubble.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic) continue;
            float wander = Time.time * 0.3f + born;
            Vector3 goal = drawTo + new Vector3(Mathf.Sin(wander) * 6f, 3.5f + Mathf.Sin(wander * 1.7f), Mathf.Cos(wander * 0.8f) * 6f);
            Vector3 pull = goal - rb.position;
            Vector3 push = rb.position - keepFrom;
            push.y = 0f;
            Vector3 force = Vector3.ClampMagnitude(pull * 0.6f - rb.linearVelocity * 0.4f, drawStrength);
            if (push.magnitude < 9f) force += push.normalized * drawStrength * 1.5f;
            // Hold it near the goal height instead of rising out of reach.
            force.y = Mathf.Clamp((goal.y - rb.position.y) * 0.6f - rb.linearVelocity.y * 0.5f, -2f, 2f);
            rb.AddForce(force, ForceMode.Acceleration);
        }
    }

    private FireBubble ResolvePrefab()
    {
        if (bubblePrefab != null) return bubblePrefab;
        // The bubble prefab a fire flower releases.
        var field = typeof(FireFlower).GetField("bubblePrefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (FireFlower flower in FindObjectsByType<FireFlower>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (field?.GetValue(flower) is FireBubble fromFlower && fromFlower != null) return bubblePrefab = fromFlower;
        // Otherwise a copy of one already in the scene.
        return FindFirstObjectByType<FireBubble>();
    }
}
