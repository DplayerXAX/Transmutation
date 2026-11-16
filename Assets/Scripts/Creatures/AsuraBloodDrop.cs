using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One combat-death reward shared by all four Asura variants.</summary>
[DisallowMultipleComponent]
public sealed class AsuraBloodDrop : MonoBehaviour
{
    [SerializeField] private AsuraBlood bloodPrefab;
    private Creature donor;
    private bool claimed;
    public AsuraBlood DroppedBlood { get; private set; }
    private void Awake() => donor = GetComponent<Creature>();
    private void OnEnable() { if (donor != null) donor.Died += OnDied; }
    private void OnDisable() { if (donor != null) donor.Died -= OnDied; }
    private void OnDied(Creature dead)
    {
        if (claimed || !Application.isPlaying || !gameObject.scene.isLoaded ||
            (dead.DeathCause != CreatureDeathCause.Damage && !(dead is RollingAsura roller && roller.Detonated)) ||
            (dead.Polarity != CreaturePolarity.Light && dead.Polarity != CreaturePolarity.Dark)) return;
        claimed = true;
        if (bloodPrefab == null) bloodPrefab = Resources.Load<AsuraBlood>("AsuraBlood");
        if (bloodPrefab == null) { Debug.LogError("Asura blood prefab is missing.", this); return; }
        Vector3 offset = Vector3.zero;
        if (dead is GeneralAsura)
        {
            float clearance = CreatureSpatialQuery.TryGetSolid(dead, out var solid) ? solid.bounds.extents.x + 0.65f : 1.8f;
            offset = Vector3.right * clearance;
        }
        DroppedBlood = Instantiate(bloodPrefab, dead.AimPosition + offset + Vector3.up * 0.6f, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(DroppedBlood.gameObject, gameObject.scene);
        DroppedBlood.SetDonor(dead.Polarity);
    }
}
