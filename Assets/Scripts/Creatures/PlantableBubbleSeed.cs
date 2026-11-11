using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A squirrel food seed may also be planted by its player carrier. Failed placement keeps the seed.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(SquirrelFood))]
public sealed class PlantableBubbleSeed : MonoBehaviour
{
    [SerializeField] private BubbleFlower flowerPrefab;
    [SerializeField, Min(1f)] private float plantingReach = 6f;
    [SerializeField, Min(0.2f)] private float spacing = 2.4f;
    public BubbleFlower FlowerPrefab => flowerPrefab;

    public bool TryPlant(PlayerCreatureCarrier carrier, Ray ray)
    {
        var food = GetComponent<SquirrelFood>();
        if (flowerPrefab == null || carrier == null || carrier.CarriedCreature != food || food.IsDead) return false;
        var physics = gameObject.scene.GetPhysicsScene();
        // Ray against all solid scenery first: walls and creatures cannot be planted through.
        RaycastHit[] hits = new RaycastHit[32];
        int count;
        while ((count = physics.Raycast(ray.origin, ray.direction, hits, plantingReach, ~0, QueryTriggerInteraction.Ignore)) == hits.Length)
            hits = new RaycastHit[hits.Length * 2];
        RaycastHit ground = default; float nearest = float.PositiveInfinity;
        for (int i=0;i<count;i++)
        {
            var hit=hits[i];
            if(hit.collider.GetComponentInParent<PlayerCreatureCarrier>() == carrier || hit.collider.GetComponentInParent<SquirrelFood>() == food || hit.distance>=nearest)continue;
            nearest=hit.distance; ground=hit;
        }
        if(ground.collider==null || ground.collider.gameObject.layer!=7 || ground.normal.y<0.8f) return false;
        var ecology = FindFirstObjectByType<LeafMapEcology>();
        if(ecology != null && (!ecology.CanPlant(ground.point) || !ecology.HasFlowerCapacity))return false;
        foreach(var flower in FindObjectsByType<BubbleFlower>(FindObjectsSortMode.None))
            if(flower.gameObject.scene==gameObject.scene && Vector3.Distance(flower.transform.position,ground.point)<spacing)return false;
        var blockers = new Collider[8];
        if(physics.OverlapSphere(ground.point + Vector3.up * 0.7f,0.38f,blockers,~(1<<7),QueryTriggerInteraction.Ignore)>0)return false;
        var planted=Instantiate(flowerPrefab,ground.point,Quaternion.Euler(0,Random.Range(0f,360f),0));
        SceneManager.MoveGameObjectToScene(planted.gameObject,gameObject.scene);
        planted.name="Bubble flower grown from seed";
        SquirrelTree.PlaceOnGround(planted.transform,ground.point+Vector3.up*0.03f);
        ecology?.RegisterFlower(planted);
        carrier.Release();
        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }
}
