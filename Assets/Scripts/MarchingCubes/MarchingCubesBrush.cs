using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mouse dig / grow brush for MarchingCubesSphere using the new Input System.
///
/// Left mouse  → dig   (subtract density)
/// Right mouse → grow  (add density)
///
/// Mouse → Camera Ray → Physics.Raycast (LayerMask) → hit.point → ModifyTerrain()
/// </summary>
public class MarchingCubesBrush : MonoBehaviour
{
	[SerializeField] MarchingCubesSphere terrain;

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

	float nextModificationTime;

	void Awake()
	{
		if (terrain == null)
		{
			terrain = GetComponent<MarchingCubesSphere>();
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

		// ModifyTerrain subtracts strength; pass negative strength to grow.
		float signedStrength = dig ? strength : -strength;
		terrain.ModifyTerrain(hit.point, brushRadius, signedStrength);
		nextModificationTime = Time.time + modificationInterval;
	}
}
