using UnityEngine;

/// <summary>A portable, passive blob consumed to coat the player's climbing hands.</summary>
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
        if (Carrier != null) Carrier.Release();
        CreatureRegistry.Unregister(this);
        Die();
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
