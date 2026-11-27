using UnityEngine;

/// <summary>
/// Per-body wind state, added by a WindArea the first time it finds a Rigidbody, and switched off again once
/// the body is out of every area and its drift has died down (no idle cost).
///
/// Who applies the wind depends on who owns the movement:
/// - a creature motor that wrote its velocity recently (Creature.SetCreatureVelocity and friends) adds
///   <see cref="Offset"/> itself, so a motor that overwrites velocity each frame cannot erase the wind;
/// - anything passive (props, sleeping creatures, corpses) is pulled directly here;
/// - carry, capture, web, cradles, reflection freeze and other external motion get no wind at all;
/// - thrown flight is bent by CreatureThrower, not here.
/// </summary>
[DisallowMultipleComponent]
public sealed class WindBody : MonoBehaviour
{
    /// <summary>A motor that wrote velocity within this many seconds owns the movement.</summary>
    private const float MotorWindow = 0.15f;

    private Rigidbody body;
    private Creature creature;
    private IWindResistant resistant;
    private Vector3 offset;
    private float grip;
    private bool motorLastStep;

    /// <summary>Wind velocity a creature motor adds on top of its own velocity.</summary>
    public Vector3 Offset => isActiveAndEnabled ? offset : Vector3.zero;

    public float Resistance => resistant != null ? Mathf.Clamp01(resistant.WindResistance) : 0f;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        creature = GetComponent<Creature>();
        resistant = GetComponentInParent<IWindResistant>();
    }

    private void OnDisable()
    {
        offset = Vector3.zero;
        motorLastStep = false;
    }

    private void FixedUpdate()
    {
        if (body == null) { enabled = false; return; }
        float dt = Time.fixedDeltaTime;
        Vector3 position = body.worldCenterOfMass;
        float resistance = Resistance;

        if (Excluded() || resistance >= 1f)
        {
            offset = Vector3.zero;
            motorLastStep = false;
            if (!WindField.Inside(position)) enabled = false;
            return;
        }

        bool horizontalOnly = false;
        bool motor = creature != null && creature.WroteLocomotionWithin(MotorWindow, out horizontalOnly);
        if (motor)
        {
            bool inside;
            // Coming from passive drift, the body already moves with the wind: start the offset there.
            if (!motorLastStep) offset = WindField.Target(position, resistance, out grip, out inside);
            offset = WindField.StepOffset(offset, position, resistance, ref grip, dt, out inside);
            // A horizontal-only motor leaves vertical speed to physics, so lift and downdraft act passively.
            if (horizontalOnly && !body.isKinematic)
            {
                Vector3 velocity = body.linearVelocity;
                Vector3 pulled = WindField.PullPassive(velocity, position, resistance, dt);
                if (pulled.y != velocity.y) body.linearVelocity = new Vector3(velocity.x, pulled.y, velocity.z);
            }
            motorLastStep = true;
            if (!inside && offset == Vector3.zero) enabled = false;
            return;
        }

        motorLastStep = false;
        offset = Vector3.zero;
        if (!WindField.Inside(position)) { enabled = false; return; }
        if (body.isKinematic) return;
        Vector3 before = body.linearVelocity;
        Vector3 after = WindField.PullPassive(before, position, resistance, dt);
        if (after != before)
        {
            body.linearVelocity = after;
            body.WakeUp();
        }
    }

    private bool Excluded()
    {
        if (TryGetComponent<BubbleCapture>(out var bubble) && bubble.IsCaptured) return true;
        if (creature == null) return false;
        return !creature.isActiveAndEnabled || !creature.IsInPhysicalWorld ||
            MovementFrameMember.IsExternallyMoved(creature) ||
            BoneChild.IsCradled(creature) || ReflectionSpotlight.IsFrozen(creature) ||
            // Render-driven motion authors its pose outside physics; immune until a species opts in.
            creature is SeaAngelCreature angel && !angel.Materialized ||
            creature is TentacleCreature tentacle && !tentacle.IsDead && body.isKinematic;
    }
}
