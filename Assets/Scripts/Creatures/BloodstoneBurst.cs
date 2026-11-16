using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BloodstoneBurst : MonoBehaviour
{
    private const float SplashLifetime = 1.1f;
    private Bloodstone prefab;
    private Transform player;
    private float clearance;
    private long remaining;
    private float angle;
    private readonly Collider[] overlaps = new Collider[1];

    public static void Emit(PlayerCreatureStats owner, long count)
    {
        var root = new GameObject("Blood coat burst");
        SceneManager.MoveGameObjectToScene(root, owner.gameObject.scene);
        root.transform.position = owner.AimPosition;
        var burst = root.AddComponent<BloodstoneBurst>();
        burst.player = owner.CombatRoot;
        var capsule = burst.player.GetComponent<Collider>();
        burst.clearance = capsule != null ? Mathf.Max(capsule.bounds.extents.x, capsule.bounds.extents.z) + 0.24f : 0.85f;
        burst.remaining = count;
        burst.angle = Random.value * Mathf.PI * 2f;
        burst.PlaySplash();
        if (count == 0) { Destroy(root, SplashLifetime); return; }
        burst.prefab = Resources.Load<Bloodstone>("Bloodstone");
        if (burst.prefab == null) { Debug.LogError("Missing Bloodstone prefab.", owner); Destroy(root, SplashLifetime); return; }
    }

    private void PlaySplash()
    {
        var ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, SplashLifetime);
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
        main.startColor = new Color(0.92f, 0.025f, 0.035f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 7.5f);
        main.gravityModifier = 0.22f;
        var emission = ps.emission;
        emission.rateOverTime = 0;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.85f;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = Resources.Load<Material>("BloodDroplet");
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        ps.Play();
        ps.Emit(180);
    }

    private void FixedUpdate()
    {
        if (remaining == 0 || prefab == null) return;
        var physics = gameObject.scene.GetPhysicsScene();
        for (int i = 0; i < 8 && remaining > 0; i++)
        {
            bool spawned = false;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                angle += 2.399963f;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var offset = direction * clearance + Vector3.up * (0.08f + 0.18f * (attempt / 8));
                var position = transform.position + offset;
                if (physics.OverlapSphere(position, 0.19f, overlaps, ~0, QueryTriggerInteraction.Ignore) != 0 ||
                    physics.Raycast(transform.position, offset.normalized, out var hit, offset.magnitude,
                        ~0, QueryTriggerInteraction.Ignore) && (player == null || !hit.transform.IsChildOf(player))) continue;
                var stone = Instantiate(prefab, position, Random.rotation, transform);
                stone.transform.SetParent(null, true);
                stone.GetComponent<Rigidbody>().AddForce(direction * Random.Range(1.4f, 2.6f) + Vector3.up * 3.8f, ForceMode.VelocityChange);
                remaining--;
                spawned = true;
                break;
            }
            if (!spawned) break; // Try again after previously emitted stones have cleared the shell.
        }
        if (remaining == 0) Destroy(gameObject, SplashLifetime);
    }
}
