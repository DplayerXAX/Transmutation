using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Shared discovery snapshots only. Individuals retain all combat decisions.</summary>
public static class CreatureSpatialQuery
{
    private sealed class Snapshot
    {
        public readonly List<ICombatTarget> Targets = new List<ICombatTarget>(256);
        public readonly List<Vector3> Points = new List<Vector3>(256);
        public readonly BirdOctree Tree = new BirdOctree();
        public Vector3[] Positions = new Vector3[256];
        public float NextRefresh = float.NegativeInfinity;
        public int Revision = -1;
    }
    private static readonly Dictionary<int, Snapshot> scenes = new Dictionary<int, Snapshot>();
    private static readonly List<Collider> solids = new List<Collider>(16);
    public static int SnapshotCount { get; private set; }
    public static int LastCandidateChecks { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        scenes.Clear(); SnapshotCount = 0;
        SceneManager.sceneUnloaded -= RemoveScene;
        SceneManager.sceneUnloaded += RemoveScene;
    }
    private static void RemoveScene(Scene scene) => scenes.Remove(scene.handle);

    public static void Nearby(Scene scene, Vector3 center, float radius, List<Creature> results, List<int> indices)
    {
        Snapshot snapshot = Query(scene, center, radius, indices);
        results.Clear();
        for (int i = 0; i < indices.Count; i++)
            if (snapshot.Targets[indices[i]] is Creature creature && creature != null) results.Add(creature);
    }

    public static void NearbyCombat(Scene scene, Vector3 center, float radius, List<ICombatTarget> results, List<int> indices)
    {
        Snapshot snapshot = Query(scene, center, radius, indices);
        results.Clear();
        for (int i = 0; i < indices.Count; i++)
        {
            ICombatTarget target = snapshot.Targets[indices[i]];
            if (CombatTarget.Exists(target)) results.Add(target);
        }
    }

    private static Snapshot Query(Scene scene, Vector3 center, float radius, List<int> indices)
    {
        if (!scenes.TryGetValue(scene.handle, out Snapshot snapshot))
        {
            snapshot = new Snapshot(); scenes.Add(scene.handle, snapshot);
        }
        if (Time.time >= snapshot.NextRefresh || snapshot.Revision != CreatureRegistry.CombatRevision)
        {
            CreatureRegistry.SnapshotCombat(scene, snapshot.Targets, snapshot.Points);
            if (snapshot.Positions.Length < snapshot.Points.Count)
                Array.Resize(ref snapshot.Positions, Mathf.NextPowerOfTwo(snapshot.Points.Count));
            snapshot.Points.CopyTo(snapshot.Positions);
            snapshot.Tree.Rebuild(snapshot.Positions, snapshot.Points.Count);
            snapshot.NextRefresh = Time.time + 0.1f;
            snapshot.Revision = CreatureRegistry.CombatRevision;
            SnapshotCount++;
        }
        snapshot.Tree.Query(center, radius, indices);
        LastCandidateChecks = snapshot.Tree.LastCandidateChecks;
        return snapshot;
    }

    public static bool TryGetSolid(ICombatTarget target, out Collider solid)
    {
        solid = null;
        if (!CombatTarget.Exists(target) || !target.CombatRoot.TryGetComponent<Rigidbody>(out var body) || !body.detectCollisions) return false;
        if (target.CombatRoot.TryGetComponent<Collider>(out var root) && root.enabled && !root.isTrigger)
        { solid = root; return true; }
        solids.Clear();
        target.CombatRoot.GetComponentsInChildren(false, solids);
        for (int i = 0; i < solids.Count; i++)
            if (solids[i].enabled && !solids[i].isTrigger && solids[i].attachedRigidbody == body)
            { solid = solids[i]; return true; }
        return false;
    }

    public static Creature Owner(Collider collider) => collider == null ? null : collider.GetComponentInParent<Creature>();

    public static bool FirstSolid(Scene scene, Vector3 from, Vector3 to, Creature ignore,
        GameObject extraIgnore, ref RaycastHit[] hits, out RaycastHit nearest)
    {
        nearest = default;
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.0001f) return false;
        PhysicsScene physics = scene.GetPhysicsScene();
        int count;
        while ((count = physics.Raycast(from, delta / distance, hits, distance, ~0, QueryTriggerInteraction.Ignore)) == hits.Length)
            Array.Resize(ref hits, hits.Length * 2);
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i].collider;
            if ((ignore != null && Owner(collider) == ignore) ||
                (extraIgnore != null && collider.transform.IsChildOf(extraIgnore.transform))) continue;
            if (hits[i].distance >= best) continue;
            best = hits[i].distance; nearest = hits[i];
        }
        return !float.IsPositiveInfinity(best);
    }

    public static bool Visible(Creature observer, ICombatTarget target, Vector3 from, ref RaycastHit[] hits,
        GameObject extraIgnore = null) => observer != null && CombatTarget.Exists(target) && !FlashBlindness.IsBlinded(observer) &&
            (!FirstSolid(observer.gameObject.scene, from, target.AimPosition,
            observer, extraIgnore, ref hits, out var hit) || CombatTarget.Owner(hit.collider) == target);
}
