using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[CreateAssetMenu(menuName = "Reflection/Exhibit Catalog")]
public sealed class ReflectionCatalog : ScriptableObject
{
    public enum Recipe
    {
        Prefab = 0, Bellows = 2, Snail = 3, Colossus = 4, FireEater = 6, Bees = 7,
        Water = 8, BatBugs = 9, Trees = 10, Squirrel = 11
    }
    [Serializable]
    public sealed class Definition
    {
        public string id;
        public string creatureType;
        public GameObject prefab;
        public GameObject support;
        public GameObject product;
        public Recipe recipe;
        [Min(1)] public int count = 1;
        [Min(1f)] public float radius = 2f;
        [Min(2f)] public float height = 4f;
    }

    [SerializeField] private Definition[] exhibits;
    public IReadOnlyList<Definition> Exhibits => exhibits;
    public static ReflectionCatalog Load() => Resources.Load<ReflectionCatalog>("ReflectionCatalog");

    public Definition Find(string id) => exhibits == null ? null : Array.Find(exhibits, item => item != null && item.id == id);

    private void OnValidate()
    {
        if (exhibits == null) return;
        var ids = new HashSet<string>();
        var types = new HashSet<string>();
        foreach (Definition exhibit in exhibits)
        {
            if (string.IsNullOrWhiteSpace(exhibit.id) || !ids.Add(exhibit.id) ||
                string.IsNullOrWhiteSpace(exhibit.creatureType) || !types.Add(exhibit.creatureType))
                Debug.LogError("Duplicate or missing reflection exhibit identity.", this);
            if ((exhibit.recipe == Recipe.Prefab || exhibit.recipe == Recipe.Trees ||
                exhibit.recipe == Recipe.BatBugs || exhibit.recipe == Recipe.Squirrel) && exhibit.prefab == null)
                Debug.LogError("Missing reflection exhibit prefab: " + exhibit.id, this);
        }
    }
}

[Serializable]
public sealed class ReflectionProgress
{
    [SerializeField] private int version = 1;
    [SerializeField] private List<string> unlocked = new();
    public IReadOnlyList<string> Unlocked => unlocked;
    public event Action Changed;
    private static string SavePath => Path.Combine(ReflectionStorage.Root, "exhibits-v1.json");

    public static ReflectionProgress Load()
    {
        try
        {
            if (File.Exists(SavePath))
            {
                var saved = JsonUtility.FromJson<ReflectionProgress>(File.ReadAllText(SavePath));
                if (saved != null && saved.version == 1 && saved.unlocked != null) return saved;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not read reflection discoveries: " + exception.Message);
        }
        return new ReflectionProgress();
    }

    public void Discover(ReflectionCatalog catalog, RestSpot seat)
    {
        var observed = new HashSet<string>();
        float radiusSquared = seat.DiscoveryRadius * seat.DiscoveryRadius;
        foreach (Creature creature in UnityEngine.Object.FindObjectsByType<Creature>(FindObjectsSortMode.None))
        {
            if (creature.gameObject.scene != seat.gameObject.scene || creature.IsDead ||
                creature.IsConsumptionPending || !creature.isActiveAndEnabled ||
                (creature.transform.position - seat.transform.position).sqrMagnitude > radiusSquared) continue;
            observed.Add(creature.GetType().Name);
        }

        var discoveries = new List<string>();
        foreach (var exhibit in catalog.Exhibits)
            if (exhibit != null && observed.Contains(exhibit.creatureType) && !unlocked.Contains(exhibit.id))
                discoveries.Add(exhibit.id);
        if (discoveries.Count == 0) return;
        Unlock(catalog, discoveries);
    }

    /// <summary>Grant current catalog identities and notify active views once per batch.</summary>
    public int Unlock(ReflectionCatalog catalog, IEnumerable<string> ids)
    {
        if (catalog == null || ids == null) return 0;
        var discoveries = new List<string>();
        var known = new HashSet<string>(unlocked, StringComparer.Ordinal);
        foreach (string id in ids)
            if (!string.IsNullOrWhiteSpace(id) && catalog.Find(id) != null && known.Add(id)) discoveries.Add(id);
        if (discoveries.Count == 0) return 0;
        discoveries.Sort(StringComparer.Ordinal);
        unlocked.AddRange(discoveries);
        try
        {
            Save();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not save reflection discoveries: " + exception.Message);
        }
        Changed?.Invoke();
        return discoveries.Count;
    }

    private void Save()
    {
        Directory.CreateDirectory(ReflectionStorage.Root);
        string temporary = SavePath + ".tmp";
        File.WriteAllText(temporary, JsonUtility.ToJson(this, true));
        if (File.Exists(SavePath)) File.Replace(temporary, SavePath, null);
        else File.Move(temporary, SavePath);
    }

#if UNITY_EDITOR
    public static int UnlockAllForEditor(ReflectionCatalog catalog)
    {
        var progress = Load();
        foreach (var exhibit in catalog.Exhibits)
        {
            if (exhibit != null && !string.IsNullOrWhiteSpace(exhibit.id) &&
                !progress.unlocked.Contains(exhibit.id))
                progress.unlocked.Add(exhibit.id);
        }
        progress.Save();
        return progress.unlocked.Count;
    }

    public static void ClearForEditor() => new ReflectionProgress().Save();
#endif
}
