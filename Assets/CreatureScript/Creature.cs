using UnityEngine;

/// <summary>
/// Information passed to a creature when the player or another object interacts with it.
/// This keeps individual creatures independent from specific keyboard or mouse buttons.
/// </summary>
public readonly struct CreatureInteraction
{
    /// <summary>The object that initiated the interaction.</summary>
    public GameObject Source { get; }

    /// <summary>The world-space point where the interaction occurred.</summary>
    public Vector3 Point { get; }

    /// <summary>The world-space direction of the interaction.</summary>
    public Vector3 Direction { get; }

    /// <summary>
    /// Creates a description of one interaction.
    /// </summary>
    public CreatureInteraction(GameObject source, Vector3 point, Vector3 direction)
    {
        Source = source;
        Point = point;
        Direction = direction;
    }
}

/// <summary>
/// Minimal base type shared by every interactive creature in the project.
///
/// This class owns only common lifecycle and interaction entry points.
/// Heat, blooming, movement, feeding, and other specific rules belong in subclasses
/// or separate components rather than being added to this base class.
/// </summary>
[DisallowMultipleComponent]
public abstract class Creature : MonoBehaviour
{
    [Header("Creature Identity")]
    [Tooltip("Editor-facing name used to identify this creature during development. It does not need to be shown to the player.")]
    [SerializeField] private string creatureName = "Unnamed Creature";

    [Header("Runtime")]
    [Tooltip("When disabled, this creature remains in the scene but does not run its creature logic.")]
    [SerializeField] private bool simulationEnabled = true;

    [Header("Shared Parameters")]
    [Tooltip("Heat currently stored by this creature. Kept as a float so small changes over time are not lost.")]
    [Min(0f)]
    [SerializeField] private float heat;

    /// <summary>Editor-facing name of this creature.</summary>
    public string CreatureName => creatureName;

    /// <summary>Whether this creature currently runs its simulation logic.</summary>
    public bool SimulationEnabled => simulationEnabled;

    /// <summary>Heat currently stored by this creature.</summary>
    public float Heat => heat;

    /// <summary>
    /// Unity initialization entry point.
    /// Calls the subclass hook once when the creature instance is created.
    /// </summary>
    protected virtual void Awake()
    {
        InitializeCreature();
    }

    /// <summary>
    /// Unity frame update entry point.
    /// Passes delta time to the subclass while simulation is enabled.
    /// </summary>
    protected virtual void Update()
    {
        if (!simulationEnabled)
        {
            return;
        }

        TickCreature(Time.deltaTime);
    }

    /// <summary>
    /// Enables or disables this creature's logic without disabling its GameObject.
    /// Useful later for pausing, freezing, or temporarily stabilizing a creature.
    /// </summary>
    public void SetSimulationEnabled(bool value)
    {
        simulationEnabled = value;
    }

    /// <summary>
    /// Gives Heat to this creature.
    /// Fire Sources and future creatures can use this common entry point.
    /// </summary>
    public virtual void AddHeat(float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        heat += amount;
        OnHeatChanged();
    }

    /// <summary>
    /// Replaces this creature's stored Heat with an exact value.
    /// Primarily used when one creature transforms into another and transfers its state.
    /// </summary>
    public virtual void SetHeat(float amount)
    {
        heat = Mathf.Max(0f, amount);
        OnHeatChanged();
    }

    /// <summary>
    /// Removes up to the requested amount of Heat and returns the amount actually removed.
    /// Returning the actual amount makes Heat transfer easy to conserve.
    /// </summary>
    protected float SpendHeat(float requestedAmount)
    {
        if (requestedAmount <= 0f || heat <= 0f)
        {
            return 0f;
        }

        float spentAmount = Mathf.Min(requestedAmount, heat);
        heat -= spentAmount;
        OnHeatChanged();

        return spentAmount;
    }

    /// <summary>
    /// Receives a deliberate interaction from the player or another system.
    /// Subclasses override this only when that creature reacts to direct interaction.
    /// </summary>
    public virtual void ReceiveInteraction(CreatureInteraction interaction)
    {
    }

    /// <summary>
    /// Receives notification that this creature has met another creature.
    /// Collision or trigger components can call this method later.
    /// </summary>
    public virtual void MeetCreature(Creature other)
    {
    }

    /// <summary>
    /// One-time initialization hook for subclasses.
    /// Use this instead of adding another Awake method whenever possible.
    /// </summary>
    protected virtual void InitializeCreature()
    {
    }

    /// <summary>
    /// Optional hook called whenever this creature gains or spends Heat.
    /// Subclasses can use it to update state without exposing the Heat field.
    /// </summary>
    protected virtual void OnHeatChanged()
    {
    }

    /// <summary>
    /// Per-frame behavior hook implemented by each concrete creature.
    /// </summary>
    protected abstract void TickCreature(float deltaTime);
}
