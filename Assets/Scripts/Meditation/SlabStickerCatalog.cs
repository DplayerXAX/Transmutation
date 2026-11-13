using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Reflection/Sticker Catalog")]
public sealed class SlabStickerCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string id;
        public string familyId;
        public string displayName;
        public Texture2D image;
    }

    [SerializeField] private List<Entry> stickers = new();
    public IReadOnlyList<Entry> Stickers => stickers;

    public Entry Find(string id) => stickers.Find(item => item != null && item.id == id);
}
