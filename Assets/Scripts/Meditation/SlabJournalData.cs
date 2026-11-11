using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
internal sealed class SlabJournalData
{
    internal const string FirstPageId = "amphitheater-journal-first";
    private const string LegacyInkId = "amphitheater-slab-v1";

    [Serializable]
    internal sealed class Page
    {
        public string id;
        public string text = "";
        public List<Sticker> stickers = new();
    }

    [Serializable]
    internal sealed class Sticker
    {
        public string id;
        public Vector2 position;
    }

    public int version = 1;
    public int viewedPage;
    public List<Page> pages = new();
    [NonSerialized] private string root;
    private string JournalPath => Path.Combine(root, "slab-journal-v1.json");

    internal static SlabJournalData Load(string saveRoot)
    {
        string path = Path.Combine(saveRoot, "slab-journal-v1.json");
        if (File.Exists(path))
        {
            try
            {
                var saved = JsonUtility.FromJson<SlabJournalData>(File.ReadAllText(path));
                if (saved != null && saved.version == 1 && saved.pages != null && saved.pages.Count > 0)
                {
                    var ids = new HashSet<string>();
                    foreach (var page in saved.pages)
                    {
                        if (page == null || string.IsNullOrEmpty(page.id) ||
                            page.id != Path.GetFileName(page.id) || !ids.Add(page.id))
                            throw new InvalidDataException("Invalid page ID.");
                        page.text ??= "";
                        page.stickers ??= new List<Sticker>();
                    }
                    saved.root = saveRoot;
                    saved.viewedPage = Mathf.Clamp(saved.viewedPage, 0, saved.pages.Count - 1);
                    return saved;
                }
                throw new InvalidDataException("Unknown or empty journal format.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not open the slab journal. The file is being preserved: " + exception.Message);
                return Create(saveRoot, true);
            }
        }

        var journal = Create(saveRoot, false);
        string legacy = journal.InkPath(LegacyInkId);
        string first = journal.InkPath(FirstPageId);
        try
        {
            if (File.Exists(legacy) && !File.Exists(first))
                WriteAtomic(first, File.ReadAllBytes(legacy));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not import the original slab drawing: " + exception.Message);
        }
        return journal;
    }

    [NonSerialized] private bool readOnly;
    internal bool ReadOnly => readOnly;

    private static SlabJournalData Create(string root, bool readOnly)
    {
        var journal = new SlabJournalData { root = root, readOnly = readOnly };
        journal.pages.Add(new Page { id = FirstPageId });
        return journal;
    }

    internal Page AddPage()
    {
        var page = new Page { id = "amphitheater-journal-" + Guid.NewGuid().ToString("N") };
        pages.Add(page);
        return page;
    }

    internal string InkPath(string pageId) => Path.Combine(root, "drawings", pageId + ".png");

    internal bool Save()
    {
        if (readOnly) return false;
        try
        {
            WriteAtomic(JournalPath, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(this, true)));
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not save the slab journal: " + exception.Message);
            return false;
        }
    }

    internal static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
}
