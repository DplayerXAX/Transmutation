using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One dormant infection per host. Additional seed meals accumulate tags, not broods.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Creature))]
public sealed class CorruptSeedInfection : MonoBehaviour
{
    private const int MinimumBroodSize = 3;
    private const int MaximumBroodSize = 5;
    private const float ScatterRadius = 0.5f;
    private const float SpawnHeight = 0.25f;

    private Creature host;
    private Creature.FoodTag accumulatedSeedTags;
    private CorruptSeed offspringPrefab;
    private bool initialized;

    public Creature.FoodTag AccumulatedSeedTags => accumulatedSeedTags;
    public CorruptSeed OffspringPrefab => offspringPrefab;

    private void Awake()
    {
        host = GetComponent<Creature>();
        host.Died += OnHostDied;
    }

    internal void Infect(Creature.FoodTag tags, CorruptSeed prefab)
    {
        accumulatedSeedTags |= tags | Creature.FoodTag.Plant;
        if (initialized) return;
        offspringPrefab = prefab;
        initialized = true;
    }

    private void OnHostDied(Creature deadHost)
    {
        if (!Application.isPlaying || !initialized) return;
        if (offspringPrefab == null)
        {
            Debug.LogError("Corrupt Seed infection needs an offspring prefab in its seed definition.", this);
            return;
        }

        Creature.FoodTag tags = accumulatedSeedTags | deadHost.FoodTags | Creature.FoodTag.Plant;
        Vector3 position = deadHost.transform.position + Vector3.up * SpawnHeight;
        int count = Random.Range(MinimumBroodSize, MaximumBroodSize + 1);
        GameObject staging = new GameObject("Corrupt Seed Brood Staging");
        staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, deadHost.gameObject.scene);
        try
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = Random.insideUnitCircle * ScatterRadius;
                CorruptSeed.Spawn(offspringPrefab, staging.transform,
                    position + new Vector3(offset.x, 0f, offset.y), tags);
            }
        }
        finally { Destroy(staging); }
    }

    private void OnDestroy()
    {
        if (host != null) host.Died -= OnHostDied;
    }
}
