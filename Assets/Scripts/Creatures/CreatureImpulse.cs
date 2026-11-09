using UnityEngine;

/// <summary>One physical impulse. Never changes carry/capture ownership or delivers damage.</summary>
public static class CreatureImpulse
{
    public static void Apply(Rigidbody body, Vector3 velocityChange, float yieldSeconds = 0.18f)
    {
        if (body == null || body.isKinematic || !body.detectCollisions) return;
        if (body.TryGetComponent<Creature>(out var creature) &&
            (creature.IsCarried || creature.IsBubbleCaptured || creature.IsConsumptionPending ||
             (creature.TryGetComponent<WebCapture>(out var web) && web.IsCaptured))) return;
        if (body.TryGetComponent<SmoothFirstPersonController>(out var player))
        { player.AddBlastImpulse(velocityChange); return; }
        if (body.TryGetComponent<AsuraMotor>(out var motor))
        {
            velocityChange *= motor.KnockbackMultiplier;
            motor.AcceptPush(yieldSeconds);
        }
        body.AddForce(velocityChange, ForceMode.VelocityChange);
    }
}
