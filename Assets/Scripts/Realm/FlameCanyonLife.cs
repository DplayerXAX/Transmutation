using UnityEngine;

/// <summary>
/// Flame Canyon's runtime-only life: the bone children and the charred nest that feeds them fire bubbles.
/// Both build their own meshes in play mode, so they cannot be saved into the scene like the other creatures.
/// Placed and filled in by Tools > Capstone > Flame Canyon > Build New Scene.
/// </summary>
public sealed class FlameCanyonLife : MonoBehaviour
{
    [SerializeField] private ProceduralWorld world;
    [SerializeField] private Material boneMaterial;
    [Tooltip("Centre of the Bone Hollow floor: the children loop round it and nest bubbles drift over it.")]
    [SerializeField] private Vector3 hollowCentre;
    [Range(0, 6)] [SerializeField] private int boneChildren = 3;
    [Tooltip("Radius of the children's loop. Keep it inside the hollow floor.")]
    [Min(8f)] [SerializeField] private float loopRadius = 9f;
    [Tooltip("Where the nest stands (it settles on the nearest flat ground).")]
    [SerializeField] private Vector3 nestPosition;
    [SerializeField] private int seed = 7;

    private bool spawned;
    private float wait;

    public void Setup(ProceduralWorld proceduralWorld, Material bone, Vector3 centre, Vector3 nest)
    {
        world = proceduralWorld;
        boneMaterial = bone;
        hollowCentre = centre;
        nestPosition = nest;
    }

    private void Update()
    {
        if (spawned) return;
        if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
        if (world == null || !world.LayoutReady) return;
        // Bone children walk on the ground: wait for it (or give up waiting after a while, like the Basin does).
        wait += Time.deltaTime;
        if (!world.GenerationComplete && !world.IsRegionReady(hollowCentre, loopRadius + 6f) && wait < 20f) return;
        spawned = true;

        Shader comb = Shader.Find("Capstone/Landmarks/BoneComb");
        Material skin = comb != null ? new Material(comb) { name = "Flame Canyon Bone Ribs (runtime)" } : boneMaterial;
        for (int c = 0; c < boneChildren; c++)
        {
            float angle = c / (float)Mathf.Max(1, boneChildren) * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 spot = hollowCentre + dir * loopRadius;
            if (Physics.Raycast(spot + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 20f, 1 << world.TerrainLayer, QueryTriggerInteraction.Ignore))
                spot = hit.point;
            BoneChild.Spawn(transform, spot, Quaternion.LookRotation(-dir).eulerAngles.y, seed * 31 + c, boneMaterial, skin, hollowCentre);
        }
        // The nest keeps its bubbles away from "keepFrom" (the mother's heart at the kiln); there is no mother here.
        KilnBubbleNest.Build(transform, nestPosition, boneMaterial, seed, hollowCentre, hollowCentre + Vector3.down * 1000f);
    }
}
