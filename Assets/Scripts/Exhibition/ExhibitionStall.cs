using UnityEngine;

/// <summary>
/// One stall in the exhibition. Holds an inactive template creature as a child, spawns a live copy on
/// the stall at start and grows a new one when the copy is lost (eaten, used up, carried far away or fallen off).
/// </summary>
public class ExhibitionStall : MonoBehaviour
{
    [Tooltip("Inactive child copied to make the live creature.")]
    public GameObject template;
    [Tooltip("Seconds before a lost creature grows back.")]
    public float respawnDelay = 10f;
    [Tooltip("A creature further than this from the stall counts as lost.")]
    public float lostDistance = 40f;
    public Vector3 spawnOffset = new Vector3(0f, 0.5f, 0f);

    private GameObject current;
    private float lostTimer;
    private float nextCheck;

    private void Start()
    {
        if (template != null) template.SetActive(false);
        Spawn();
    }

    private void Update()
    {
        if (template == null || Time.time < nextCheck) return;
        nextCheck = Time.time + 1f;

        bool alive = current != null && current.activeInHierarchy;
        if (alive && current.TryGetComponent(out Creature creature)) alive = !creature.IsDead;
        if (alive && (current.transform.position - transform.position).sqrMagnitude > lostDistance * lostDistance)
            alive = false;
        if (alive)
        {
            lostTimer = 0f;
            return;
        }
        lostTimer += 1f;
        if (lostTimer >= respawnDelay) Spawn();
    }

    private void Spawn()
    {
        if (template == null) return;
        current = Instantiate(template, transform.position + spawnOffset, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        current.name = template.name;
        current.SetActive(true);
        // Held still until the floor under it is found.
        if (current.GetComponent<Rigidbody>() != null) current.AddComponent<SettleWhenGroundReady>();
        lostTimer = 0f;
    }
}
