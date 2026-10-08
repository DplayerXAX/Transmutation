using UnityEngine;

/// <summary>
/// Music channel for climbing a structure: on while the player's hands grip or feet stand on
/// colliders under this object. Put it on the Hanging Spire.
/// </summary>
public sealed class ClimbMusicChannel : MusicChannel
{
    [Tooltip("How far around a hand or below the feet to look for this structure's colliders.")]
    [Min(0.1f)] public float probeRadius = 0.5f;
    [Min(0.05f)] public float checkInterval = 0.25f;

    private HandClimber climber;
    private Transform player;
    private float nextCheck;
    private bool onStructure;
    private readonly Collider[] hits = new Collider[16];

    private void Reset() => layerEvent = "BGM_climb";

    public override float Weight(Vector3 listener)
    {
        if (Time.unscaledTime >= nextCheck)
        {
            nextCheck = Time.unscaledTime + checkInterval;
            onStructure = Check();
        }
        return onStructure ? 1f : 0f;
    }

    private bool Check()
    {
        if (climber == null)
        {
            climber = FindFirstObjectByType<HandClimber>();
            if (climber == null) return false;
            player = climber.transform;
        }

        if (climber.IsClimbing)
        {
            for (int i = 0; i < 2; i++)
                if (climber.GetHand(i, out Vector3 hold, out _, out _) && Touches(hold)) return true;
            return false;
        }

        // Standing on a ledge of the structure still counts.
        return Physics.Raycast(player.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 2.5f,
                   ~0, QueryTriggerInteraction.Ignore) && hit.collider.transform.IsChildOf(transform);
    }

    private bool Touches(Vector3 point)
    {
        int count = Physics.OverlapSphereNonAlloc(point, probeRadius, hits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (hits[i].transform.IsChildOf(transform)) return true;
        return false;
    }
}
