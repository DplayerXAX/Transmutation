using UnityEngine;

/// <summary>Neutral pickup; donor color is data, never the pickup's combat allegiance.</summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class AsuraBlood : Creature
{
    [SerializeField] private CreaturePolarity donorPolarity = CreaturePolarity.Light;
    private bool used;
    private bool applying;
    private MaterialPropertyBlock tint;
    public CreaturePolarity DonorPolarity => donorPolarity;
    public bool Used => used;
    public override bool Invulnerable => true;
    public override bool CanBeCarried => false;
    public override bool CanBeCaptured => false;
    public override bool CanBeConsumed => false;
    public override string InteractionPrompt => used || IsDead ? null : $"Coat with {donorPolarity} blood";

    protected override void InitializeCreature()
    {
        SetCreatureName("Asura Blood");
        SetFoodAndDiet(FoodTag.None, FoodTag.None);
        SetResolvedPolarity(CreaturePolarity.None);
        ConfigureLifespan(300f, 0f);
        RefreshColor();
    }
    protected override void TickCreature(float dt) { }
    public void SetDonor(CreaturePolarity donor)
    {
        if (donor != CreaturePolarity.Light && donor != CreaturePolarity.Dark)
            throw new System.ArgumentOutOfRangeException(nameof(donor));
        donorPolarity = donor;
        RefreshColor();
    }
    private void RefreshColor()
    {
        tint ??= new MaterialPropertyBlock();
        Color color = AsuraBloodPalette.For(donorPolarity);
        tint.SetColor("_BaseColor", color); tint.SetColor("_Color", color);
        foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.SetPropertyBlock(tint);
    }
    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (interaction.Source == null) return;
        var interactor = interaction.Source.GetComponent<PlayerCreatureInteractor>();
        if (interactor != null) TryCoat(interactor);
    }
    public bool TryCoat(PlayerCreatureInteractor interactor)
    {
        if (used || applying || !IsCombatActive || interactor == null || !interactor.CanInteractWithBlood(this)) return false;
        var player = interactor.PlayerStats;
        if (player == null) return false;
        bool applied;
        applying = true;
        try { applied = player.TryApplyBlood(donorPolarity); }
        finally { applying = false; }
        if (!applied) return false;
        // Claim immediately; Destroy is deferred, so a second input in this frame must fail.
        used = true;
        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }
}
