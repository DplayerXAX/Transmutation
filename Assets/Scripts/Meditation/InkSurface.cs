using UnityEngine;

/// <summary>
/// A drawable face on a stele or tablet. Ink lives in a single-channel texture that is
/// painted on the CPU and saved as a PNG, so drawings stay between play sessions.
/// Needs a MeshCollider so ray hits return texture coordinates.
/// </summary>
[RequireComponent(typeof(MeshCollider))]
public sealed class InkSurface : MonoBehaviour
{
    [SerializeField] private string surfaceId = "surface";
    [Min(64)] [SerializeField] private int pixelsPerMetre = 420;

    private Texture2D texture;
    private Renderer surfaceRenderer;
    private MaterialPropertyBlock block;
    private bool dirty;
    private bool unsaved;
    private int width, height;

    public string SurfaceId => surfaceId;
    public MeshCollider Collider { get; private set; }
    /// <summary>Size of the face in metres (width, height).</summary>
    public Vector2 Size { get; private set; }

    public void Setup(string id, Vector2 sizeInMetres)
    {
        surfaceId = id;
        Size = sizeInMetres;
        Collider = GetComponent<MeshCollider>();
        surfaceRenderer = GetComponent<Renderer>();

        width = Mathf.Clamp(Mathf.RoundToInt(sizeInMetres.x * pixelsPerMetre), 64, 1024);
        height = Mathf.Clamp(Mathf.RoundToInt(sizeInMetres.y * pixelsPerMetre), 64, 1024);
        texture = new Texture2D(width, height, TextureFormat.R8, false)
        {
            name = "Ink " + id,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        texture.SetPixelData(new byte[width * height], 0);
        texture.Apply(false);
        Load();

        block = new MaterialPropertyBlock();
        surfaceRenderer.GetPropertyBlock(block);
        block.SetTexture("_InkTex", texture);
        block.SetFloat("_DrawingAmount", 1f);
        surfaceRenderer.SetPropertyBlock(block);
    }

    /// <summary>Paints a soft round line between two uv points.</summary>
    public void Stroke(Vector2 fromUv, Vector2 toUv, float radiusPixels, bool erase)
    {
        if (texture == null) return;
        var data = texture.GetPixelData<byte>(0);
        Vector2 a = new Vector2(fromUv.x * width, fromUv.y * height);
        Vector2 b = new Vector2(toUv.x * width, toUv.y * height);
        float distance = Vector2.Distance(a, b);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(0.5f, radiusPixels * 0.3f)));
        for (int s = 0; s <= steps; s++)
            Stamp(data, Vector2.Lerp(a, b, (float)s / steps), radiusPixels, erase);
        dirty = true;
        unsaved = true;
    }

    private void Stamp(Unity.Collections.NativeArray<byte> data, Vector2 centre, float radius, bool erase)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(centre.x - radius - 1f));
        int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(centre.x + radius + 1f));
        int minY = Mathf.Max(0, Mathf.FloorToInt(centre.y - radius - 1f));
        int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(centre.y + radius + 1f));
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);
            float strength = Mathf.Clamp01(radius - d + 0.5f);
            if (strength <= 0f) continue;
            int i = y * width + x;
            if (erase)
                data[i] = (byte)Mathf.Min(data[i], Mathf.RoundToInt((1f - strength) * 255f));
            else
                data[i] = (byte)Mathf.Max(data[i], Mathf.RoundToInt(strength * 255f));
        }
    }

    private void LateUpdate()
    {
        if (!dirty) return;
        dirty = false;
        texture.Apply(false);
    }

    public void Save()
    {
        if (!unsaved || texture == null) return;
        unsaved = false;
        ReflectionStorage.SaveBytes(ReflectionStorage.DrawingPath(surfaceId), texture.EncodeToPNG());
    }

    private void Load()
    {
        byte[] bytes = ReflectionStorage.LoadBytes(ReflectionStorage.DrawingPath(surfaceId));
        if (bytes == null) return;
        var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (loaded.LoadImage(bytes) && loaded.width == width && loaded.height == height)
        {
            Color32[] pixels = loaded.GetPixels32();
            var data = texture.GetPixelData<byte>(0);
            for (int i = 0; i < pixels.Length && i < data.Length; i++) data[i] = pixels[i].r;
            texture.Apply(false);
        }
        Destroy(loaded);
    }

    private void OnDestroy()
    {
        Save();
        if (texture != null) Destroy(texture);
    }
}
