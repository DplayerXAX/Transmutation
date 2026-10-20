using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One scene-local listener presents shared explosion events, including player shots.</summary>
public sealed class AsuraPrototypeEffects : MonoBehaviour
{
    [SerializeField] private Material explosionMaterial;
    private void OnEnable() => ExplosionResolver.Exploded += Show;
    private void OnDisable() => ExplosionResolver.Exploded -= Show;
    private void Show(Vector3 center, float radius, Scene scene)
    {
        if (scene != gameObject.scene) return;
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Blast Flash";
        SceneManager.MoveGameObjectToScene(sphere, scene);
        sphere.transform.position = center;
        sphere.GetComponent<Collider>().enabled = false;
        Destroy(sphere.GetComponent<Collider>());
        if (explosionMaterial != null) sphere.GetComponent<Renderer>().sharedMaterial = explosionMaterial;
        sphere.AddComponent<AsuraBlastFlash>().Radius = radius;
    }
}
