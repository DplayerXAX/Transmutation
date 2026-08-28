using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Creature that stores Heat and continuously radiates it to nearby creatures.
/// Every creature independently receives distance-scaled Heat; receivers do not divide it.
/// </summary>
[RequireComponent(typeof(SphereCollider), typeof(Rigidbody))]
public sealed class FireSource : Creature
{
    [Header("Radiation")]
    [Tooltip("Heat this source attempts to radiate and spend each second.")]
    [Min(0f)]
    [SerializeField] private float radiationPerSecond = 5f;

    [Tooltip("How much received Heat per second decreases for every metre of distance.")]
    [Min(0.001f)]
    [SerializeField] private float heatDropPerMetre = 1f;

    [Tooltip("Physics layers that can contain creatures receiving Heat.")]
    [SerializeField] private LayerMask heatReceiverLayers = ~0;

    [Header("Physics")]
    [Tooltip("Solid body collider used when this Fire Source falls onto the ground.")]
    [SerializeField] private SphereCollider bodyCollider;

    [Tooltip("Trigger sphere whose radius represents the current significant Heat range.")]
    [SerializeField] private SphereCollider radiationTrigger;

    [Tooltip("Rigidbody used to keep a normal source stationary or allow a transformed source to fall.")]
    [SerializeField] private Rigidbody sourceBody;

    // Creatures currently overlapping the radiation trigger.
    private readonly HashSet<Creature> creaturesInRange = new HashSet<Creature>();

    /// <summary>Radius at which the linear Heat falloff reaches zero.</summary>
    public float RadiationRadius => radiationPerSecond / Mathf.Max(0.001f, heatDropPerMetre);

    /// <summary>
    /// Configures the default Fire Source as stationary.
    /// A transformed Bubble explicitly calls BeginFalling afterward.
    /// </summary>
    protected override void InitializeCreature()
    {
        FindPhysicsReferences();

        bodyCollider.isTrigger = false;
        radiationTrigger.isTrigger = true;
        sourceBody.isKinematic = true;
        sourceBody.useGravity = false;
        UpdateRadiationTrigger();

    }

    /// <summary>Fire Source has no frame-based behavior outside its physics radiation step.</summary>
    protected override void TickCreature(float deltaTime)
    {
        UpdateRadiationTrigger();
    }

    /// <summary>
    /// Makes this Fire Source a normal falling physics object.
    /// Used when a successfully interacted Fire Bubble transforms into a source.
    /// </summary>
    public void BeginFalling(Vector3 initialVelocity)
    {
        FindPhysicsReferences();

        sourceBody.isKinematic = false;
        sourceBody.useGravity = true;
        sourceBody.linearVelocity = initialVelocity;

    }

    /// <summary>
    /// Gives Heat to creatures registered by the radiation Trigger, then spends Heat once.
    /// </summary>
    private void FixedUpdate()
    {
        if (!SimulationEnabled || radiationPerSecond <= 0f)
        {
            return;
        }

        if (Heat <= 0f)
        {
            return;
        }

        float deltaTime = Time.fixedDeltaTime;
        float actualRadiationRate = Mathf.Min(radiationPerSecond, Heat / deltaTime);
        creaturesInRange.RemoveWhere(creature => creature == null);

        foreach (Creature creature in creaturesInRange)
        {
            float distance = Vector3.Distance(transform.position, creature.transform.position);
            float receivedPerSecond = Mathf.Max(0f, actualRadiationRate - distance * heatDropPerMetre);

            creature.AddHeat(receivedPerSecond * deltaTime);
        }

        // Radiation is spent even when no creature receives it.
        SpendHeat(actualRadiationRate * deltaTime);
    }

    /// <summary>Registers a Creature entering the Heat trigger.</summary>
    private void OnTriggerEnter(Collider other)
    {
        Creature creature = other.GetComponentInParent<Creature>();

        if (creature != null && creature != this && IsReceiverLayerAllowed(other.gameObject.layer))
        {
            creaturesInRange.Add(creature);
        }
    }

    /// <summary>Removes a Creature leaving the Heat trigger.</summary>
    private void OnTriggerExit(Collider other)
    {
        Creature creature = other.GetComponentInParent<Creature>();

        if (creature != null && creaturesInRange.Remove(creature))
        {
        }
    }

    /// <summary>Checks the optional receiver Layer Mask.</summary>
    private bool IsReceiverLayerAllowed(int layer)
    {
        return (heatReceiverLayers.value & (1 << layer)) != 0;
    }

    /// <summary>Finds the required physics components if they were not assigned manually.</summary>
    private void FindPhysicsReferences()
    {
        SphereCollider[] sphereColliders = GetComponents<SphereCollider>();

        if (radiationTrigger == bodyCollider)
        {
            radiationTrigger = null;
        }

        if (bodyCollider == null || bodyCollider == radiationTrigger)
        {
            foreach (SphereCollider sphereCollider in sphereColliders)
            {
                if (!sphereCollider.isTrigger)
                {
                    bodyCollider = sphereCollider;
                    break;
                }
            }

            if (bodyCollider == null && sphereColliders.Length > 0)
            {
                bodyCollider = sphereColliders[0];
            }
        }

        if (radiationTrigger == null || radiationTrigger == bodyCollider)
        {
            foreach (SphereCollider sphereCollider in sphereColliders)
            {
                if (sphereCollider != bodyCollider && sphereCollider.isTrigger)
                {
                    radiationTrigger = sphereCollider;
                    break;
                }
            }
        }

        // Existing objects created before the two-collider setup receive their trigger at runtime.
        if (radiationTrigger == null && Application.isPlaying)
        {
            radiationTrigger = gameObject.AddComponent<SphereCollider>();
        }

        if (sourceBody == null)
        {
            sourceBody = GetComponent<Rigidbody>();
        }
    }

    /// <summary>Updates the Trigger radius while leaving the solid body Collider unchanged.</summary>
    private void UpdateRadiationTrigger()
    {
        if (radiationTrigger == null)
        {
            return;
        }

        radiationTrigger.isTrigger = true;
        radiationTrigger.radius = RadiationRadius;
        radiationTrigger.enabled = Heat > 0f && radiationPerSecond > 0f;
    }

    /// <summary>Shows the current radiation range when selected in the Scene view.</summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.05f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, RadiationRadius);
    }

    /// <summary>Keeps editable values valid and configures the source as stationary by default.</summary>
    private void OnValidate()
    {
        radiationPerSecond = Mathf.Max(0f, radiationPerSecond);
        heatDropPerMetre = Mathf.Max(0.001f, heatDropPerMetre);

        FindPhysicsReferences();

        if (bodyCollider != null)
        {
            bodyCollider.isTrigger = false;
        }

        if (radiationTrigger != null)
        {
            radiationTrigger.isTrigger = true;
            radiationTrigger.radius = RadiationRadius;
        }

        if (sourceBody != null && !Application.isPlaying)
        {
            sourceBody.isKinematic = true;
            sourceBody.useGravity = false;
        }
    }

    /// <summary>
    /// Creates the separate radiation Trigger when this component is first added in the Editor.
    /// </summary>
    private void Reset()
    {
        FindPhysicsReferences();

        if (radiationTrigger == null)
        {
            radiationTrigger = gameObject.AddComponent<SphereCollider>();
        }

        bodyCollider.isTrigger = false;
        radiationTrigger.isTrigger = true;
        radiationTrigger.radius = RadiationRadius;
    }
}
