using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Code-built sneakers with bright soles and compact heel/toe wheels; no physics colliders.</summary>
public sealed class SkateShoesVisuals : MonoBehaviour
{
    private readonly Transform[] shoes = new Transform[2];
    private Material upper, sole, rubber, accent;

    private void Awake()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        upper = MakeMaterial(shader, "Skate shoe teal", new Color(0.04f, 0.48f, 0.5f));
        sole = MakeMaterial(shader, "Skate shoe sole", new Color(0.92f, 0.95f, 0.85f));
        rubber = MakeMaterial(shader, "Skate shoe wheels", new Color(0.055f, 0.07f, 0.085f));
        accent = MakeMaterial(shader, "Skate shoe orange", new Color(1f, 0.4f, 0.12f));
        for (int i = 0; i < 2; i++)
        {
            shoes[i] = new GameObject(i == 0 ? "Left skate shoe" : "Right skate shoe").transform;
            shoes[i].SetParent(transform, false);
            shoes[i].localPosition = Vector3.right * (i == 0 ? -0.2f : 0.2f);
            Part(shoes[i], "Sole", new Vector3(0f, -0.025f, 0.07f), new Vector3(0.21f, 0.065f, 0.41f), sole);
            Part(shoes[i], "Upper", new Vector3(0f, 0.055f, 0.065f), new Vector3(0.18f, 0.105f, 0.33f), upper);
            Part(shoes[i], "Ankle", new Vector3(0f, 0.11f, -0.05f), new Vector3(0.16f, 0.15f, 0.14f), upper);
            Part(shoes[i], "Heel accent", new Vector3(0f, 0.065f, -0.105f), new Vector3(0.19f, 0.045f, 0.018f), accent);
            for (int lace = 0; lace < 4; lace++)
                Part(shoes[i], "Lace", new Vector3(0f, 0.115f, 0.015f + lace * 0.037f),
                    new Vector3(0.12f, 0.012f, 0.012f), sole);
            for (int wheel = 0; wheel < 4; wheel++)
            {
                Transform axle = Part(shoes[i], "Wheel", new Vector3(wheel % 2 == 0 ? -0.1f : 0.1f,
                    -0.075f, wheel < 2 ? -0.065f : 0.2f), new Vector3(0.09f, 0.025f, 0.09f), rubber, PrimitiveType.Cylinder);
                axle.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }
    }

    private static Material MakeMaterial(Shader shader, string label, Color color)
    {
        var material = new Material(shader) { name = label, enableInstancing = true };
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        return material;
    }

    private static Transform Part(Transform parent, string label, Vector3 position, Vector3 scale,
        Material material, PrimitiveType type = PrimitiveType.Cube)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = label;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        Collider collider = part.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        var renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return part.transform;
    }

    public void SetFootPose(int index, Vector3 position, Quaternion rotation)
    {
        if (shoes[index] != null) shoes[index].SetPositionAndRotation(position, rotation);
    }

    private void OnDestroy()
    {
        if (upper != null) Destroy(upper);
        if (sole != null) Destroy(sole);
        if (rubber != null) Destroy(rubber);
        if (accent != null) Destroy(accent);
    }
}
