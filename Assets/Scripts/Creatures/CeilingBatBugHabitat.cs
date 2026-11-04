using UnityEngine;

/// <summary>Passive authoring data. No population registry, tick, or reproduction service.</summary>
public sealed class CeilingBatBugHabitat : MonoBehaviour
{
    public ProceduralWorld world;
    public CeilingBatBug bugPrefab;
    public CeilingBatBugEgg eggPrefab;
    public Bounds localBounds = new Bounds(Vector3.zero, new Vector3(34f, 24f, 26f));
    [Min(1f)] public float roamRadius = 18f;
    [Min(1f)] public float sensingRadius = 10f;
    [Min(1)] public int localLivingLimit = 7;
    public Vector2 lifetime = new Vector2(55f, 75f);
    public Vector2 layingAge = new Vector2(15f, 30f);
    public Vector2 incubation = new Vector2(13f, 17f);
    [Tooltip("Unclaimed corpses retire after this many seconds; pickup and Compost absorption protect them.")]
    public float corpseRetentionSeconds = 180f;
    public Vector3 Home => transform.position;
    public bool Contains(Vector3 p) => localBounds.Contains(transform.InverseTransformPoint(p));
    public bool Ready(Vector3 p) => world == null || world.IsRegionReady(p, 1.5f);
    private void OnDrawGizmosSelected()
    { Gizmos.matrix = transform.localToWorldMatrix; Gizmos.color = Color.yellow; Gizmos.DrawWireCube(localBounds.center, localBounds.size); }
}
