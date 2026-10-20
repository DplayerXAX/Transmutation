using UnityEngine;

public abstract class CreatureAttack : MonoBehaviour
{
    [Min(0.1f)] [SerializeField] private float range = 1.5f;
    [Min(0f)] [SerializeField] private float damage = 20f;
    [Min(0.05f)] [SerializeField] private float interval = 1f;
    [Min(0f)] [SerializeField] private float windup = 0.2f;
    public float Range => range;
    public float Damage => damage;
    public float Interval => Mathf.Max(interval, windup + 0.01f);
    public float Windup => windup;
    public abstract bool Release(Creature owner, Creature target, Transform muzzle);
}
