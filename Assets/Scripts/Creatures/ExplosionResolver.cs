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
        public GameObject FiringPlayer;
    }
    private struct Hit
    {
        public Rigidbody Body;
        public ICombatTarget Target;
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
        Creature emitter, Creature projectile = null, GameObject firingPlayer = null)
    {
        if (!scene.IsValid() || radius <= 0f) return;
        queue.Enqueue(new Blast { Scene = scene, Center = center, Radius = radius, Damage = damage,
            Push = push, Emitter = emitter, Projectile = projectile, FiringPlayer = firingPlayer });
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
            ICombatTarget target = CombatTarget.Owner(collider);
            if (body == null && !CombatTarget.Exists(target)) continue;
            if (blast.Projectile != null && target == (ICombatTarget)blast.Projectile) continue;
            Vector3 point = collider.ClosestPoint(blast.Center);
            float distance = (point - blast.Center).magnitude;
            if (distance > blast.Radius) continue;
            if (CreatureSpatialQuery.FirstSolid(blast.Scene, blast.Center, point, blast.Projectile,
                null, ref rays, out var obstruction) && (body == null || obstruction.rigidbody != body) &&
                (!CombatTarget.Exists(target) || CombatTarget.Owner(obstruction.collider) != target)) continue;
            var hit = new Hit { Body = body, Target = target, Point = point, Distance = distance };
            // Receiver and rigidbody identity are independent: compound/multi-body targets take one hit.
            if (CombatTarget.Exists(target) && target != (ICombatTarget)blast.Emitter)
            {
                int targetId = target.TargetComponent.GetInstanceID();
                if (!creatureHits.TryGetValue(targetId, out var previousTarget) || distance < previousTarget.Distance)
                    creatureHits[targetId] = hit;
            }
            if (body == null) continue;
            int key = body.GetInstanceID();
            if (bodies.TryGetValue(key, out var previous) && previous.Distance <= distance) continue;
            bodies[key] = hit;
        }
        // The dictionary is not mutated by nested deaths: their blasts only enqueue.
        foreach (Hit hit in creatureHits.Values)
            if (CombatTarget.Exists(hit.Target))
            {
                float damage = blast.Damage * Mathf.Clamp01(1f - hit.Distance / blast.Radius);
                if (blast.FiringPlayer != null && hit.Target.TargetComponent.transform.IsChildOf(blast.FiringPlayer.transform))
                    damage = Mathf.Min(1f, damage);
                hit.Target.ApplyDamage(damage, blast.Emitter);
            }
        foreach (Hit hit in bodies.Values)
        {
            float fraction = Mathf.Clamp01(1f - hit.Distance / blast.Radius);
            if (hit.Body == null || hit.Body.isKinematic) continue;
            Vector3 direction = hit.Body.worldCenterOfMass - blast.Center;
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.up;
            Vector3 velocity = direction.normalized * (blast.Push * fraction);
            if (blast.FiringPlayer != null && hit.Body.transform.IsChildOf(blast.FiringPlayer.transform))
            {
                float height = Mathf.Max(1f, hit.Body.GetComponentInChildren<Collider>()?.bounds.size.y ?? 2f);
                float shooterFalloff = Mathf.Clamp01((blast.Radius - hit.Distance) /
                    Mathf.Max(0.01f, blast.Radius - height));
                float lift = Mathf.Sqrt(2f * Physics.gravity.magnitude * 3.5f * height);
                velocity.y = Mathf.Max(velocity.y, lift * shooterFalloff);
            }
            CreatureImpulse.Apply(hit.Body, velocity, 0.22f);
        }
        ResolvedCount++;
        if (Exploded != null)
            foreach (Action<Vector3, float, Scene> listener in Exploded.GetInvocationList())
                try { listener(blast.Center, blast.Radius, blast.Scene); }
                catch (Exception exception) { Debug.LogException(exception); }
    }
}
