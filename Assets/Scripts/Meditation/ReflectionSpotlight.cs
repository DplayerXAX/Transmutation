using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns the initial members of one exhibit and controls its visible light volume.</summary>
public sealed class ReflectionSpotlight : MonoBehaviour
{
    private sealed class FrozenState
    {
        public bool Simulated;
        public (Rigidbody body, bool kinematic, CollisionDetectionMode detection, Vector3 velocity, Vector3 angular)[] Bodies;
        public (Animator animator, float speed)[] Animators;
    }

    private static readonly Dictionary<Creature, ReflectionSpotlight> Frozen = new();
    private static readonly Dictionary<Creature, ReflectionSpotlight> Exhibits = new();
    private readonly Dictionary<Creature, FrozenState> states = new();
    private readonly List<Creature> members = new();
    private GameObject illumination;
    private float radius, height;
    public bool IsLit => illumination != null && illumination.activeSelf;
    public static ReflectionSpotlight ForFrozen(Creature creature) =>
        Frozen.TryGetValue(creature, out var beam) ? beam : null;
    public static bool IsFrozen(Creature creature) => Frozen.ContainsKey(creature);

    public void Configure(float beamRadius, float beamHeight, Material beam, Material fixture)
    {
        radius = beamRadius;
        height = beamHeight;
        illumination = new GameObject("Visible spotlight");
        illumination.transform.SetParent(transform, false);
        var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shaft.name = "Light shaft";
        shaft.transform.SetParent(illumination.transform, false);
        shaft.transform.localPosition = Vector3.up * (height * 0.5f);
        shaft.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        shaft.GetComponent<Collider>().enabled = false;
        shaft.GetComponent<Renderer>().sharedMaterial = beam;
        var pool = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pool.name = "Pool of light";
        pool.transform.SetParent(illumination.transform, false);
        pool.transform.localPosition = Vector3.up * 0.012f;
        pool.transform.localScale = new Vector3(radius * 2f, 0.006f, radius * 2f);
        pool.GetComponent<Collider>().enabled = false;
        pool.GetComponent<Renderer>().sharedMaterial = beam;
        var spot = new GameObject("Spotlight").AddComponent<Light>();
        spot.transform.SetParent(illumination.transform, false);
        spot.transform.localPosition = Vector3.up * height;
        spot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        spot.type = LightType.Spot;
        spot.spotAngle = 2f * Mathf.Atan2(radius, height) * Mathf.Rad2Deg;
        spot.range = height + 1f;
        spot.color = new Color(1f, 0.91f, 0.72f);
        spot.intensity = 3f;
        var control = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        control.name = "Light switch";
        control.transform.SetParent(transform, false);
        control.transform.localPosition = Vector3.back * radius + Vector3.up * 0.35f;
        control.transform.localScale = new Vector3(0.32f, 0.35f, 0.32f);
        control.GetComponent<Renderer>().sharedMaterial = fixture;
    }

    public void Register(Creature creature)
    {
        if (creature == null || members.Contains(creature)) return;
        members.Add(creature);
        Exhibits[creature] = this;
        Refresh(creature);
    }

    internal static void Forget(Creature creature)
    {
        if (creature == null || !Exhibits.Remove(creature, out var beam)) return;
        if (beam.states.TryGetValue(creature, out var state)) beam.Thaw(creature, state);
        beam.members.Remove(creature);
    }

    public void Toggle()
    {
        illumination.SetActive(!IsLit);
        foreach (Creature creature in members)
            if (creature != null) Refresh(creature);
    }

    private void FixedUpdate()
    {
        foreach (Creature creature in members)
            if (creature != null) Refresh(creature);
    }

    private void Refresh(Creature creature)
    {
        Vector3 offset = creature.transform.position - transform.position;
        bool shouldFreeze = IsLit && !creature.IsDead && !creature.IsCarried &&
            offset.y >= -0.4f && offset.y <= height &&
            offset.x * offset.x + offset.z * offset.z <= radius * radius;
        if (shouldFreeze && !states.ContainsKey(creature)) Freeze(creature);
        else if (!shouldFreeze && states.TryGetValue(creature, out var state)) Thaw(creature, state);
    }

    private void Freeze(Creature creature)
    {
        var bodies = creature.GetComponentsInChildren<Rigidbody>();
        var animators = creature.GetComponentsInChildren<Animator>();
        var state = new FrozenState
        {
            Simulated = creature.SimulationEnabled,
            Bodies = new (Rigidbody, bool, CollisionDetectionMode, Vector3, Vector3)[bodies.Length],
            Animators = new (Animator, float)[animators.Length]
        };
        for (int i = 0; i < bodies.Length; i++)
            state.Bodies[i] = (bodies[i], bodies[i].isKinematic, bodies[i].collisionDetectionMode, bodies[i].linearVelocity, bodies[i].angularVelocity);
        for (int i = 0; i < animators.Length; i++)
        {
            state.Animators[i] = (animators[i], animators[i].speed);
            animators[i].speed = 0f;
        }
        Frozen[creature] = this;
        states.Add(creature, state);
        creature.SetSimulationEnabled(false);
        foreach (var part in state.Bodies)
        {
            part.body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            part.body.isKinematic = true;
        }
    }

    private void Thaw(Creature creature, FrozenState state)
    {
        Frozen.Remove(creature);
        states.Remove(creature);
        foreach (var part in state.Bodies)
        {
            if (part.body == null) continue;
            part.body.isKinematic = part.kinematic;
            part.body.collisionDetectionMode = part.detection;
            if (part.kinematic) continue;
            part.body.linearVelocity = part.velocity;
            part.body.angularVelocity = part.angular;
        }
        foreach (var part in state.Animators)
            if (part.animator != null) part.animator.speed = part.speed;
        creature.SetSimulationEnabled(state.Simulated);
    }

    private void OnDestroy()
    {
        foreach (Creature creature in members) Exhibits.Remove(creature);
        foreach (Creature creature in states.Keys) Frozen.Remove(creature);
    }
}
