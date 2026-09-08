using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mouse dig / grow brush for MarchingCubesVolume using the new Input System.
///
/// Left mouse  → dig   (subtract density)
/// Right mouse → grow  (add density)
///
/// Mouse → Camera Ray → Physics.Raycast (LayerMask) → hit.point → ModifyTerrain()
/// </summary>
public class MarchingCubesBrush : MonoBehaviour
{
	[SerializeField] MarchingCubesVolume terrain;

	[Tooltip("Only colliders on these layers can be hit by the brush ray (exclude Player).")]
	[SerializeField] LayerMask brushLayers = ~0;

	[Tooltip("Max ray length so the brush works from far away.")]
	[SerializeField] float maxRayDistance = 10000f;

	[Tooltip("Brush radius in the terrain's local units (same space as spacing).")]
	[SerializeField] float brushRadius = 1.5f;

	[Tooltip("How strongly density changes at the brush center each modification.")]
	[SerializeField] float strength = 1f;

	[Tooltip("Minimum seconds between modifications while holding the mouse.")]
	[SerializeField] float modificationInterval = 0.05f;

	[Tooltip("Grow is blocked when the hit point is closer than this to the camera (or Grow Distance Origin). Dig is unaffected.")]
	[SerializeField] float minGrowDistance = 3f;

	[Tooltip("Optional origin for the grow distance check. Uses Camera.main when empty.")]
	[SerializeField] Transform growDistanceOrigin;

	float nextModificationTime;

	void Awake()
	{
		if (terrain == null)
		{
			terrain = GetComponent<MarchingCubesVolume>();
		}
	}

	void Update()
	{
		if (terrain == null || Camera.main == null || Mouse.current == null)
		{
			return;
		}

		bool dig = Mouse.current.leftButton.isPressed;
		bool grow = Mouse.current.rightButton.isPressed;

		if (!dig && !grow)
		{
			return;
		}

		if (Time.time < nextModificationTime)
		{
			return;
		}

		Vector2 screenPosition = Mouse.current.position.ReadValue();
		Ray ray = Camera.main.ScreenPointToRay(screenPosition);

		if (!Physics.Raycast(
			    ray,
			    out RaycastHit hit,
			    maxRayDistance,
			    brushLayers,
			    QueryTriggerInteraction.Ignore))
		{
			return;
		}

		// Chunk colliders live on children of the terrain root.
		if (!hit.collider.transform.IsChildOf(terrain.transform) &&
		    hit.collider.transform != terrain.transform)
		{
			return;
		}

		// Prevent growing into the player / camera; digging stays unrestricted.
		if (grow && !dig && minGrowDistance > 0f)
		{
			Vector3 origin = growDistanceOrigin != null
				? growDistanceOrigin.position
				: Camera.main.transform.position;

			if (Vector3.Distance(origin, hit.point) < minGrowDistance)
			{
				return;
			}
		}

		// ModifyTerrain subtracts strength; pass negative strength to grow.
		// If both buttons are held, prefer dig.
		float signedStrength = dig ? strength : -strength;
		terrain.ModifyTerrain(hit.point, brushRadius, signedStrength);
		nextModificationTime = Time.time + modificationInterval;
	}
}
