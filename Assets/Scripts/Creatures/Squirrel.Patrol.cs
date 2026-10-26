using UnityEngine;

public sealed partial class Squirrel
{
    private Vector3 patrolPoint;
    private bool hasPatrolPoint;
    private float patrolTime;
    private PlayerCreatureCarrier patrolPlayer;

    // Food search remains higher priority. Keep locomotion intent alive between search intervals.
    private void TickIdlePatrol(float deltaTime)
    {
        patrolTime -= deltaTime;
        bool playerTooCloseToGoal = patrolPlayer != null &&
            Vector3.ProjectOnPlane(patrolPoint - patrolPlayer.transform.position, Vector3.up).sqrMagnitude < 16f;
        if (!hasPatrolPoint || patrolTime <= 0f || playerTooCloseToGoal ||
            Vector3.ProjectOnPlane(patrolPoint - transform.position, Vector3.up).sqrMagnitude < 0.5f)
        {
            hasPatrolPoint = false;
            patrolTime = 5f; // Also retries a blocked route instead of waiting there forever.
            float radius = Mathf.Min(territoryRadius * 0.85f, 7f);
            float best = float.NegativeInfinity;
            for (int i = 0; i < 20; i++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                Vector3 point = territoryCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) *
                    Random.Range(radius * 0.45f, radius);
                if (Vector3.ProjectOnPlane(point - transform.position, Vector3.up).sqrMagnitude < 2.25f) continue;
                if (!gameObject.scene.GetPhysicsScene().Raycast(point + Vector3.up * 30f, Vector3.down,
                    out RaycastHit hit, 65f, 1 << 7, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f) continue;
                float score = patrolPlayer != null
                    ? Vector3.ProjectOnPlane(hit.point - patrolPlayer.transform.position, Vector3.up).sqrMagnitude
                    : Vector3.ProjectOnPlane(hit.point - transform.position, Vector3.up).sqrMagnitude;
                if (score <= best) continue;
                best = score;
                patrolPoint = hit.point;
                hasPatrolPoint = true;
            }
            motor.ResetRoute();
        }
        if (!hasPatrolPoint) return;
        Vector3 direction = Vector3.ProjectOnPlane(patrolPoint - transform.position, Vector3.up);
        motor.SetIntent(direction.normalized * moveSpeed * 0.65f, goal: patrolPoint);
    }
}
