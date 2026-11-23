using UnityEngine;

/// <summary>Scene-local discovery uses the shared registry. Membership survives stopped creature logic.</summary>
public sealed class IslandHabitat : MonoBehaviour
{
    public MovingFrame frame;
    float nextCheck;
    SmoothFirstPersonController player;
    void Update()
    {
        if (Time.time < nextCheck || !frame) return;
        nextCheck = Time.time + .25f;
        foreach (var creature in CreatureRegistry.All)
        {
            if (!creature || creature.gameObject.scene != gameObject.scene ||
                !frame.Contains(creature.transform.position) || creature.TryGetComponent<MovementFrameMember>(out _)) continue;
            // World-rooted or already attached creatures require explicit placement, not incidental adoption.
            if (!creature.CanBeCaptured || creature.IsCarried || creature.IsBubbleCaptured || CreatureThrower.IsFlying(creature) ||
                creature is ProjectileCreature projectile && projectile.FlightState == ProjectileFlightState.Flying) continue;
            creature.gameObject.AddComponent<MovementFrameMember>();
        }
        if (!player)
            foreach (var candidate in FindObjectsByType<SmoothFirstPersonController>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
        if (player && frame.Contains(player.transform.position) && !player.TryGetComponent<MovementFrameMember>(out _))
            player.gameObject.AddComponent<MovementFrameMember>();
    }
}
