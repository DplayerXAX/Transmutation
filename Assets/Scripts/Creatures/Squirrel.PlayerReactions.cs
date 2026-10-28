using UnityEngine;
using UnityEngine.SceneManagement;

public enum NestPlayerAction { Deposit, Withdraw, Carry }

public sealed partial class Squirrel
{
    [Header("Witnessing player actions")]
    [SerializeField, Min(1f)] private float playerSightRange = 18f;
    [SerializeField, Range(10f, 170f)] private float playerSightAngle = 120f;
    [SerializeField, Min(1f)] private float happyOrbitRadius = 2.5f;
    [SerializeField, Min(1f)] private float warningRange = 6f;
    [SerializeField, Min(0.1f)] private float warningDuration = 2f;
    [SerializeField, Min(0.1f)] private float continuedStareDuration = 2f;
    [SerializeField, Min(20f)] private float escapeSpeed = 80f;
    [Tooltip("Optional warning sound. A red flashing marker and log also signal the warning.")]
    [SerializeField] private AudioClip warningClip;
    private AudioSource warningAudio;
    private Renderer warningMarker;
    private PlayerCreatureCarrier observedPlayer;
    private SquirrelNest observedNest;
    private SquirrelFood stolenFood;
    private Vector3 originalNestGround, socialDestination, lastPlayerPosition;
    private float socialTime, stareTime, orbitTravel, previousOrbitAngle, hopTime;
    private bool hasEscapeSite;
    private Activity resumeAfterSocial;
    public int WarningCount { get; private set; }
    public bool IsEscapingPlayer => activity == Activity.EscapingWithNest;
    public bool IsSocialReaction => activity >= Activity.Celebrating;
    public bool WarningVisible => warningMarker != null && warningMarker.enabled;

    public bool CanSeePlayer(PlayerCreatureCarrier player)
    {
        if (player == null || !player.isActiveAndEnabled || player.gameObject.scene != gameObject.scene ||
            !CanMoveAutonomously || IsDead || FlashBlindness.IsBlinded(this)) return false;
        Vector3 eye = player.CarryCamera != null ? player.CarryCamera.transform.position : player.transform.position + Vector3.up;
        Vector3 offset = eye - AimPosition;
        Vector3 flat = Vector3.ProjectOnPlane(offset, Vector3.up);
        if (offset.sqrMagnitude > playerSightRange * playerSightRange || flat.sqrMagnitude < 0.001f ||
            Vector3.Dot(transform.forward, flat.normalized) < Mathf.Cos(playerSightAngle * 0.5f * Mathf.Deg2Rad)) return false;
        if (!gameObject.scene.GetPhysicsScene().Raycast(AimPosition, offset.normalized, out var hit,
                offset.magnitude, ~0, QueryTriggerInteraction.Ignore)) return true;
        return hit.collider.transform.IsChildOf(player.transform.root) ||
            hit.collider.GetComponentInParent<Creature>()?.Carrier == player;
    }

    public void WitnessNestAction(PlayerCreatureCarrier player, SquirrelNest home, NestPlayerAction action, SquirrelFood food)
    {
        if (home == null || home.Owner != this || !CanSeePlayer(player) || activity == Activity.Fighting) return;
        // Protecting a witnessed nest takes priority over a second gift or food theft.
        if (IsSocialReaction && activity >= Activity.FollowingNest) return;
        if (action == NestPlayerAction.Deposit && IsSocialReaction) return;
        CancelSocialReaction();
        CancelMigration();
        resumeAfterSocial = IsNestWork ? activity : carried.Count > 0 ? Activity.Returning : Activity.Searching;
        if (target != null) target.Release(this);
        target = null;
        observedPlayer = player; observedNest = home; stolenFood = food;
        lastPlayerPosition = player.transform.position;
        socialTime = stareTime = orbitTravel = hopTime = 0f;
        motor.ResetRoute();
        if (action == NestPlayerAction.Deposit)
        {
            activity = Activity.Celebrating;
            previousOrbitAngle = OrbitAngle();
        }
        else if (action == NestPlayerAction.Withdraw) activity = Activity.ReclaimingFood;
        else
        {
            originalNestGround = MemoryFor(home)?.Position ?? home.transform.position;
            nestHalfExtents = home.GetComponent<Collider>()?.bounds.extents ?? Vector3.one * 0.5f;
            activity = Activity.FollowingNest;
        }
    }

