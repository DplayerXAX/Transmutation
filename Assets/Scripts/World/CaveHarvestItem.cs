using UnityEngine;

/// <summary>
/// A fruit or flower growing in the inner world that the player can pick (E) and carry in the hand.
/// Not a living creature: it only uses the Creature base so the existing pickup, prompt and carry work.
/// It hangs or stands still until picked; once picked it becomes a normal falling object.
/// </summary>
public sealed class CaveHarvestItem : Creature
{
    [Tooltip("Extra hint shown while held. Empty = none.")]
    [SerializeField] private string carriedHint = "";

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool picked;

    public bool Picked => picked;

    public override string CarriedPrompt => string.IsNullOrEmpty(carriedHint) ? null : carriedHint;

    /// <summary>Name shown in the pickup prompt.</summary>
    public void SetDisplayName(string displayName) => SetCreatureName(displayName);

    protected override void TickCreature(float deltaTime)
    {
        // The first pick frees it from its stalk: when let go it falls instead of floating in place.
        if (!picked && IsCarried)
        {
            picked = true;
            SetMovementPhysics(isKinematic: false, useGravity: true);
        }
    }
}
