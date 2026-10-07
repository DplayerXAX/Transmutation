using UnityEngine;

/// <summary>
/// A seed or an inscription the player takes by touching it. It hovers and turns slowly,
/// is saved in LandmarkProgress, and does not come back once taken.
/// </summary>
[DisallowMultipleComponent]
public sealed class LandmarkPickup : MonoBehaviour
{
    public enum Kind { Seed, Inscription }

    public Kind kind;
    public string id;
    [Tooltip("Inscriptions only: which glyph is learned.")]
    public string glyphId;
    [Tooltip("Drop onto the ground below once the terrain there exists.")]
    public bool snapToGround;
    public float hoverHeight = 1.1f;

    private Vector3 home;
    private float taken = -1f;
    private float phase;

    private void Start()
    {
        if (kind == Kind.Seed ? LandmarkProgress.HasSeed(id) : LandmarkProgress.HasGlyph(glyphId))
        {
            gameObject.SetActive(false);
            return;
        }
        home = transform.position;
        phase = Random.value * 10f;
        var trigger = gameObject.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 1.3f;
        gameObject.layer = 2; // Ignore Raycast: climbing and pickup rays pass through.
    }

    private void Update()
    {
        if (snapToGround)
        {
            // Chunks far from spawn are built later; wait for the ground to appear.
            if (Physics.Raycast(home + Vector3.up, Vector3.down, out RaycastHit hit, 120f, 1 << 7, QueryTriggerInteraction.Ignore))
            {
                home = hit.point + Vector3.up * hoverHeight;
                snapToGround = false;
            }
        }

        if (taken >= 0f)
        {
            taken += Time.deltaTime;
            float t = taken / 0.8f;
            transform.position += Vector3.up * Time.deltaTime * 2.5f;
            transform.localScale = Vector3.one * Mathf.Max(0f, 1f - t * t);
            if (t >= 1f) Destroy(gameObject);
            return;
        }

        float time = Time.time + phase;
        transform.position = home + Vector3.up * (Mathf.Sin(time * 1.3f) * 0.12f);
        transform.rotation = Quaternion.Euler(Mathf.Sin(time * 0.7f) * 8f, time * 25f, 0f);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (taken >= 0f || other.attachedRigidbody == null) return;
        if (other.attachedRigidbody.GetComponent<SmoothFirstPersonController>() == null) return;

        taken = 0f;
        if (kind == Kind.Seed)
        {
            LandmarkProgress.AddSeed(id);
            LandmarkToast.Show(LandmarkProgress.SeedCount == 1 ? "A seed." : "A seed.   (" + LandmarkProgress.SeedCount + ")");
        }
        else
        {
            LandmarkProgress.AddGlyph(glyphId);
            LandmarkToast.Show("The sign settles somewhere in your mind.   (meditate: M)");
        }
    }
}
