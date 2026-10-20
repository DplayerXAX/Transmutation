using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps self-spreading fire life from piling up over a long session. Fire flowers bloom fire bubbles,
/// and every bubble that lands becomes another fire flower; near a fire source this never stops,
/// so after some minutes there are dozens of flowers, each with its own update and terrain shapes.
/// Only creatures that appeared during play are trimmed (the oldest first); the ones placed in the scene,
/// and flowers keeping a bubble flower warm, are always kept.
/// </summary>
[DisallowMultipleComponent]
public sealed class RealmPopulation : MonoBehaviour
{
    [Tooltip("Most fire flowers that grew during play allowed at once.")]
    [Min(0)] [SerializeField] private int maxNewFireFlowers = 6;
    [Tooltip("Most fire bubbles that appeared during play allowed at once.")]
    [Min(0)] [SerializeField] private int maxNewFireBubbles = 14;
    [Tooltip("Fire flowers this close to a bubble flower are never trimmed (they keep it warm).")]
    [Min(0f)] [SerializeField] private float keepNearBubbleFlower = 4f;
    [Min(0.2f)] [SerializeField] private float checkInterval = 2f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private int newFireFlowers;
    [SerializeField] private int newFireBubbles;
    [SerializeField] private int trimmed;

    private readonly HashSet<Object> original = new HashSet<Object>();
    private readonly Dictionary<Object, float> firstSeen = new Dictionary<Object, float>();
    private float timer;

    private void Start()
    {
        foreach (FireFlower flower in FindObjectsByType<FireFlower>(FindObjectsSortMode.None)) original.Add(flower);
        foreach (FireBubble bubble in FindObjectsByType<FireBubble>(FindObjectsSortMode.None)) original.Add(bubble);
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = checkInterval;

        BubbleFlower[] bubbleFlowers = FindObjectsByType<BubbleFlower>(FindObjectsSortMode.None);
        newFireFlowers = Trim(FindObjectsByType<FireFlower>(FindObjectsSortMode.None), maxNewFireFlowers, flower =>
        {
            foreach (BubbleFlower warm in bubbleFlowers)
                if (warm != null && (warm.transform.position - flower.transform.position).sqrMagnitude < keepNearBubbleFlower * keepNearBubbleFlower) return true;
            return false;
        });
        newFireBubbles = Trim(FindObjectsByType<FireBubble>(FindObjectsSortMode.None), maxNewFireBubbles,
            bubble => bubble.IsCarried || bubble.IsBoundToBone);

        // Forget destroyed ones.
        var gone = new List<Object>();
        foreach (Object key in firstSeen.Keys) if (key == null) gone.Add(key);
        foreach (Object key in gone) firstSeen.Remove(key);
    }

    /// <summary>Destroys the oldest newcomers beyond the cap; returns how many newcomers remain.</summary>
    private int Trim<T>(T[] all, int cap, System.Func<T, bool> keep) where T : Creature
    {
        var newcomers = new List<T>();
        foreach (T creature in all)
        {
            if (creature == null || original.Contains(creature)) continue;
            if (!firstSeen.ContainsKey(creature)) firstSeen[creature] = Time.time;
            if (keep(creature)) continue;
            newcomers.Add(creature);
        }
        if (newcomers.Count <= cap) return newcomers.Count;
        newcomers.Sort((a, b) => firstSeen[a].CompareTo(firstSeen[b]));
        for (int i = 0; i < newcomers.Count - cap; i++)
        {
            Destroy(newcomers[i].gameObject);
            trimmed++;
        }
        return cap;
    }
}
