using UnityEngine;

/// <summary>
/// Music channel for a map region: a vertical cylinder around this object between two world heights.
/// Full inside, fading out over fadeDistance outside. Pair it with a layer marked replacesBase so the
/// region's music takes the base track's place.
/// </summary>
public sealed class RegionMusicChannel : MusicChannel
{
    [Min(1f)] public float radius = 50f;
    [Tooltip("World heights the region spans.")]
    public Vector2 heightRange = new Vector2(-50f, 80f);
    [Min(0.1f)] public float fadeDistance = 15f;

    public override float Weight(Vector3 listener)
    {
        Vector3 c = transform.position;
        float side = Mathf.Max(0f, new Vector2(listener.x - c.x, listener.z - c.z).magnitude - radius);
        float up = Mathf.Max(0f, Mathf.Max(heightRange.x - listener.y, listener.y - heightRange.y));
        return AudioManager.Falloff(new Vector2(side, up).magnitude, 0f, fadeDistance);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.6f);
        Vector3 c = transform.position;
        for (int i = 0; i < 2; i++)
        {
            float y = i == 0 ? heightRange.x : heightRange.y;
            Vector3 prev = new Vector3(c.x + radius, y, c.z);
            for (int a = 1; a <= 32; a++)
            {
                float t = a / 32f * Mathf.PI * 2f;
                Vector3 next = new Vector3(c.x + Mathf.Cos(t) * radius, y, c.z + Mathf.Sin(t) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
