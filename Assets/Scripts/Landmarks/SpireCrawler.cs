using UnityEngine;

/// <summary>
/// Moves a TentacleCreature slowly over the outside of the Hanging Spire: it picks a nearby spot on
/// the wall, crawls there, rests, and picks another, staying between the base and the crown.
/// Once the player picks it up, it lets go of the tower for good and behaves like any other creature.
/// </summary>
[DisallowMultipleComponent]
public sealed class SpireCrawler : MonoBehaviour
{
    [Tooltip("The tower wall to crawl on.")]
    public Collider wall;
    [Tooltip("Base of the tower axis (the spire's root position).")]
    public Vector3 axisBase;
    public float minHeight = 3f;
    public float maxHeight = 60f;
    [Min(0f)] public float speed = 0.6f;
    [Tooltip("Distance of the body centre from the wall.")]
    [Min(0.1f)] public float standOff = 0.7f;

    private TentacleCreature creature;
    private float angle, height;
    private float targetAngle, targetHeight;
    private float restTimer;
    private float radiusGuess = 10f;

    public void Place(float startAngle, float startHeight)
    {
        angle = targetAngle = startAngle;
        height = targetHeight = startHeight;
        if (Surface(angle, height, out Vector3 point, out Vector3 normal))
            transform.position = point + normal * standOff;
    }

    private void Start()
    {
        creature = GetComponent<TentacleCreature>();
        if (creature != null) creature.ExternalMovement = true;
    }

    private void Update()
    {
        if (creature == null || wall == null) return;
        if (creature.IsCarried)
        {
            // Taken off the tower: from now on it is an ordinary creature.
            creature.ExternalMovement = false;
            Destroy(this);
            return;
        }

        float deltaTime = Time.deltaTime;
        float metresPerDegree = radiusGuess * Mathf.Deg2Rad;
        Vector2 toTarget = new Vector2(Mathf.DeltaAngle(angle, targetAngle) * metresPerDegree, targetHeight - height);
        if (toTarget.magnitude < 0.2f)
        {
            restTimer -= deltaTime;
            if (restTimer <= 0f) PickTarget();
        }
        else
        {
            Vector2 step = Vector2.ClampMagnitude(toTarget, speed * deltaTime);
            angle += step.x / metresPerDegree;
            height += step.y;
        }

        if (!Surface(angle, height, out Vector3 point, out Vector3 normal)) return;
        radiusGuess = Mathf.Max(2f, Vector3.ProjectOnPlane(point - axisBase, Vector3.up).magnitude);
        creature.CrawlNormal = normal;

        // Body hugs the wall, belly towards it, facing the way it crawls.
        Vector3 goal = point + normal * standOff;
        transform.position = Vector3.Lerp(transform.position, goal, 1f - Mathf.Exp(-5f * deltaTime));
        Vector3 heading = Vector3.ProjectOnPlane(Vector3.up * toTarget.y + Vector3.Cross(Vector3.up, normal) * toTarget.x, normal);
        if (heading.sqrMagnitude < 1e-4f) heading = Vector3.ProjectOnPlane(Vector3.up, normal);
        if (heading.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(heading.normalized, normal), 1f - Mathf.Exp(-3f * deltaTime));
    }

    private void PickTarget()
    {
        targetAngle = angle + Random.Range(-35f, 35f);
        targetHeight = Mathf.Clamp(height + Random.Range(-5f, 6f), minHeight, maxHeight);
        restTimer = Random.Range(1.5f, 5f);
    }

    /// <summary>The wall point at an angle around the tower and a height above its base.</summary>
    private bool Surface(float atAngle, float atHeight, out Vector3 point, out Vector3 normal)
    {
        Vector3 outward = Quaternion.Euler(0f, atAngle, 0f) * Vector3.forward;
        Vector3 axisPoint = axisBase + Vector3.up * atHeight;
        var ray = new Ray(axisPoint + outward * 40f, -outward);
        if (wall.Raycast(ray, out RaycastHit hit, 40f))
        {
            point = hit.point;
            normal = hit.normal;
            return true;
        }
        point = default;
        normal = outward;
        return false;
    }
}
