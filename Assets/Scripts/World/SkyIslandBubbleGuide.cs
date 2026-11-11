using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bubbles shot by the bubble flowers on the sky islands drift out over the gap to the next island and hang there,
/// so there is always something to hop through between islands that are too far apart to jump.
/// Each flower has a point in its gap (a share of the way across); its newest bubble is tugged gently towards it
/// while it rises. A bubble with someone inside is left alone. Set up by Tools > Capstone > World > Add Sky Islands.
/// </summary>
public sealed class SkyIslandBubbleGuide : MonoBehaviour
{
    [System.Serializable]
    public sealed class Lane
    {
        public BubbleFlower flower;
        [Tooltip("Where its bubbles should hang (world space; height is a hint, the bubble's own rise decides).")]
        public Vector3 target;
    }

    public List<Lane> lanes = new List<Lane>();
    [Tooltip("Sideways drift speed towards the gap point (m/s).")]
    [Min(0.1f)] public float driftSpeed = 1.6f;
    [Tooltip("Seconds a bubble is guided after it leaves the flower.")]
    [Min(1f)] public float guideSeconds = 14f;

    private readonly Dictionary<Lane, (TrappingBubble bubble, float since)> tracked = new Dictionary<Lane, (TrappingBubble, float)>();

    private void FixedUpdate()
    {
        foreach (Lane lane in lanes)
        {
            if (lane.flower == null) continue;
            TrappingBubble newest = lane.flower.LastShot;
            if (newest != null && (!tracked.TryGetValue(lane, out var t) || t.bubble != newest)) tracked[lane] = (newest, Time.time);
            if (!tracked.TryGetValue(lane, out var current) || current.bubble == null) continue;
            if (Time.time - current.since > guideSeconds) continue;
            TrappingBubble bubble = current.bubble;
            if (bubble.IsOccupied) continue;
            Vector3 offset = lane.target - bubble.transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance < 0.3f) continue;
            bubble.Tug(offset / distance * Mathf.Min(driftSpeed, distance * 0.8f));
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.7f, 0.9f, 1f, 0.8f);
        foreach (Lane lane in lanes)
            if (lane.flower != null) { Gizmos.DrawLine(lane.flower.transform.position, lane.target); Gizmos.DrawWireSphere(lane.target, 0.6f); }
    }
}
