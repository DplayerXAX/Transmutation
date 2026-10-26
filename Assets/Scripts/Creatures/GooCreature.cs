using UnityEngine;

/// <summary>
/// A portable, passive blob consumed to coat the player's climbing hands. Applied with the crosshair on a fuzzy
/// creature, it coats the fuzzy instead (which keeps it for later). Thrown at a fuzzy it coats it too; thrown at a
/// wall or the ground it leaves a climbable splat (GooPaint).
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class GooCreature : Creature
{
    [SerializeField] private Transform blob;
    [Range(0f, 0.1f)] [SerializeField] private float idleWobble = 0.035f;
    private Vector3 blobScale;
    private bool usePending;

    public override bool CanBeCarried => !usePending && !IsDead;
    public override bool CanUseWhileCarried => !IsDead;
    public override string InteractionPrompt => IsDead ? null : "Apply Goo";
    public override string CarriedPrompt => IsDead ? null : "[LMB]  Apply Goo";

    protected override void InitializeCreature()
    {
        SetCreatureName("Goo");
        if (blob != null) blobScale = blob.localScale;
    }

    protected override void TickCreature(float deltaTime)
    {
        if (blob == null) return;
        float wobble = Mathf.Sin(Time.time * 2.1f + GetInstanceID()) * idleWobble;
        blob.localScale = Vector3.Scale(blobScale, new Vector3(1f + wobble, 1f - wobble, 1f + wobble));
    }

    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (usePending || IsDead || IsConsumptionPending || IsBubbleCaptured || !isActiveAndEnabled || !SimulationEnabled ||
            interaction.Source == null) return;
        // Crosshair on a fuzzy creature close by: it gets the goo instead of the hands.
        FuzzCreature fuzz = FuzzInSight();
        if (fuzz != null)
        {
            if (fuzz.TryCoatWithGoo()) UseUp();
            return;
        }
        PlayerGooCoating coating = interaction.Source.GetComponentInParent<PlayerGooCoating>();
        if (coating == null)
        {
            // The prefab's CameraHolder is a sibling of Player, not its child.
            PlayerCreatureCarrier sourceCarrier = interaction.Source.GetComponentInParent<PlayerCreatureCarrier>();
            if (sourceCarrier == null)
                sourceCarrier = interaction.Source.transform.root.GetComponentInChildren<PlayerCreatureCarrier>();
            if (sourceCarrier != null && sourceCarrier.PlayerController != null)
                coating = sourceCarrier.PlayerController.GetComponent<PlayerGooCoating>();
        }
        if (coating == null || coating.gameObject.scene != gameObject.scene ||
            (IsCarried && Carrier.PlayerController != coating.GetComponent<SmoothFirstPersonController>())) return;

        // Claim before notifying supply listeners: callbacks cannot use this same blob twice.
        usePending = true;
        if (!coating.TryApplyGoo()) { usePending = false; return; }
        UseUp();
    }

    private void UseUp()
    {
        usePending = true;
        if (Carrier != null) Carrier.Release();
        CreatureRegistry.Unregister(this);
        Die();
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    private FuzzCreature FuzzInSight()
    {
        Camera view = Camera.main;
        if (view == null) return null;
        foreach (RaycastHit hit in Physics.RaycastAll(view.transform.position, view.transform.forward, 4f, ~0, QueryTriggerInteraction.Ignore))
        {
            FuzzCreature fuzz = hit.collider.GetComponentInParent<FuzzCreature>();
            if (fuzz != null && !fuzz.IsDead) return fuzz;
        }
        return null;
    }

    /// <summary>
    /// A throw hit something: a fuzzy creature takes the goo, a wall or the ground gets a climbable splat.
    /// True when the blob was used up (the throw ends there).
    /// </summary>
    public bool SplatOn(RaycastHit hit)
    {
        if (usePending || IsDead || hit.collider == null) return false;
        FuzzCreature fuzz = hit.collider.GetComponentInParent<FuzzCreature>();
        if (fuzz != null)
        {
            if (!fuzz.TryCoatWithGoo()) return false;
            UseUp();
            return true;
        }
        if (hit.collider.GetComponentInParent<Creature>() != null) return false;
        Renderer look = blob != null ? blob.GetComponentInChildren<Renderer>() : GetComponentInChildren<Renderer>();
        GooPaint.Splat(hit.point, hit.normal, look != null ? look.sharedMaterial : null);
        UseUp();
        return true;
    }
}