    private float OrbitAngle()
    {
        Vector3 d = transform.position - observedPlayer.transform.position;
        return Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
    }
    private bool SocialMove(Vector3 point, float speed, Collider destination = null, float reach = 0.8f, float hop = 0f)
    {
        Vector3 offset = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
        if (offset.sqrMagnitude <= reach * reach) { if (hop > 0f) motor.SetIntent(Vector3.zero, hop); return true; }
        motor.SetIntent(offset.normalized * speed, hop, destination, point);
        return false;
    }
    private bool MutualStare()
    {
        if (!CanSeePlayer(observedPlayer)) return false;
        Vector3 toSquirrel = Vector3.ProjectOnPlane(AimPosition - observedPlayer.transform.position, Vector3.up);
        if (toSquirrel.sqrMagnitude > warningRange * warningRange) return false;
        Vector3 facing = observedPlayer.CarryCamera != null ? observedPlayer.CarryCamera.transform.forward : observedPlayer.transform.forward;
        return Vector3.Dot(Vector3.ProjectOnPlane(facing, Vector3.up).normalized, toSquirrel.normalized) >= 0.7f;
    }
    private void FacePlayer()
    {
        Vector3 toward = Vector3.ProjectOnPlane(observedPlayer.transform.position - transform.position, Vector3.up);
        if (toward.sqrMagnitude > 0.001f) GetComponent<Rigidbody>().MoveRotation(Quaternion.LookRotation(toward));
    }
    private bool TickSocialReaction(float dt)
    {
        if (!IsSocialReaction) return false;
        if (observedPlayer == null || !observedPlayer.isActiveAndEnabled || observedPlayer.gameObject.scene != gameObject.scene ||
            observedNest == null || observedNest.IsDead)
        { CancelSocialReaction(); return false; }
        socialTime += dt;
        if (warningMarker != null) warningMarker.enabled = activity == Activity.WarningPlayer && Mathf.Repeat(socialTime, 0.3f) < 0.15f;
        switch (activity)
        {
            case Activity.Celebrating:
                float angle = OrbitAngle();
                // Count actual angular travel, rather than declaring completion after a timer.
                orbitTravel += Mathf.Max(0f, Mathf.DeltaAngle(previousOrbitAngle, angle));
                previousOrbitAngle = angle;
                if (orbitTravel >= 355f || socialTime > 25f) { FinishSocialReaction(); break; }
                float radians = (angle + 28f) * Mathf.Deg2Rad;
                SocialMove(observedPlayer.transform.position + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * happyOrbitRadius,
                    Mathf.Max(moveSpeed * 2f, 5f), reach: 0.15f);
                break;
            case Activity.ReclaimingFood:
                if (stolenFood == null || observedPlayer.CarriedCreature != stolenFood || socialTime > 12f)
                { FinishSocialReaction(); break; }
                hopTime -= dt;
                float jump = hopTime <= 0f ? 7f : 0f;
                if (jump > 0f) hopTime = 0.65f;
                Vector3 hand = stolenFood.transform.position;
                SocialMove(hand, Mathf.Max(8f, moveSpeed * 3f), stolenFood.GetComponent<Collider>(), 0.5f, jump);
                // Must physically reach the held item while airborne; never create a replacement item.
                if ((AimPosition - hand).sqrMagnitude <= 2.25f && GetComponent<Rigidbody>().linearVelocity.y > 0.1f)
                {
                    observedPlayer.Release();
                    if (!stolenFood.IsCarried && stolenFood.Reserve(this))
                    { stolenFood.HideInside(this); carried.Add(stolenFood); }
                    nest = observedNest; RememberNest(nest); territoryCenter = nest.transform.position;
                    resumeAfterSocial = Activity.Returning;
                    FinishSocialReaction();
                }
                break;
            case Activity.FollowingNest:
                if (observedNest.IsCarried)
                {
                    // Follow the witnessed carrier, but search the last seen point behind an obstruction.
                    if (CanSeePlayer(observedPlayer)) lastPlayerPosition = observedPlayer.transform.position;
                    SocialMove(lastPlayerPosition, Mathf.Max(9f, moveSpeed * 3f), reach: 2.5f);
                }
                else if (!observedNest.IsMoving && SocialMove(observedNest.transform.position, Mathf.Max(8f, moveSpeed * 3f),
                             observedNest.GetComponent<Collider>(), motor.BodyRadius + 1.2f) && observedNest.BeginMove(this))
                {
                    nest = observedNest;
                    socialDestination = originalNestGround;
                    activity = Activity.ReturningStolenNest;
                    motor.ResetRoute();
                }
                break;
            case Activity.ReturningStolenNest:
                if (!observedNest.IsMoving) { FinishSocialReaction(); break; }
                if (!SocialMove(socialDestination, Mathf.Max(5f, moveSpeed * 2f), reach: 1f)) break;
                FacePlayer();
                if (MutualStare())
                {
                    activity = Activity.WarningPlayer; socialTime = stareTime = 0f;
                    WarningCount++;
                    if (warningMarker != null) warningMarker.enabled = true;
                    if (warningClip != null)
                    {
                        if (warningAudio == null) { warningAudio = gameObject.AddComponent<AudioSource>(); warningAudio.spatialBlend = 1f; }
                        warningAudio.PlayOneShot(warningClip);
                    }
                    Debug.Log("Squirrel warning triggered: leave my nest! (red flashing marker)", this);
                }
                else TryFinishNestReturn();
                break;
            case Activity.WarningPlayer:
                // Facing is held where the warning started: looking away breaks continuous eye contact.
                if (!MutualStare()) { TryFinishNestReturn(); break; }
                stareTime += dt;
                if (stareTime >= warningDuration + continuedStareDuration)
                {
                    activity = Activity.EscapingWithNest; hasEscapeSite = false; socialTime = 0f;
                    if (warningMarker != null) warningMarker.enabled = false;
                    motor.ResetRoute();
                }
                break;
            case Activity.EscapingWithNest:
                if (!observedNest.IsMoving) { FinishSocialReaction(); break; }
                if (!hasEscapeSite || !SafeEscapeSite(socialDestination, out _))
                { hasEscapeSite = FindEscapeSite(); motor.ResetRoute(); }
                float playerSpeed = observedPlayer.PlayerBody != null
                    ? Vector3.ProjectOnPlane(observedPlayer.PlayerBody.linearVelocity, Vector3.up).magnitude : 0f;
                float speed = Mathf.Max(escapeSpeed, playerSpeed + 20f);
                if (hasEscapeSite)
                {
                    if (SocialMove(socialDestination, speed, reach: 1f)) TryFinishNestReturn();
                }
                else
                {
                    // No valid site yet: retain the real nest and keep searching instead of dropping it illegally.
                    Vector3 away = Vector3.ProjectOnPlane(transform.position - observedPlayer.transform.position, Vector3.up).normalized;
                    SocialMove(transform.position + (away.sqrMagnitude > 0f ? away : transform.forward) * 8f, speed);
                }
                break;
        }
        return true;
    }

