using UnityEngine;

/// <summary>
/// Invisible walls just inside the edge of the ProceduralWorld, so the player cannot climb over the rim
/// cliffs (or walk along a landmark) and fall off the map. They are on the Default layer, which the
/// climbing hands do not grip. Added automatically to every scene that has a ProceduralWorld.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldEdgeWalls : MonoBehaviour
{
    [Tooltip("Where the walls stand, as a share of the half world size (the rim cliffs rise from about 0.82).")]
    [Range(0.85f, 1f)] [SerializeField] private float edge = 0.95f;
    [Min(1f)] [SerializeField] private float thickness = 4f;
    [Tooltip("Height of the walls above and below the world's origin.")]
    [Min(10f)] [SerializeField] private float height = 260f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AddToWorld()
    {
        ProceduralWorld world = FindFirstObjectByType<ProceduralWorld>();
        if (world != null && world.GetComponent<WorldEdgeWalls>() == null) world.gameObject.AddComponent<WorldEdgeWalls>();
    }

    private void Start()
    {
        ProceduralWorld world = GetComponent<ProceduralWorld>();
        if (world == null) return;
        Vector2 half = world.WorldSize * 0.5f * edge;
        var root = new GameObject("World Edge Walls").transform;
        root.SetParent(transform, false);
        Wall(root, new Vector3(half.x + thickness * 0.5f, 0f, 0f), new Vector3(thickness, height, half.y * 2f + thickness * 2f));
        Wall(root, new Vector3(-half.x - thickness * 0.5f, 0f, 0f), new Vector3(thickness, height, half.y * 2f + thickness * 2f));
        Wall(root, new Vector3(0f, 0f, half.y + thickness * 0.5f), new Vector3(half.x * 2f + thickness * 2f, height, thickness));
        Wall(root, new Vector3(0f, 0f, -half.y - thickness * 0.5f), new Vector3(half.x * 2f + thickness * 2f, height, thickness));
    }

    private static void Wall(Transform parent, Vector3 centre, Vector3 size)
    {
        var go = new GameObject("Wall") { layer = 0 };
        go.transform.SetParent(parent, false);
        go.transform.localPosition = centre;
        go.AddComponent<BoxCollider>().size = size;
    }
}
