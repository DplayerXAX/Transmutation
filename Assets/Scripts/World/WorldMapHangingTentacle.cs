using System.Collections;
using UnityEngine;

/// <summary>Holds a scene-authored tentacle beneath a generated sky stone until it is picked up.</summary>
[RequireComponent(typeof(TentacleCreature))]
public sealed class WorldMapHangingTentacle : MonoBehaviour
{
    public SkyIslands Islands;
    public int StoneIndex;
    private TentacleCreature creature;
    private int arm = -1;

    private IEnumerator Start()
    {
        creature = GetComponent<TentacleCreature>();
        for (int attempt = 0; attempt < 30; attempt++)
        {
            if (Islands != null && Islands.Built != null && StoneIndex >= 0 && StoneIndex < Islands.Built.childCount)
            {
                Transform stone = Islands.Built.GetChild(StoneIndex);
                var collider = stone.GetComponent<Collider>();
                if (collider != null)
                {
                    Vector3 from = new Vector3(stone.position.x, collider.bounds.min.y - 2f, stone.position.z);
                    if (collider.Raycast(new Ray(from, Vector3.up), out RaycastHit hit, collider.bounds.size.y + 4f))
                    {
                        transform.position = hit.point + Vector3.down * 1.35f;
                        creature.Suspend(true);
                        arm = creature.PinArm(hit.point, stone);
                        if (arm >= 0) yield break;
                    }
                }
            }
            yield return null;
        }
        Debug.LogWarning("Hanging tentacle could not find its sky stone underside.", this);
    }

    private void Update()
    {
        if (arm < 0 || creature == null || !creature.IsCarried) return;
        creature.UnpinArm(arm);
        creature.Suspend(false);
        enabled = false;
    }
}
