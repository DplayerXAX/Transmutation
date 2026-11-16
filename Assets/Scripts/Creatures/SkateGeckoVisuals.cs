using UnityEngine;

/// <summary>Broad, low back for two feet; splayed adhesive toes, bulging eyes and a tapered tail.</summary>
public sealed class SkateGeckoVisuals : MonoBehaviour
{
    [SerializeField] Transform[] legs = new Transform[4];
    public static void Build(Transform parent)
    {
        var root = new GameObject("Gecko model"); root.transform.SetParent(parent, false);
        root.AddComponent<SkateGeckoVisuals>().BuildModel();
    }
    public void BuildModel()
    {
        if (transform.childCount > 0) return;
        var skin = Material(new Color(.19f, .51f, .38f)); var belly = Material(new Color(.55f, .69f, .35f));
        var eye = Material(new Color(.95f, .68f, .16f)); var pupil = Material(new Color(.025f, .035f, .025f));
        Part("Broad back", new Vector3(0, .02f, 0), new Vector3(.78f, .28f, 1.65f), skin);
        Part("Head", new Vector3(0, .03f, 1.05f), new Vector3(.72f, .29f, .65f), skin);
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1 : 1;
            Part("Eye", new Vector3(side * .28f, .16f, 1.17f), new Vector3(.21f, .2f, .21f), eye);
            Part("Vertical pupil", new Vector3(side * .30f, .19f, 1.25f), new Vector3(.045f, .12f, .035f), pupil);
        }
        for (int i = 0; i < 7; i++)
            Part("Tapered tail", new Vector3(Mathf.Sin(i * .5f) * .1f, -.02f, -.85f - i * .19f), new Vector3(.34f - i * .043f, .19f - i * .022f, .38f), skin);
        for (int i = 0; i < 4; i++)
        {
            float side = i % 2 == 0 ? -1 : 1, z = i < 2 ? .56f : -.52f;
            var limb = new GameObject("Splayed leg").transform; limb.SetParent(transform, false); legs[i] = limb;
            Part("Leg", new Vector3(side * .46f, -.04f, z), new Vector3(.58f, .13f, .22f), skin, limb);
            for (int toe = 0; toe < 4; toe++)
            {
                var p = new Vector3(side * (.69f + toe * .035f), -.1f, z + (toe - 1.5f) * .09f);
                Part("Toe", p, new Vector3(.21f, .065f, .045f), skin, limb);
                Part("Adhesive toe pad", p + Vector3.right * side * .1f, new Vector3(.095f, .04f, .08f), belly, limb);
            }
        }
        for (int i = 0; i < 16; i++) Part("Back spot", new Vector3(Mathf.Sin(i * 2.4f) * .23f, .15f, -.65f + i * .09f), new Vector3(.09f, .022f, .12f), belly);
    }
    static Material Material(Color color) { var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", color); m.enableInstancing = true; return m; }
    Transform Part(string label, Vector3 at, Vector3 scale, Material material, Transform parent = null)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Sphere); p.name = label; p.transform.SetParent(parent != null ? parent : transform, false);
        p.transform.localPosition = at; p.transform.localScale = scale;
        var collider = p.GetComponent<Collider>(); collider.enabled = false;
        if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
        p.GetComponent<Renderer>().sharedMaterial = material; return p.transform;
    }
    void Update()
    {
        var body = GetComponentInParent<Rigidbody>(); var gecko = GetComponentInParent<SkateGecko>();
        float speed = body != null && !body.isKinematic ? body.linearVelocity.magnitude : 0;
        for (int i = 0; i < legs.Length; i++) if (legs[i] != null)
            legs[i].localRotation = Quaternion.Euler(0, gecko != null && !gecko.Ridden ? Mathf.Sin(Time.time * Mathf.Min(24, speed * 2) + (i == 0 || i == 3 ? 0 : Mathf.PI)) * Mathf.Min(22, speed * 4) : 0, 0);
    }
}
