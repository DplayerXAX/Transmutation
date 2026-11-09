using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fires a set of bubble flowers in a staggered rhythm instead of each on its own random timer: every flower
/// fires once per period at its own offset, and neighbours never fire together, so the air above terraced
/// ground always holds bubbles at many heights to ride and jump between. Stops while the player is far away
/// or a flower is not allowed to emit (LeafMapEcology's bubble budget).
/// </summary>
public sealed class BubbleCascade : MonoBehaviour
{
    [SerializeField] private List<BubbleFlower> flowers = new List<BubbleFlower>();
    [Tooltip("Each flower fires once in this many seconds.")]
    [Min(1f)] [SerializeField] private float period = 7f;
    [Tooltip("The cascade only runs while the player is this close.")]
    [Min(1f)] [SerializeField] private float playerRange = 110f;
    [Tooltip("No flower fires while this many bubbles are already in the scene (they are the costly part).")]
    [Min(1)] [SerializeField] private int maxBubbles = 45;

    private float clock;
    private int[] lastCycle;
    private Transform player;
    private float nextCount;
    private int bubbles;

    public void Configure(List<BubbleFlower> cascade, float seconds)
    {
        flowers = cascade;
        period = seconds;
    }

    private void Start()
    {
        lastCycle = new int[flowers.Count];
        for (int i = 0; i < lastCycle.Length; i++) lastCycle[i] = -1;
    }

    private void Update()
    {
        if (player == null)
        {
            var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
            if (carrier == null) return;
            player = carrier.transform;
        }
        clock += Time.deltaTime;
        if (Time.time >= nextCount)
        {
            nextCount = Time.time + 0.5f;
            bubbles = FindObjectsByType<TrappingBubble>(FindObjectsSortMode.None).Length;
        }
        for (int i = 0; i < flowers.Count; i++)
        {
            var flower = flowers[i];
            if (flower == null || !flower.AmbientEmissionAllowed) continue;
            if ((flower.transform.position - player.position).sqrMagnitude > playerRange * playerRange) continue;
            // Golden-ratio offsets: consecutive flowers (sorted by height when built) fire far apart in time.
            float phase = (i * 0.618034f) % 1f;
            int cycle = Mathf.FloorToInt(clock / period - phase);
            if (cycle <= lastCycle[i]) continue;
            lastCycle[i] = cycle;
            // Over the limit the turn is skipped, not saved up, so the rhythm stays even.
            if (bubbles >= maxBubbles) continue;
            if (flower.EmitGroundBubble()) bubbles++;
        }
    }
}
