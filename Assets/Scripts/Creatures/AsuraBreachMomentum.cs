using UnityEngine;

/// <summary>Refreshable, non-stacking victory effect; sampled by the existing attack and movement logic.</summary>
public sealed class AsuraBreachMomentum : MonoBehaviour
{
    [SerializeField] private float expiresAt;
    public bool Active=>Time.time<expiresAt;
    public void Refresh()=>expiresAt=Time.time+12;
    public static bool Has(Creature creature)=>creature!=null && creature.TryGetComponent<AsuraBreachMomentum>(out var buff) && buff.Active;
    public static float Damage(Creature c)=>Has(c)?1.15f:1;
    public static float Speed(Creature c)=>Has(c)?1.15f:1;
    public static float AttackSpeed(Creature c)=>Has(c)?1.25f:1;
    public static float Detection(Creature c)=>Has(c)?1.5f:1;
}
