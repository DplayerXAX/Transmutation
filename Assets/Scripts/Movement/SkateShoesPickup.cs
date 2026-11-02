using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A non-creature equipment pickup. Spawn placement waits for real generated ground.</summary>
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

    public static void OfferNear(SmoothFirstPersonController player)
    {
        foreach (SkateShoesPickup existing in FindObjectsByType<SkateShoesPickup>(FindObjectsSortMode.None))
            if (existing.gameObject.scene == player.gameObject.scene) return;
        if (player.GetComponent<PlayerSkateShoes>() != null) return;
        var library = GameplayPrefabLibrary.Load();
        SkateShoesPickup pickup = library != null && library.skateShoes != null
            ? Instantiate(library.skateShoes) : new GameObject("Skate Shoes - starting equipment").AddComponent<SkateShoesPickup>();
        var root = pickup.gameObject;
        root.name = "Skate Shoes - starting equipment";
        SceneManager.MoveGameObjectToScene(root, player.gameObject.scene);
        root.transform.position = player.transform.position;
        pickup.StartCoroutine(pickup.PlaceNear(player));
    }

    private IEnumerator PlaceNear(SmoothFirstPersonController player)
    {
        var collider = GetComponent<BoxCollider>();
        collider.enabled = false;
        visuals.gameObject.SetActive(false);
        Vector3 spawn = player.Body != null ? player.Body.position : player.transform.position;
        Transform facing = player.Orientation != null ? player.Orientation : player.transform;
        Vector3 forward = facing.forward, right = facing.right;
        bool anchored = false;
        // Keep the offer near the starting area even if the player leaves while terrain is loading.
        for (int attempt = 0; player != null && attempt < 180; attempt++)
        {
            // ProceduralWorld may move the player to its generated spawn after this component starts.
            if (!anchored)
            {
                if (!player.Grounded)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }
                spawn = player.Body != null ? player.Body.position : player.transform.position;
                forward = facing.forward;
                right = facing.right;
                anchored = true;
            }
            for (int side = 0; side < 3; side++)
            {
                Vector3 point = spawn + forward * 2.2f + right * (side == 0 ? 1.6f : side == 1 ? -1.6f : 0f);
                if (!Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f,
                    player.GroundLayers, QueryTriggerInteraction.Ignore) ||
                    Vector3.Angle(hit.normal, Vector3.up) > 35f ||
                    hit.collider.GetComponentInParent<SmoothFirstPersonController>() != null) continue;
                transform.SetPositionAndRotation(hit.point + Vector3.up * 0.01f,
                    Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, hit.normal), hit.normal));
                visuals.gameObject.SetActive(true);
                collider.enabled = true;
                yield break;
            }
            yield return new WaitForSeconds(0.25f);
        }
        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.1f, 0.9f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.up * 0.21f, new Vector3(0.75f, 0.5f, 0.6f));
    }
}
