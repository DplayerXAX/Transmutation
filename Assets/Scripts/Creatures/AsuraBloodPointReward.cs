using UnityEngine;

[DisallowMultipleComponent]
public sealed class AsuraBloodPointReward : MonoBehaviour
{
    [SerializeField, Min(0)] private int bloodPointValue = 25;
    [SerializeField, Min(0)] private float collectionRadius = 12f;
    private Creature donor;
    private bool awarded;

    internal static bool IsCombatDeath(Creature dead) => Application.isPlaying && dead.gameObject.scene.isLoaded
        && (dead.Polarity == CreaturePolarity.Light || dead.Polarity == CreaturePolarity.Dark)
        && (dead.DeathCause == CreatureDeathCause.Damage || dead is RollingAsura roller && roller.Detonated);

    private void Awake() => donor = GetComponent<Creature>();
    private void OnEnable() => donor.Died += OnDeath;
    private void OnDisable() => donor.Died -= OnDeath;

    private void OnDeath(Creature dead)
    {
        if (awarded || !IsCombatDeath(dead)) return;
        awarded = true;
        foreach (var player in FindObjectsByType<PlayerCreatureStats>(FindObjectsSortMode.None))
            if (player.gameObject.scene == gameObject.scene &&
                (player.AimPosition - dead.AimPosition).sqrMagnitude <= collectionRadius * collectionRadius)
                player.TryAddBloodPoints(dead.Polarity, bloodPointValue);
    }
}
