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

        [System.NonSerialized] public Vector3 startLocalPosition;
        [System.NonSerialized] public Vector3 spreadDirection;
        [System.NonSerialized] public bool initialized;
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

    [Tooltip("How far each shape travels from its start position at full Heat / bloom.")]
    [Min(0f)]
    [SerializeField] private float maxSpreadDistance = 3f;

    [Tooltip("Seconds to lerp shapes from 0 out to the current target spread. Shorter = snappier.")]
    [Min(0.01f)]
    [SerializeField] private float spreadOutDuration = 0.35f;

    [Header("Petal Crown")]
    [Tooltip("On: the density shapes are petals in an even crown round the bud, each one turned to point along " +
             "its own direction, opening from upright to leaning out as Heat rises. Off: they drift out in random directions.")]
    [SerializeField] private bool petalCrown;
    [Tooltip("Petal lean from straight up while closed (degrees).")]
    [Range(0f, 90f)] [SerializeField] private float crownClosedAngle = 12f;
    [Tooltip("Petal lean from straight up when fully open (degrees).")]
    [Range(0f, 120f)] [SerializeField] private float crownOpenAngle = 62f;
    [Tooltip("How far each petal's centre sits out from the crown pivot while closed, in local units (about half a petal).")]
    [Min(0f)] [SerializeField] private float petalReach = 1.3f;
    [Tooltip("Random turn of the whole crown at the start of each heating cycle (degrees), so blooms are not identical.")]
    [Range(0f, 180f)] [SerializeField] private float crownJitter = 25f;
    private float crownYaw;

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
    /// <summary>The fire bubble this flower blooms (a carried fire source shoots the same kind).</summary>
    public FireBubble BubblePrefab => bubblePrefab;

    [Header("Visuals")]
    [Tooltip("Renderer using the Fire Flower shader. Automatically found on this flower or its children when left empty.")]
    [SerializeField] private Renderer flowerRenderer;

    private static readonly int GrowthId = Shader.PropertyToID("_Growth");
    private static readonly int HeatId = Shader.PropertyToID("_Heat");
    private MaterialPropertyBlock visualProperties;
    // What is actually drawn: the marching-cubes chunks built under this flower at runtime, all using the flower material.
    private readonly List<Renderer> heatRenderers = new List<Renderer>();
    private float nextRendererScan;

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
        InitializeVisuals();
        UpdatePresentation(0f);
    }

    protected override void TickCreature(float deltaTime)
    {
        SenseHeat(deltaTime);
        UpdateState(deltaTime);
        UpdateShapeMotion(deltaTime);
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
            shapeSpreadAmount = 0f;
            ApplyShapeSpread(0f);
            ApplyBodyRadius(0f);
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
            SetHeat(0f);
            ChangeState(FireFlowerState.Closed);
        }
    }

    private void ReleaseBubbles()
    {
        int shapeCount = CountValidShapes();
        if (bubblePrefab == null)
        {
            Debug.LogWarning($"{name} cannot release bubbles because no Bubble prefab is assigned.", this);
            return;
        }

        if (shapeCount <= 0)
        {
            Debug.LogWarning($"{name} cannot release bubbles because no density shapes are assigned.", this);
            return;
        }

        EnsureShapeSlotsInitialized();

        // Always spawn bubbles from shape positions. Heat transfer is optional leftover energy.
        float transferredHeat = SpendHeat(Heat);
        float heatPerBubble = transferredHeat > 0f ? transferredHeat / shapeCount : 0f;

        for (int index = 0; index < densityShapes.Count; index++)
        {
            ShapeSlot slot = densityShapes[index];
            if (slot == null || slot.shape == null)
            {
                continue;
            }

            FireBubble bubble = Instantiate(bubblePrefab, slot.shape.transform.position, Quaternion.identity);
            if (heatPerBubble > 0f)
            {
                bubble.AddHeat(heatPerBubble);
            }
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

    /// <summary>
    /// Pushes the flower's heat to every renderer drawing it. Heat colours the grain from ash to white-hot and
    /// stretches it upward into flame lines; while blooming and releasing it stays at full heat.
    /// </summary>
    private void UpdatePresentation(float deltaTime)
    {
        // Chunks appear and are rebuilt while the shapes spread, so look for new ones now and then.
        if (Time.time >= nextRendererScan || heatRenderers.Exists(r => r == null))
        {
            nextRendererScan = Time.time + 0.5f;
            heatRenderers.Clear();
            foreach (Renderer candidate in GetComponentsInChildren<Renderer>(true))
                if (candidate.sharedMaterial != null && candidate.sharedMaterial.HasProperty(HeatId) && CreatureBounds.BelongsTo(candidate.transform, transform))
                    heatRenderers.Add(candidate);
            if (flowerRenderer != null && !heatRenderers.Contains(flowerRenderer)) heatRenderers.Add(flowerRenderer);
        }

        float heatRatio = Mathf.Clamp01(Heat / Mathf.Max(0.01f, bloomHeatThreshold));
        float heat = Mathf.Max(heatRatio, bloomAmount);
        visualProperties ??= new MaterialPropertyBlock();
        foreach (Renderer target in heatRenderers)
        {
            if (target == null) continue;
            // Keep whatever else is in the block (the shared hurt tint writes there too).
            target.GetPropertyBlock(visualProperties);
            visualProperties.SetFloat(HeatId, heat);
            visualProperties.SetFloat(GrowthId, Mathf.Lerp(0.25f, 0.8f, heat));
            target.SetPropertyBlock(visualProperties);
        }
    }

    private void InitializeVisuals()
    {
        if (flowerRenderer != null)
        {
            return;
        }

        foreach (Renderer candidate in GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material material in candidate.sharedMaterials)
            {
                if (material != null && material.HasProperty(GrowthId))
                {
                    flowerRenderer = candidate;
                    return;
                }
            }
        }
    }

    /// <summary>Captures each shape's current local pose as its spread origin.</summary>
    private void InitializeShapeSlots()
    {
        for (int index = 0; index < densityShapes.Count; index++)
        {
            ShapeSlot slot = densityShapes[index];
            if (slot == null || slot.shape == null)
            {
                continue;
            }

            slot.startLocalPosition = slot.shape.transform.localPosition;
            slot.initialized = true;
        }

        RerollSpreadDirections();
    }

    private void EnsureShapeSlotsInitialized()
    {
        for (int index = 0; index < densityShapes.Count; index++)
        {
            ShapeSlot slot = densityShapes[index];
            if (slot == null || slot.shape == null)
            {
                continue;
            }

            if (!slot.initialized)
            {
                InitializeShapeSlots();
                return;
            }
        }
    }

    /// <summary>Picks a fresh unit direction in local space for every shape (called at the start of each heating cycle).</summary>
    private void RerollSpreadDirections()
    {
        if (petalCrown)
        {
            // An even crown; only its turn changes from one cycle to the next.
            crownYaw = Random.Range(-crownJitter, crownJitter);
            return;
        }

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
    /// Heating / Blooming target full spread; Recovering returns to center.
    /// Spread amount is smoothed so a sudden Heat fill still shows outward motion.
    /// </summary>
    private void UpdateShapeMotion(float deltaTime)
    {
        EnsureShapeSlotsInitialized();

        float targetSpread = 0f;

        switch (currentState)
        {
            case FireFlowerState.Closed:
                targetSpread = 0f;
                break;

            case FireFlowerState.Heating:
                targetSpread = Mathf.Clamp01(Heat / Mathf.Max(0.01f, bloomHeatThreshold));
                break;

            case FireFlowerState.Blooming:
            case FireFlowerState.Releasing:
                targetSpread = 1f;
                break;

            case FireFlowerState.Recovering:
            {
                float safeDuration = Mathf.Max(0.01f, recoveryDuration);
                targetSpread = 1f - Mathf.Clamp01(stateTime / safeDuration);
                break;
            }
        }

        float spreadSpeed = currentState == FireFlowerState.Recovering
            ? 1f / Mathf.Max(0.01f, recoveryDuration)
            : 1f / Mathf.Max(0.01f, spreadOutDuration);

        shapeSpreadAmount = Mathf.MoveTowards(shapeSpreadAmount, targetSpread, spreadSpeed * deltaTime);
        ApplyShapeSpread(shapeSpreadAmount);
        ApplyBodyRadius(shapeSpreadAmount);
    }

    private void ApplyShapeSpread(float spread)
    {
        float amount = Mathf.Clamp01(spread);
        float distance = amount * maxSpreadDistance;

        if (petalCrown)
        {
            ApplyCrown(amount, distance);
            return;
        }

        for (int index = 0; index < densityShapes.Count; index++)
        {
            ShapeSlot slot = densityShapes[index];
            if (slot == null || slot.shape == null || !slot.initialized)
            {
                continue;
            }

            slot.shape.transform.localPosition = slot.startLocalPosition + slot.spreadDirection * distance;
        }
    }

    /// <summary>
    /// Petals pivot on their start position (inside the top of the bud). Each leans out along its own heading,
    /// from upright when closed to the open angle, and slides further out as the flower heats; the shape is
    /// turned so its long axis (local Y) points along the petal.
    /// </summary>
    private void ApplyCrown(float amount, float distance)
    {
        int count = CountValidShapes();
        if (count == 0) return;
        float lean = Mathf.Lerp(crownClosedAngle, crownOpenAngle, amount);
        int slotIndex = 0;
        for (int index = 0; index < densityShapes.Count; index++)
        {
            ShapeSlot slot = densityShapes[index];
            if (slot == null || slot.shape == null || !slot.initialized) continue;
            float heading = crownYaw + slotIndex * 360f / count;
            slotIndex++;
            Vector3 direction = Quaternion.Euler(0f, heading, 0f) * Quaternion.Euler(lean, 0f, 0f) * Vector3.up;
            slot.spreadDirection = direction;
            slot.shape.transform.localPosition = slot.startLocalPosition + direction * (petalReach + distance);
            slot.shape.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
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
        spreadOutDuration = Mathf.Max(0.01f, spreadOutDuration);
        bodyRadiusMin = Mathf.Max(0.01f, bodyRadiusMin);
        bodyRadiusMax = Mathf.Max(0.01f, bodyRadiusMax);
        petalReach = Mathf.Max(0f, petalReach);
    }
}