    private bool SafeEscapeSite(Vector3 point, out Vector3 ground)
    {
        ground = point;
        if (Vector3.ProjectOnPlane(point - observedPlayer.transform.position, Vector3.up).sqrMagnitude < 144f) return false;
        bool nearTree = false;
        foreach (var tree in foodManager.Trees)
            if (tree != null && (tree.GroundPosition - point).sqrMagnitude <= 36f) { nearTree = true; break; }
        if (!nearTree) return false;
        foreach (var home in SquirrelNest.All)
        {
            if (home == null || home == observedNest || home.IsDead || home.gameObject.scene != gameObject.scene) continue;
            float clearance = home.Owner == this ? 3.5f : otherOwnerNestClearance;
            if (Vector3.ProjectOnPlane(home.transform.position - point, Vector3.up).sqrMagnitude < clearance * clearance) return false;
        }
        return TryNestGround(point, out ground);
    }
    private bool FindEscapeSite()
    {
        float best = float.PositiveInfinity;
        bool found = false;
        foreach (var tree in foodManager.Trees)
        {
            if (tree == null) continue;
            for (int i = 0; i < 12; i++)
            {
                Vector3 candidate = tree.GroundPosition + Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * 4f;
                if (!SafeEscapeSite(candidate, out var ground)) continue;
                float score = (ground - transform.position).sqrMagnitude;
                if (score >= best) continue;
                best = score; socialDestination = ground; found = true;
            }
        }
        return found;
    }
    private void TryFinishNestReturn()
    {
        bool escaping = activity == Activity.EscapingWithNest;
        Vector3 ground;
        bool valid = escaping ? SafeEscapeSite(socialDestination, out ground) : TryNestGround(socialDestination, out ground);
        if (!valid)
        {
            activity = Activity.EscapingWithNest; hasEscapeSite = false;
            return;
        }
        observedNest.EndMove(ground);
        nest = observedNest;
        RememberNest(nest); territoryCenter = nest.transform.position;
        foodShortageTime = 0f;
        resumeAfterSocial = carried.Count > 0 ? Activity.Returning : Activity.Searching;
        FinishSocialReaction();
    }
    private void FinishSocialReaction()
    {
        bool celebrated = activity == Activity.Celebrating;
        if (warningMarker != null) warningMarker.enabled = false;
        if (celebrated) patrolPlayer = observedPlayer;
        observedPlayer = null; observedNest = null; stolenFood = null;
        // Empty-handed squirrels leave to forage/patrol away from the player immediately.
        // Only squirrels carrying food need to approach the nest to unload it.
        activity = celebrated && resumeAfterSocial != Activity.GatheringNestMaterials &&
            resumeAfterSocial != Activity.BuildingNest && nest != null && !nest.IsDead
            ? (carried.Count > 0 ? Activity.Returning : Activity.Searching) : resumeAfterSocial;
        motor.ResetRoute(); nextSearch = 0f;
        hasPatrolPoint = false;
    }
    private void CancelSocialReaction()
    {
        if (!IsSocialReaction) return;
        if (observedNest != null && observedNest.IsMoving)
        {
            Vector3 point = TryNestGround(transform.position, out var ground) ? ground : originalNestGround;
            observedNest.EndMove(point);
            RememberNest(observedNest);
            territoryCenter = observedNest.transform.position;
        }
        FinishSocialReaction();
    }

