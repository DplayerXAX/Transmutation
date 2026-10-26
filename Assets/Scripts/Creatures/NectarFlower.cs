using UnityEngine;

/// <summary>A renewable nectar source. A successful sip removes a real unit of nectar.</summary>
public sealed class NectarFlower : MonoBehaviour
{
    [Min(1f)] [SerializeField] private float capacity = 3f;
    [Min(0.1f)] [SerializeField] private float refillSeconds = 12f;
    private float nectar;
    public bool HasNectar => nectar >= 1f;
    public float Nectar => nectar;
    public Vector3 SippingPoint => transform.position + Vector3.up * 0.85f;
    private void Awake() => nectar = capacity;
    private void Update() => nectar = Mathf.Min(capacity, nectar + Time.deltaTime / refillSeconds);
    public float TakeNectar(float requested)
    {
        float amount = Mathf.Min(nectar, Mathf.Max(0f, requested));
        nectar -= amount;
        return amount;
    }
}
