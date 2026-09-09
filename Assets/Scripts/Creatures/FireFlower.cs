using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fire Flower creature.
/// Accumulates Heat, spreads linked DensityShapeOverlays outward, blooms,
/// releases Bubbles from those shape positions, then recovers the shapes inward.
/// </summary>
public sealed class FireFlower : Creature
{
    public enum FireFlowerState
    {
        Closed,
        Heating,
        Blooming,
        Releasing,
        Recovering
    }

    [System.Serializable]
    private sealed class ShapeSlot
    {
        public DensityShapeOverlay shape;

        [HideInInspector] public Vector3 startPosition;
        [HideInInspector] public Vector3 spreadDirection;
        [HideInInspector] public bool initialized;
    }

    [Header("Heat")]
    [Tooltip("Heat required before the flower can begin blooming.")]
    [Min(0.01f)]
    [SerializeField] private float bloomHeatThreshold = 70f;

    [Header("Bloom")]
    [Tooltip("Approximate time used by the bloom transition.")]
    [SerializeField] private float bloomDuration = 1f;

    [Tooltip("Approximate recovery time before the flower can bloom again.")]
    [SerializeField] private float recoveryDuration = 1f;

    [Header("Density Shapes")]
    [Tooltip("Density shapes driven by this flower. Bubbles spawn from these positions on release.")]
    [SerializeField] private List<ShapeSlot> densityShapes = new List<ShapeSlot>();

    [Tooltip("Inclusive world-Y range used when randomizing each shape's start height.")]
    [SerializeField] private Vector2 startHeightRange = new Vector2(0f, 2f);

    [Tooltip("How far each shape travels from its start position at full Heat / bloom.")]
    [Min(0f)]
    [SerializeField] private float maxSpreadDistance = 3f;

    [Header("Body Cylinder")]
    [Tooltip("This flower's own cylinder DensityShape. Radius grows with Heat and shrinks while recovering.")]
    [SerializeField] private DensityShapeOverlay bodyCylinder;

    [Tooltip("Cylinder radius while Closed / at the start of Heating.")]
    [Min(0.01f)]
    [SerializeField] private float bodyRadiusMin = 0.25f;

    [Tooltip("Cylinder radius at full Heat / bloom.")]
    [Min(0.01f)]
    [SerializeField] private float bodyRadiusMax = 1.5f;

    [Header("Bubble Output")]
    [Tooltip("Bubble prefab released from each density shape when blooming finishes.")]
    [SerializeField] private FireBubble bubblePrefab;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private FireFlowerState currentState = FireFlowerState.Closed;
    [SerializeField] private float stateTime;

    [Range(0f, 1f)]
    [SerializeField] private float bloomAmount;

    [Range(0f, 1f)]
    [SerializeField] private float shapeSpreadAmount;

    /// <summary>Current high-level state, exposed read-only to other systems.</summary>
    public FireFlowerState CurrentState => currentState;

    /// <summary>Current normalized bloom amount for presentation systems.</summary>
    public float BloomAmount => bloomAmount;

    /// <summary>How far density shapes have spread from their start positions (0–1).</summary>
    public float ShapeSpreadAmount => shapeSpreadAmount;

    protected override void InitializeCreature()
    {
        InitializeShapeSlots();
        ChangeState(FireFlowerState.Closed);
        ApplyShapeSpread(0f);
        ApplyBodyRadius(0f);
    }

    protected override void TickCreature(float deltaTime)
    {
        SenseHeat(deltaTime);
        UpdateState(deltaTime);
        UpdateShapeMotion();
        UpdatePresentation(deltaTime);
    }

    private void SenseHeat(float deltaTime)
    {
        // Heat arrives through Creature.AddHeat from sources such as FireSource.
    }

    private void UpdateState(float deltaTime)
    {
        stateTime += deltaTime;

        switch (currentState)
        {
            case FireFlowerState.Closed:
                TickClosed();
                break;

            case FireFlowerState.Heating:
                TickHeating();
                break;

            case FireFlowerState.Blooming:
                TickBlooming(deltaTime);
                break;

            case FireFlowerState.Releasing:
                TickReleasing();
                break;

            case FireFlowerState.Recovering:
                TickRecovering(deltaTime);
                break;
        }
    }

    private void TickClosed()
    {
        if (Heat > 0f)
        {
            RerollSpreadDirections();
            ChangeState(FireFlowerState.Heating);
        }
    }

    private void TickHeating()
    {
        if (Heat >= bloomHeatThreshold)
        {
            Bloom();
        }
        else if (Heat <= 0f)
        {
            ChangeState(FireFlowerState.Closed);
        }
    }

    private void TickBlooming(float deltaTime)
    {
        float safeDuration = Mathf.Max(0.01f, bloomDuration);
        bloomAmount = Mathf.Clamp01(stateTime / safeDuration);

        if (stateTime >= safeDuration)
        {
            ChangeState(FireFlowerState.Releasing);
        }
    }

    private void TickReleasing()
    {
        ReleaseBubbles();
        ChangeState(FireFlowerState.Recovering);
    }

    private void TickRecovering(float deltaTime)
    {
        float safeDuration = Mathf.Max(0.01f, recoveryDuration);
        bloomAmount = 1f - Mathf.Clamp01(stateTime / safeDuration);

        if (stateTime >= safeDuration)
        {
            ChangeState(FireFlowerState.Closed);
        }
    }

