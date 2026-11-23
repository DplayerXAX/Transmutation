using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class DimensionalSchool : MonoBehaviour, ICreatureRelevancePolicy
{
    static readonly Unity.Profiling.ProfilerMarker activationMarker = new("SeaAngel.Activation");
    static readonly Dictionary<int, DimensionalSchool> scenes = new();
    readonly Dictionary<(int frame, Vector3Int cell), HashSet<SeaAngelCreature>> cells = new();
    readonly Dictionary<SeaAngelCreature, (int frame, Vector3Int cell)> locations = new();
    readonly HashSet<SeaAngelCreature> newlyPlaced = new();
    internal void Register(SeaAngelCreature fish)
    {
        CreatureSimulation.SourceDriven(fish, true);
        Reindex(fish);
        newlyPlaced.Add(fish);
    }
    void FinishPlacement()
    {
        foreach (var fish in newlyPlaced) Reindex(fish);
        newlyPlaced.Clear();
    }
    readonly List<SeaAngelCreature> retire = new();
    readonly List<SeaAngelCreature> nearby = new();
    internal readonly HashSet<SeaAngelCreature> AwakeFish = new();
    internal readonly HashSet<SeaAngelCreature> PhysicalFish = new();
    internal readonly HashSet<DimensionStone> Stones = new();
    internal readonly HashSet<FishingPoleCreature> Poles = new();
    [SerializeField] float refreshInterval = .25f, sleepDelay = 2;
    public int AwakeCount => AwakeFish.Count;
    public int RegisteredCount => locations.Count;
    public PlayerCreatureCarrier Player { get; private set; }
    CreatureSimulationScheduler scheduler;
    public float RelevanceInterval=>refreshInterval;
    void Start()=>RegisterScheduler();
    void OnEnable(){if(Application.isPlaying)RegisterScheduler();}
    void RegisterScheduler(){scheduler=CreatureSimulationScheduler.For(gameObject.scene);if(scheduler)scheduler.Register(this,this);}
    void OnDisable(){if(scheduler)scheduler.Unregister(this);}
    int sceneKey;
    const float CellSize = 12;
    public static DimensionalSchool Existing(Component item) =>
        scenes.TryGetValue(item.gameObject.scene.handle, out var school) ? school : null;
    public static DimensionalSchool For(Component item)
    {
        var school = Existing(item);
        if (school != null) return school;
        var go = new GameObject("Dimensional School");
        var scene = item.gameObject.scene;
        SceneManager.MoveGameObjectToScene(go, scene);
        school = go.AddComponent<DimensionalSchool>();
        school.sceneKey = scene.handle;
        scenes[scene.handle] = school;
        return school;
    }
    void OnDestroy() { if (scenes.TryGetValue(sceneKey, out var current) && current == this) scenes.Remove(sceneKey); }
    public void Reindex(SeaAngelCreature fish)
    {
        var frame = fish.TryGetComponent<MovementFrameMember>(out var member) ? member.Frame : null;
        Vector3 point = frame ? frame.transform.InverseTransformPoint(fish.transform.position) : fish.transform.position;
        var key = (frame ? frame.GetInstanceID() : 0, Vector3Int.FloorToInt(point / CellSize));
        if (locations.TryGetValue(fish, out var old))
        {
            if (old == key) return;
            RemoveCell(fish, old);
        }
        locations[fish] = key;
        if (!cells.TryGetValue(key, out var bucket)) cells.Add(key, bucket = new());
        bucket.Add(fish);
    }
    void RemoveCell(SeaAngelCreature fish, (int frame, Vector3Int cell) key)
    {
        var bucket = cells[key]; bucket.Remove(fish);
        if (bucket.Count == 0) cells.Remove(key);
    }
    internal void Remove(SeaAngelCreature fish)
    {
        PhysicalFish.Remove(fish);
        newlyPlaced.Remove(fish);
        if (locations.Remove(fish, out var key)) RemoveCell(fish, key);
        AwakeFish.Remove(fish); CreatureSimulation.SourceDriven(fish, false);
    }
    internal void Wake(SeaAngelCreature fish)
    {
        AwakeFish.Add(fish); CreatureSimulation.Wake(fish, sleepDelay);
    }
    void Query(Vector3 center, float radius)
    {
        nearby.Clear();
        QueryFrame(center, radius, null);
        foreach (var frame in MovingFrame.Active)
            if (frame.gameObject.scene == gameObject.scene) QueryFrame(center, radius, frame);
    }
    void QueryFrame(Vector3 worldCenter, float radius, MovingFrame frame)
    {
        Vector3 center = frame ? frame.transform.InverseTransformPoint(worldCenter) : worldCenter;
        int frameId = frame ? frame.GetInstanceID() : 0;
        var extent = Vector3.one * radius;
        var lo = Vector3Int.FloorToInt((center - extent) / CellSize);
        var hi = Vector3Int.FloorToInt((center + extent) / CellSize);
        for (int z=lo.z; z<=hi.z; z++)
        for (int y=lo.y; y<=hi.y; y++)
        for (int x=lo.x; x<=hi.x; x++)
            if (cells.TryGetValue((frameId, new Vector3Int(x,y,z)), out var bucket))
                foreach (var fish in bucket)
                    if ((fish.transform.position-worldCenter).sqrMagnitude <= radius*radius) nearby.Add(fish);
    }
    void WakeAround(Vector3 center, float radius)
    {
        Query(center,radius);
        foreach (var fish in nearby) Wake(fish);
    }
    internal SeaAngelCreature Reserve(Vector3 center, float radius, FishingPoleCreature pole)
    {
        FinishPlacement();
        Query(center,radius);
        SeaAngelCreature best = null;
        float distance = float.PositiveInfinity;
        foreach (var fish in nearby)
        {
            if (!fish.Available) continue;
            float d = (fish.transform.position-center).sqrMagnitude;
            if (d < distance) { best = fish; distance = d; }
        }
        return best != null && best.TryReserve(pole) ? best : null;
    }
    public void RefreshRelevance()
    {
        using var sample = activationMarker.Auto();
        if (!Player)
            foreach (var p in FindObjectsByType<PlayerCreatureCarrier>(FindObjectsSortMode.None))
                if (p.gameObject.scene == gameObject.scene) { Player = p; break; }
        FinishPlacement();
        foreach (var fish in AwakeFish)
        {
            Reindex(fish);
        }
        if (Player)
        {
            var pos = Player.transform.position;
            float playerWakeRadius=0;
            foreach (var pole in Poles)
                if (!pole.IsDead && (pole.Carrier == Player || (!pole.IsCarried && Vector3.Distance(pos,pole.transform.position)<=pole.PlayerWakeRange)))
                    playerWakeRadius=Mathf.Max(playerWakeRadius,pole.FishWakeRange);
            if (Player.TryGetComponent<PlayerHoneyEffects>(out var honey) && honey.HasHoneySight)
                playerWakeRadius=Mathf.Max(playerWakeRadius,honey.FishSightRange);
            // Player-centered activation sources share one query. Visibility still uses
            // each source's own rules in the renderer.
            if(playerWakeRadius>0)WakeAround(pos,playerWakeRadius);
            foreach (var stone in Stones)
                if (!stone.IsDead && Vector3.Distance(pos,stone.transform.position)<=stone.playerRange)
                    WakeAround(stone.transform.position,stone.fishRange);
        }
        retire.Clear();
        foreach (var fish in AwakeFish)
            if (fish.ReservedBy == null && !CreatureSimulation.Allows(fish)) retire.Add(fish);
        foreach (var fish in retire) AwakeFish.Remove(fish);
    }
}
