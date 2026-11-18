using UnityEngine;
public sealed class AsuraTerritoryResidue : MonoBehaviour
{
    private float remaining=120;
    private void Update(){remaining-=Time.deltaTime;if(remaining<=0)Destroy(gameObject);}
}