    private void ReleaseBubbles()
    {
        int shapeCount = CountValidShapes();
        if (bubblePrefab == null || shapeCount <= 0)
        {
            Debug.LogWarning($"{name} cannot release bubbles because no Bubble prefab or density shapes are assigned.", this);
            return;
        }

        float transferredHeat = SpendHeat(bloomHeatThreshold);
        if (transferredHeat <= 0f)
        {
            return;
        }

        float heatPerBubble = transferredHeat / shapeCount;

        for (int index = 0; index < densityShapes.Count; index++)
        {
            ShapeSlot slot = densityShapes[index];
            if (slot == null || slot.shape == null)
            {
                continue;
            }

            FireBubble bubble = Instantiate(bubblePrefab, slot.shape.transform.position, Quaternion.identity);
            bubble.AddHeat(heatPerBubble);
        }
    }

    private void Bloom()
    {
        ChangeState(FireFlowerState.Blooming);
    }

    private void ChangeState(FireFlowerState nextState)
    {
        currentState = nextState;
        stateTime = 0f;
    }

    private void UpdatePresentation(float deltaTime)
    {
        // TODO: Send bloomAmount / shapeSpreadAmount to visuals.
    }

    /// <summary>
    /// Captures each shape's start pose: keeps editor XZ, randomizes Y within the configured range.
    /// </summary>
    private void InitializeShapeSlots()
    {
        float minY = Mathf.Min(startHeightRange.x, startHeightRange.y);
        float maxY = Mathf.Max(startHeightRange.x, startHeightRange.y);

        for (int index = 0; index < densityShapes.Count; index++)
        {
            ShapeSlot slot = densityShapes[index];
            if (slot == null || slot.shape == null)
            {
                continue;
            }

            Vector3 position = slot.shape.transform.position;
            float randomY = Random.Range(minY, maxY);
            slot.startPosition = new Vector3(position.x, randomY, position.z);
            slot.initialized = true;
            slot.shape.transform.position = slot.startPosition;
        }

        RerollSpreadDirections();
    }

    /// <summary>Picks a fresh unit direction in 3D for every shape (called at the start of each heating cycle).</summary>
    private void RerollSpreadDirections()
    {
        for (int index = 0; index < densityShapes.Count; index++)
        {
            ShapeSlot slot = densityShapes[index];
            if (slot == null || slot.shape == null)
            {
                continue;
            }

            Vector3 direction = Random.onUnitSphere;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.up;
            }

            slot.spreadDirection = direction.normalized;
        }
    }

    /// <summary>
    /// Heating: spread follows Heat / threshold.
    /// Blooming / Releasing: hold at full spread.
    /// Recovering: ease back to the start positions.
    /// </summary>
    private void UpdateShapeMotion()
    {
        float spread = 0f;

        switch (currentState)
        {
            case FireFlowerState.Closed:
                spread = 0f;
                break;

            case FireFlowerState.Heating:
                spread = Mathf.Clamp01(Heat / Mathf.Max(0.01f, bloomHeatThreshold));
                break;

            case FireFlowerState.Blooming:
            case FireFlowerState.Releasing:
                spread = 1f;
                break;

            case FireFlowerState.Recovering:
            {
                float safeDuration = Mathf.Max(0.01f, recoveryDuration);
                spread = 1f - Mathf.Clamp01(stateTime / safeDuration);
                break;
            }
        }

        ApplyShapeSpread(spread);
        ApplyBodyRadius(spread);
    }

    private void ApplyShapeSpread(float spread)
    {
        shapeSpreadAmount = Mathf.Clamp01(spread);
        float distance = shapeSpreadAmount * maxSpreadDistance;

        for (int index = 0; index < densityShapes.Count; index++)
        {
            ShapeSlot slot = densityShapes[index];
            if (slot == null || slot.shape == null || !slot.initialized)
            {
                continue;
            }

            slot.shape.transform.position = slot.startPosition + slot.spreadDirection * distance;
        }
    }

    private void ApplyBodyRadius(float amount)
    {
        if (bodyCylinder == null)
        {
            return;
        }

        float minRadius = Mathf.Min(bodyRadiusMin, bodyRadiusMax);
        float maxRadius = Mathf.Max(bodyRadiusMin, bodyRadiusMax);
        bodyCylinder.SetRadius(Mathf.Lerp(minRadius, maxRadius, Mathf.Clamp01(amount)));
    }

    private int CountValidShapes()
    {
        int count = 0;
        for (int index = 0; index < densityShapes.Count; index++)
        {
            ShapeSlot slot = densityShapes[index];
            if (slot != null && slot.shape != null)
            {
                count++;
            }
        }

        return count;
    }

    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        base.ReceiveInteraction(interaction);
    }

    public override void MeetCreature(Creature other)
    {
        base.MeetCreature(other);
    }

    private void OnValidate()
    {
        bloomHeatThreshold = Mathf.Max(0.01f, bloomHeatThreshold);
        bloomDuration = Mathf.Max(0f, bloomDuration);
        recoveryDuration = Mathf.Max(0f, recoveryDuration);
        maxSpreadDistance = Mathf.Max(0f, maxSpreadDistance);
        bodyRadiusMin = Mathf.Max(0.01f, bodyRadiusMin);
        bodyRadiusMax = Mathf.Max(0.01f, bodyRadiusMax);

        if (startHeightRange.x > startHeightRange.y)
        {
            float swap = startHeightRange.x;
            startHeightRange.x = startHeightRange.y;
            startHeightRange.y = swap;
        }
    }
}
