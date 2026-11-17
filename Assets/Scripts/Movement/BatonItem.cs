using UnityEngine;

/// <summary>
/// Makes the conductor baton an ordinary hand-held thing: carried in the hand like a creature (RMB picks it up, the
/// mouse wheel puts it away on a body slot like any held creature), so the hotbar and carry code need nothing new.
/// Not alive: uses Creature only for pickup, carry and the hotbar (like CaveHarvestItem).
/// While held, RMB on a glow creature mutes it (ConductorBaton). Once it is the hotbar's tool (BodyMounts.SetTool) RMB never
/// puts it down or throws it; otherwise RMB anywhere else puts it down as usual.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class BatonItem : Creature
{
    public ConductorBaton Baton { get; set; }

    public override bool IgnoreCarrySizeLimit => true;
    public override bool CanBeCarried => Baton == null || Baton.CanBeTaken;

    public override bool TryHandleCarriedRightMouse(PlayerCreatureCarrier carrier, bool pressed, bool held, bool released) =>
        Baton != null && (Baton.HandleRightMouse(pressed, held, released) || Baton.IsTool); // a tool is never dropped

    protected override void TickCreature(float deltaTime) { }
}
