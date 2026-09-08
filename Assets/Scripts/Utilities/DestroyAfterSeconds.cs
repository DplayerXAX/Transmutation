using UnityEngine;

/// <summary>
/// Destroys this GameObject after a configurable delay (seconds).
/// </summary>
public class DestroyAfterSeconds : MonoBehaviour
{
	[SerializeField]
	[Tooltip("Seconds to wait before destroying this GameObject.")]
	float seconds = 5f;

	void Start()
	{
		Destroy(gameObject, Mathf.Max(0f, seconds));
	}
}
