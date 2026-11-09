using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Contact-driven after-death behavior, independent of Creature's stopped simulation.</summary>
[DisallowMultipleComponent]
public sealed class RecycleGermination : MonoBehaviour
{
    RecycleFlower flower;
    void Awake() => flower = GetComponent<RecycleFlower>();
    void OnCollisionEnter(Collision collision) => Contact(collision);
    void OnCollisionStay(Collision collision) => Contact(collision);
    void Contact(Collision collision)
    {
        if (flower == null || !flower.IsDead || flower.SeedReserved) return;
        for (int i = 0; i < collision.contactCount; i++)
        {
            var contact = collision.GetContact(i);
            if (contact.normal.y >= 0.35f && SurfaceGeometry.IsTerrain(collision.collider) &&
                SurfaceGeometry.Acquire(collision.collider, contact.point, contact.normal, out var anchor))
            { TryGerminate(anchor); return; }
        }
    }
    public void CheckRestingContact()
    {
        if (flower == null) flower = GetComponent<RecycleFlower>();
        if (flower == null || !TryGetComponent<Collider>(out var collider)) return;
        Bounds bounds = collider.bounds;
        Vector3 foot = new Vector3(bounds.center.x, bounds.min.y + 0.04f, bounds.center.z);
        if (SurfaceGeometry.Probe(gameObject.scene, foot, Vector3.down, 0.10f, true, out var anchor)) TryGerminate(anchor);
    }
    internal bool TryGerminate(SurfaceAnchor anchor)
    {
        if (flower == null) flower = GetComponent<RecycleFlower>();
        if (!Application.isPlaying || flower == null || !flower.IsDead || flower.IsConsumptionPending || flower.SeedReserved ||
            flower.IsCarried || flower.IsBubbleCaptured || flower.SeedPrefab == null ||
            (flower.TryGetComponent<CompostAbsorption>(out var claim) && claim.HasClaim) ||
            !SurfaceGeometry.IsTerrain(anchor.Support) || anchor.Support.gameObject.scene != gameObject.scene ||
            !anchor.Resolve(out Vector3 p, out Vector3 n) || n.y < 0.35f) return false;
        var world = anchor.Support.GetComponent<ProceduralWorldChunk>().Owner;
        if (world == null || !world.IsRegionReady(p, 0.5f)) return false;
        flower.SeedReserved = true;
        var staging = new GameObject("Recycle germination staging"); staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, gameObject.scene);
        CompostCreature colony = null;
        try
        {
            colony = Instantiate(flower.SeedPrefab, p, Quaternion.identity, staging.transform);
            colony.InitializeSeed(anchor);
            colony.transform.SetParent(null, true);
            flower.gameObject.SetActive(false); Destroy(flower.gameObject);
            return true;
        }
        catch
        { if (colony != null) Destroy(colony.gameObject); flower.SeedReserved = false; throw; }
        finally { Destroy(staging); }
    }
}
