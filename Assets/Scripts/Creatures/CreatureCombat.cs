using System.Collections.Generic;
using UnityEngine;

public enum CreatureCombatState { Idle, Approach, Windup, Release, Recovery }

[DisallowMultipleComponent]
public sealed class CreatureCombat : MonoBehaviour
{
    [Min(0.1f)] [SerializeField] private float moveSpeed = 3f;
    [Min(0.1f)] [SerializeField] private float detectionRange = 20f;
    [Min(0.1f)] [SerializeField] private float retentionRange = 25f;
    [Min(0.01f)] [SerializeField] private float searchInterval = 0.25f;
    [Min(0f)] [SerializeField] private float obstructionGrace = 1f;
    [SerializeField] private Transform firingPivot;
    [SerializeField] private Transform muzzle;
    private Creature owner;
    private AsuraMotor motor;
    private CreatureAttack attack;
    private float nextSearch, windupRemaining, nextRelease, obstructed;
    private readonly List<Creature> candidates = new List<Creature>(64);
    private readonly List<int> indices = new List<int>(64);
    private RaycastHit[] hits = new RaycastHit[16];
    public Creature Target { get; private set; }
    public CreatureCombatState State { get; private set; }
    public int Searches { get; private set; }
    public int Releases { get; private set; }
    public event System.Action<CreatureCombatState> StateChanged;

    private void Awake()
    {
        owner = GetComponent<Creature>(); motor = GetComponent<AsuraMotor>(); attack = GetComponent<CreatureAttack>();
        nextSearch = Time.time + (Mathf.Abs(GetInstanceID() % 997) / 997f) * searchInterval;
        if (firingPivot == null) firingPivot = transform.Find("Firing Pivot");
        if (muzzle == null) muzzle = firingPivot != null ? firingPivot.Find("Muzzle") : transform;
    }

    public bool Eligible(Creature candidate, float range)
    {
        return owner != null && candidate != null && candidate != owner && candidate.isActiveAndEnabled &&
            candidate.gameObject.scene == gameObject.scene && !candidate.IsDead && !candidate.IsConsumptionPending &&
            candidate.CanBeAITargeted && owner.CanBeAITargeted && candidate.Polarity != owner.Polarity &&
            CreatureSpatialQuery.TryGetSolid(candidate, out _) &&
            (candidate.AimPosition - owner.AimPosition).sqrMagnitude <= range * range;
    }

    public void Tick(float deltaTime)
    {
        if (owner == null || !owner.SimulationEnabled || owner.IsConsumptionPending || attack == null ||
            FlashBlindness.IsBlinded(owner)) { Cancel(); return; }
        if (Target != null && !Eligible(Target, retentionRange)) Cancel();
        if (Target == null && Time.time >= nextSearch)
        {
            nextSearch = Time.time + searchInterval; Searches++;
            CreatureSpatialQuery.Nearby(gameObject.scene, owner.AimPosition, detectionRange, candidates, indices);
            float best = float.PositiveInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                Creature candidate = candidates[i];
                if (!Eligible(candidate, detectionRange)) continue;
                float distance = (candidate.AimPosition - owner.AimPosition).sqrMagnitude;
                if (distance >= best || !CreatureSpatialQuery.Visible(owner, candidate, owner.AimPosition, ref hits)) continue;
                Target = candidate; best = distance;
            }
        }
        if (Target == null) { motor?.Stop(); SetState(CreatureCombatState.Idle); return; }
        Vector3 delta = Target.AimPosition - owner.AimPosition;
        Vector3 aimOrigin = muzzle != null ? muzzle.position : owner.AimPosition;
        bool visible = CreatureSpatialQuery.Visible(owner, Target, aimOrigin, ref hits);
        obstructed = visible ? 0f : obstructed + deltaTime;
        if (obstructed > obstructionGrace) { Cancel(); nextSearch = Time.time + searchInterval; return; }
        if (firingPivot != null)
        {
            Vector3 aim = Target.AimPosition - firingPivot.position;
            if (aim.sqrMagnitude > 0.0001f)
                firingPivot.rotation = Quaternion.RotateTowards(firingPivot.rotation, Quaternion.LookRotation(aim), 540f * deltaTime);
        }
        bool inRange = delta.sqrMagnitude <= attack.Range * attack.Range;
        if (State == CreatureCombatState.Windup)
        {
            motor?.SetIntent(Vector3.zero, delta);
            windupRemaining -= deltaTime;
            if (windupRemaining > 0f) return;
            if (Eligible(Target, attack.Range) && visible)
            {
                SetState(CreatureCombatState.Release);
                if (attack.Release(owner, Target, muzzle)) Releases++;
                nextRelease = Time.time + attack.Interval;
                SetState(CreatureCombatState.Recovery);
            }
            else SetState(CreatureCombatState.Approach);
            return;
        }
        // Recovery is mobile; only the committed windup stops pursuit.
        if (!inRange)
        {
            motor?.SetIntent(delta.normalized * moveSpeed, delta);
            SetState(CreatureCombatState.Approach);
            return;
        }
        motor?.SetIntent(Vector3.zero, delta);
        if (Time.time < nextRelease - attack.Windup) { SetState(CreatureCombatState.Recovery); return; }
        Vector3 forward = firingPivot != null ? firingPivot.forward : transform.forward;
        if (!visible || Vector3.Angle(forward, Target.AimPosition - aimOrigin) > 10f) return;
        windupRemaining = attack.Windup;
        SetState(CreatureCombatState.Windup);
    }

    public void Cancel()
    {
        Target = null; obstructed = 0f; windupRemaining = 0f;
        motor?.Stop(); SetState(CreatureCombatState.Idle);
    }
    private void SetState(CreatureCombatState state)
    {
        if (State == state) return;
        State = state;
        try { StateChanged?.Invoke(state); }
        catch (System.Exception exception) { Debug.LogException(exception, this); }
    }
}
