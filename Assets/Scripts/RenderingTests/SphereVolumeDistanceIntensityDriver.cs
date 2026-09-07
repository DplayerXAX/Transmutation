using UnityEngine;
using UnityEngine.Rendering;

namespace Render_Test
{
	/// <summary>
	/// Drives <see cref="SphereVolumeComponent.Intensity"/> from distance to a target (e.g. player):
	/// intensity is 0 at the sphere collider surface (distance from center = world radius) and 1 when closer than <see cref="fullIntensityDistanceFromCenter"/>.
	/// </summary>
	[RequireComponent(typeof(Volume), typeof(SphereCollider))]
	public class SphereVolumeDistanceIntensityDriver : MonoBehaviour
	{
		[SerializeField]
		[Tooltip("Usually the player root. If null, uses Camera.main.transform at runtime.")]
		Transform player;

		[SerializeField]
		[Min(0f)]
		[Tooltip("When distance from volume center to player is at or below this (meters), Intensity = 1.")]
		float fullIntensityDistanceFromCenter = 2f;

		[SerializeField]
		[Tooltip("If true, Intensity = 0 when player is at or beyond the collider's world-space radius from center.")]
		bool zeroIntensityAtColliderRadius = true;

		[SerializeField]
		[Min(0.01f)]
		[Tooltip("Used when Zero Intensity At Collider Radius is false: distance from center at which Intensity = 0.")]
		float manualOuterDistanceFromCenter = 10f;

		[SerializeField]
		[Tooltip("If true, compares only XZ distance (ignores Y difference between player and volume center).")]
		bool compareOnlyXZ = true;

		Volume _volume;
		SphereCollider _sphere;
		SphereVolumeComponent _sphereVolume;

		void Awake()
		{
			_volume = GetComponent<Volume>();
			_sphere = GetComponent<SphereCollider>();

			if (player == null && Camera.main != null)
			{
				player = Camera.main.transform;
			}

			if (_volume.sharedProfile != null)
			{
				_volume.profile = Instantiate(_volume.sharedProfile);
			}

			if (_volume.profile != null && !_volume.profile.TryGet(out _sphereVolume))
			{
				Debug.LogError(
					"SphereVolumeDistanceIntensityDriver: Volume profile has no SphereVolumeComponent. Add it to this Volume's profile.",
					this);
				enabled = false;
			}
		}

		void LateUpdate()
		{
			if (_sphereVolume == null || player == null || _sphere == null)
			{
				return;
			}

			Vector3 worldCenter = _sphere.transform.TransformPoint(_sphere.center);
			Vector3 playerPos = player.position;
			if (compareOnlyXZ)
			{
				playerPos.y = worldCenter.y;
			}

			float dist = Vector3.Distance(worldCenter, playerPos);

			float outer = zeroIntensityAtColliderRadius ? GetWorldSphereRadius() : manualOuterDistanceFromCenter;
			float inner = fullIntensityDistanceFromCenter;

			float intensity;
			if (inner >= outer)
			{
				// Full intensity everywhere inside the outer radius.
				intensity = dist < outer ? 1f : 0f;
			}
			else if (dist <= inner)
			{
				intensity = 1f;
			}
			else if (dist >= outer)
			{
				intensity = 0f;
			}
			else
			{
				intensity = Mathf.InverseLerp(outer, inner, dist);
			}

			_sphereVolume.Intensity.overrideState = true;
			_sphereVolume.Intensity.value = Mathf.Clamp01(intensity);
		}

		float GetWorldSphereRadius()
		{
			Vector3 s = _sphere.transform.lossyScale;
			float m = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
			return _sphere.radius * m;
		}
	}
}
