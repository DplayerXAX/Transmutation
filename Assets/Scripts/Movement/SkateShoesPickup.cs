using UnityEngine;

/// <summary>A non-creature equipment pickup placed as a normal scene prefab.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
public sealed class SkateShoesPickup : MonoBehaviour
{
    private SkateShoesVisuals visuals;
    private bool claimed;

    private void Awake()
    {
        var collider = GetComponent<BoxCollider>();
        collider.size = new Vector3(0.75f, 0.5f, 0.6f);
        collider.center = Vector3.up * 0.21f;
        visuals = GetComponentInChildren<SkateShoesVisuals>(true);
        if (visuals == null)
        {
            var model = new GameObject("Skate shoes display");
            model.transform.SetParent(transform, false);
            model.transform.localPosition = Vector3.up * 0.13f;
            visuals = model.AddComponent<SkateShoesVisuals>();
        }
    }

    public bool TryEquip(SmoothFirstPersonController player)
    {
        if (claimed || !isActiveAndEnabled || player == null || !player.isActiveAndEnabled ||
            player.gameObject.scene != gameObject.scene || player.BubbleMovementBlocked) return false;
        claimed = true;
        var equipment = player.GetComponent<PlayerSkateShoes>();
        if (equipment == null) equipment = player.gameObject.AddComponent<PlayerSkateShoes>();
        equipment.Equip();
        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.1f, 0.9f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.up * 0.21f, new Vector3(0.75f, 0.5f, 0.6f));
    }
}
