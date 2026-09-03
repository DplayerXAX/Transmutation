using UnityEngine;

/// <summary>
/// Continuously rotates around local X at a constant angular speed (degrees per second).
/// </summary>
public class XRotator : MonoBehaviour
{
	[SerializeField]
	[Tooltip("Degrees per second around local X. Negative reverses direction.")]
	float degreesPerSecond = 45f;

	[SerializeField]
	[Tooltip("When enabled, rotates in world space around the global X axis.")]
	bool useWorldSpace;

	void Update()
	{
		if (Mathf.Approximately(degreesPerSecond, 0f))
		{
			return;
		}

		float delta = degreesPerSecond * Time.deltaTime;
		transform.Rotate(Vector3.right, delta, useWorldSpace ? Space.World : Space.Self);
	}
}
