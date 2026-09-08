using UnityEngine;

/// <summary>
/// Spawns a prefab and launches it along this transform's local Y axis.
/// </summary>
public class Emitter : MonoBehaviour
{
	[Header("Projectile")]
	[SerializeField]
	[Tooltip("Prefab spawned and launched along local Y.")]
	GameObject prefab;

	[SerializeField]
	[Tooltip("Instant impulse applied along local Y (ForceMode.Impulse). For kinematic bodies this becomes an equivalent velocity change.")]
	[Min(0f)]
	float launchForce = 10f;

	[SerializeField]
	[Tooltip("Spawn offset from this transform, in local space.")]
	Vector3 spawnOffset = Vector3.up;

	[Header("Firing")]
	[SerializeField]
	[Tooltip("Seconds between automatic shots. Set to 0 to disable auto-fire.")]
	[Min(0f)]
	float fireInterval = 1f;

	[SerializeField]
	[Tooltip("Fire once immediately when Play starts.")]
	bool fireOnStart = true;

	[Header("Gizmos")]
	[SerializeField]
	[Tooltip("Length of the launch-direction arrow drawn in the Scene view.")]
	[Min(0.1f)]
	float gizmoLength = 2f;

	[SerializeField]
	Color gizmoColor = new Color(1f, 0.85f, 0.15f, 0.95f);

	float nextFireTime;

	/// <summary>World-space launch direction (local Y).</summary>
	public Vector3 LaunchDirection => transform.up;

	void Start()
	{
		if (fireOnStart)
		{
			Fire();
		}

		nextFireTime = Time.time + fireInterval;
	}

	void Update()
	{
		if (fireInterval <= 0f)
		{
			return;
		}

		if (Time.time < nextFireTime)
		{
			return;
		}

		Fire();
		nextFireTime = Time.time + fireInterval;
	}

	/// <summary>Spawns the prefab and launches it along local Y.</summary>
	[ContextMenu("Fire")]
	public void Fire()
	{
		if (prefab == null)
		{
			Debug.LogWarning($"{name}: Emitter has no prefab assigned.", this);
			return;
		}

		Vector3 position = transform.TransformPoint(spawnOffset);
		Quaternion rotation = Quaternion.LookRotation(LaunchDirection, transform.forward);
		GameObject instance = Instantiate(prefab, position, rotation);

		Rigidbody body = instance.GetComponentInChildren<Rigidbody>();
		if (body == null)
		{
			Debug.LogWarning($"{name}: Spawned prefab has no Rigidbody to receive launch force.", this);
			return;
		}

		Vector3 impulse = LaunchDirection * launchForce;

		// AddForce is ignored on kinematic rigidbodies; apply the Impulse equivalent as Δv = J / m.
		if (body.isKinematic)
		{
			float mass = Mathf.Max(body.mass, 0.0001f);
			body.linearVelocity = impulse / mass;
		}
		else
		{
			body.AddForce(impulse, ForceMode.Impulse);
		}
	}

	void OnDrawGizmos()
	{
		Vector3 origin = transform.TransformPoint(spawnOffset);
		Vector3 direction = transform.up;
		Vector3 tip = origin + direction * gizmoLength;

		Gizmos.color = gizmoColor;
		Gizmos.DrawLine(origin, tip);
		Gizmos.DrawWireSphere(origin, 0.08f);

		// Arrow head
		Vector3 right = Vector3.Cross(direction, transform.forward);
		if (right.sqrMagnitude < 0.0001f)
		{
			right = Vector3.Cross(direction, transform.right);
		}

		right.Normalize();
		Vector3 back = tip - direction * (gizmoLength * 0.2f);
		Gizmos.DrawLine(tip, back + right * (gizmoLength * 0.12f));
		Gizmos.DrawLine(tip, back - right * (gizmoLength * 0.12f));
	}
}