    private void CreateFacingMarkers()
    {
        // Authored FBX visuals already carry the facing colors; legacy placeholders use plates.
        if (transform.Find("Squirrel Visual") == null)
        {
            CreateMarker("Front (green)", new Vector3(0f, 0f, 0.51f), new Vector3(0.75f, 0.7f, 0.04f), new Color(0.15f, 0.95f, 0.35f));
            CreateMarker("Back (blue)", new Vector3(0f, 0f, -0.51f), new Vector3(0.75f, 0.7f, 0.04f), new Color(0.15f, 0.35f, 1f));
        }
        warningMarker = CreateMarker("Warning flash", new Vector3(0f, 0.9f, 0f), Vector3.one * 0.25f, Color.red);
        warningMarker.enabled = false;
    }
    private Renderer CreateMarker(string label, Vector3 position, Vector3 scale, Color color)
    {
        var existing = transform.Find(label);
        if (existing != null) return existing.GetComponent<Renderer>();
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = label;
        marker.transform.SetParent(transform, false);
        marker.transform.localPosition = position; marker.transform.localScale = scale;
        var shape = marker.GetComponent<Collider>(); shape.enabled = false; Destroy(shape);
        var renderer = marker.GetComponent<Renderer>();
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color); block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
        return renderer;
    }
}
