using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Synchronous queue prevents chained projectile deaths from recursing into physics queries.</summary>
public static class ExplosionResolver
{
    private struct Blast
    {
        public Scene Scene;
        public Vector3 Center;
        public float Radius, Damage, Push;
        public Creature Emitter, Projectile;
    }
    private struct Hit
    {
        public Rigidbody Body;
        public Creature Creature;
        public Vector3 Point;
        public float Distance;
    }
    private static readonly Queue<Blast> queue = new Queue<Blast>();
    private static readonly Dictionary<int, Hit> bodies = new Dictionary<int, Hit>(64);
    private static readonly Dictionary<int, Hit> creatureHits = new Dictionary<int, Hit>(64);
    private static Collider[] overlaps = new Collider[64];
    private static RaycastHit[] rays = new RaycastHit[32];
    private static bool processing;
    public static int ResolvedCount { get; private set; }
    public static event Action<Vector3, float, Scene> Exploded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    { queue.Clear(); bodies.Clear(); creatureHits.Clear(); processing = false; ResolvedCount = 0; Exploded = null; }

    public static void Request(Scene scene, Vector3 center, float radius, float damage, float push,
        Creature emitter, Creature projectile = null)
    {
        if (!scene.IsValid() || radius <= 0f) return;
        queue.Enqueue(new Blast { Scene = scene, Center = center, Radius = radius, Damage = damage,
            Push = push, Emitter = emitter, Projectile = projectile });
        if (processing) return;
        processing = true;
        try { while (queue.Count > 0) Resolve(queue.Dequeue()); }
        finally { processing = false; queue.Clear(); bodies.Clear(); creatureHits.Clear(); }
    }

    private static void Resolve(Blast blast)
    {
        bodies.Clear(); creatureHits.Clear();
        int count;
        var physics = blast.Scene.GetPhysicsScene();
        while ((count = physics.OverlapSphere(blast.Center, blast.Radius, overlaps, ~0,
                   QueryTriggerInteraction.Ignore)) == overlaps.Length) Array.Resize(ref overlaps, overlaps.Length * 2);
        for (int i = 0; i < count; i++)
        {
            Collider collider = overlaps[i];
            Rigidbody body = collider.attachedRigidbody;
            Creature creature = CreatureSpatialQuery.Owner(collider);
            if (body == null && creature == null) continue;
            if (blast.Projectile != null && creature == blast.Projectile) continue;
            Vector3 point = collider.ClosestPoint(blast.Center);
            float distance = (point - blast.Center).magnitude;
            if (distance > blast.Radius) continue;
            if (CreatureSpatialQuery.FirstSolid(blast.Scene, blast.Center, point, blast.Projectile,
                null, ref rays, out var obstruction) && (body == null || obstruction.rigidbody != body) &&
                (creature == null || CreatureSpatialQuery.Owner(obstruction.collider) != creature)) continue;
            int key = body != null ? body.GetInstanceID() : creature.GetInstanceID();
            if (bodies.TryGetValue(key, out var previous) && previous.Distance <= distance) continue;
            bodies[key] = new Hit { Body = body, Creature = creature, Point = point, Distance = distance };
        }
        // The dictionary is not mutated by nested deaths: their blasts only enqueue.
        foreach (Hit hit in bodies.Values)
        {
            if (hit.Creature == null || hit.Creature == blast.Emitter) continue;
            int id = hit.Creature.GetInstanceID();
            if (!creatureHits.TryGetValue(id, out var previous) || hit.Distance < previous.Distance) creatureHits[id] = hit;
        }
        foreach (Hit hit in creatureHits.Values)
            if (hit.Creature != null) hit.Creature.ApplyDamage(blast.Damage * Mathf.Clamp01(1f - hit.Distance / blast.Radius), blast.Emitter);
        foreach (Hit hit in bodies.Values)
        {
            float fraction = Mathf.Clamp01(1f - hit.Distance / blast.Radius);
            if (hit.Body == null || hit.Body.isKinematic) continue;
            Vector3 direction = hit.Body.worldCenterOfMass - blast.Center;
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.up;
            Vector3 velocity = direction.normalized * (blast.Push * fraction);
            var player = hit.Body.GetComponent<SmoothFirstPersonController>();
            if (player != null) player.Fling(hit.Body.linearVelocity + velocity);
            else hit.Body.AddForce(velocity, ForceMode.VelocityChange);
            if (hit.Body.TryGetComponent<AsuraMotor>(out var motor)) motor.AcceptPush();
        }
        ResolvedCount++;
        if (Exploded != null)
            foreach (Action<Vector3, float, Scene> listener in Exploded.GetInvocationList())
                try { listener(blast.Center, blast.Radius, blast.Scene); }
                catch (Exception exception) { Debug.LogException(exception); }
    }
}
