using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns squirrel population, randomized sizes, capacities and home assignments.</summary>
[DisallowMultipleComponent]
public sealed class SquirrelManager : MonoBehaviour
{
    [SerializeField] private FoodManager foodManager;
    [SerializeField] private List<Squirrel> squirrels = new List<Squirrel>();
    [SerializeField] private List<SquirrelNest> nests = new List<SquirrelNest>();
    [SerializeField] private List<SquirrelTree> trees = new List<SquirrelTree>();
    [SerializeField] private Vector2 sizeRange = new Vector2(0.7f, 1.4f);
    [SerializeField, Min(1)] private int baseCapacity = 3;
    [Tooltip("Minimum population. Clones the first squirrel when fewer exist.")]
    [SerializeField, Min(1)] private int population = 3;
    [SerializeField, Min(0f)] private float spawnRadius = 2f;
    [Header("Homes and fixed territories")]
    [SerializeField, Min(1f)] private float territoryRadius = 12f;
    [SerializeField, Min(0.5f)] private float nestTreeDistance = 1.8f;
    [Header("Food fights")]
    [SerializeField] private bool enableFights = true;
    [SerializeField, Min(0.1f)] private float fightDistance = 2.5f;
    [SerializeField, Min(0.1f)] private float fightDuration = 1.4f;
    [SerializeField, Min(1f)] private float fightCooldown = 6f;
    [Tooltip("How strongly body size affects winning. Zero gives equal odds.")]
    [SerializeField, Min(0f)] private float sizeAdvantage = 2f;
    private sealed class Fight
    {
        public Squirrel first, second;
        public Vector3 firstOrigin, secondOrigin;
        public float elapsed;
    }
    private readonly List<Fight> fights = new List<Fight>();
    public IReadOnlyList<Squirrel> Squirrels => squirrels;
    public IReadOnlyList<SquirrelNest> Nests => nests;

    public void Register(Squirrel squirrel)
    {
        if (squirrel != null && !squirrels.Contains(squirrel)) squirrels.Add(squirrel);
    }

    public void Register(SquirrelNest nest)
    {
        if (nest != null && !nests.Contains(nest)) nests.Add(nest);
    }

    public void Register(SquirrelTree tree)
    {
        if (tree != null && !trees.Contains(tree)) trees.Add(tree);
    }

    public void SetFoodManager(FoodManager manager) => foodManager = manager;

    private void LateUpdate()
    {
        for (int i = fights.Count - 1; i >= 0; i--)
        {
            var fight = fights[i];
            if (fight.first == null || fight.second == null || !fight.first.CanMoveAutonomously ||
                !fight.second.CanMoveAutonomously || !enableFights)
            {
                FinishFight(fight, false);
                fights.RemoveAt(i);
                continue;
            }
            fight.elapsed += Time.deltaTime;
            if (fight.elapsed >= fightDuration)
            {
                FinishFight(fight, true);
                fights.RemoveAt(i);
                continue;
            }
            fight.first.PoseFight(fight.secondOrigin, fight.elapsed);
            fight.second.PoseFight(fight.firstOrigin, fight.elapsed);
        }
        if (!enableFights) return;
        for (int i = 0; i < squirrels.Count; i++)
        {
            var first = squirrels[i];
            if (first == null || !first.CanFight) continue;
            for (int j = i + 1; j < squirrels.Count; j++)
            {
                var second = squirrels[j];
                if (second == null || !second.CanFight ||
                    (!first.WantsToChallenge(second) && !second.WantsToChallenge(first))) continue;
                Vector3 separation = first.transform.position - second.transform.position;
                separation.y = 0f;
                if (separation.sqrMagnitude > fightDistance * fightDistance) continue;
                fights.Add(new Fight { first = first, second = second,
                    firstOrigin = first.transform.position, secondOrigin = second.transform.position });
                first.BeginFight();
                second.BeginFight();
                break;
            }
        }
    }

    private void FinishFight(Fight fight, bool resolved)
    {
        Squirrel winner = fight.first, loser = fight.second;
        if (resolved)
        {
            float a = Mathf.Pow(Mathf.Max(0.1f, winner.transform.lossyScale.x), sizeAdvantage);
            float b = Mathf.Pow(Mathf.Max(0.1f, loser.transform.lossyScale.x), sizeAdvantage);
            if (Random.value >= a / (a + b)) { winner = fight.second; loser = fight.first; }
        }
        // The loser hands off food first, so the winner decides its next state with its new load.
        if (loser != null) loser.EndFight(winner, loser, fightCooldown, resolved);
        if (winner != null) winner.EndFight(winner, loser, fightCooldown, resolved);
    }

    private void OnDisable()
    {
        foreach (var fight in fights) FinishFight(fight, false);
        fights.Clear();
    }

    private void Start()
    {
        squirrels.RemoveAll(squirrel => squirrel == null);
        nests.RemoveAll(nest => nest == null);
        trees.RemoveAll(tree => tree == null);
        if (foodManager == null || nests.Count == 0 || squirrels.Count == 0 || trees.Count == 0)
        {
            Debug.LogWarning("SquirrelManager needs a FoodManager, a Squirrel, a Nest and tree anchors in this scene.", this);
            return;
        }
        // Clone before randomizing so every squirrel starts from the authored model scale.
        Squirrel template = squirrels[0];
        while (squirrels.Count < population)
        {
            Vector2 offset = Random.insideUnitCircle * spawnRadius;
            var squirrel = Instantiate(template, template.transform.position + new Vector3(offset.x, 0f, offset.y),
                template.transform.rotation, template.transform.parent);
            squirrel.name = template.name + " " + (squirrels.Count + 1);
            squirrels.Add(squirrel);
        }
        float minimum = Mathf.Max(0.1f, Mathf.Min(sizeRange.x, sizeRange.y));
        float maximum = Mathf.Max(minimum, Mathf.Max(sizeRange.x, sizeRange.y));
        // Each squirrel receives a distinct nest. Prefer different trees before sharing an anchor.
        SquirrelNest nestTemplate = nests[0];
        Vector3 originalHome = nestTemplate.transform.position;
        trees.Sort((a, b) => (a.GroundPosition - originalHome).sqrMagnitude.CompareTo(
            (b.GroundPosition - originalHome).sqrMagnitude));
        while (nests.Count < squirrels.Count)
        {
            var home = Instantiate(nestTemplate, nestTemplate.transform.position,
                nestTemplate.transform.rotation, nestTemplate.transform.parent);
            nests.Add(home);
        }
        for (int i = 0; i < squirrels.Count; i++)
        {
            var squirrel = squirrels[i];
            var home = nests[i];
            var tree = trees[i % trees.Count];
            int sharedIndex = i / trees.Count;
            float angle = sharedIndex * 2.399963f;
            Vector3 ground = tree.GroundPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * nestTreeDistance;
            // Place on the side facing the clearing, away from the floor's outer edges.
            if (sharedIndex == 0)
            {
                Vector3 inward = originalHome - tree.GroundPosition;
                inward.y = 0f;
                if (inward.sqrMagnitude > 0.001f) ground = tree.GroundPosition + inward.normalized * nestTreeDistance;
            }
            SquirrelTree.PlaceOnGround(home.transform, ground);
            home.name = squirrel.name + " Nest";
            home.AssignOwner(squirrel);
            squirrel.Initialize(foodManager, home, Random.Range(minimum, maximum), baseCapacity, territoryRadius);
            // Body height is adjusted after random scaling so all sizes stand on the same floor.
            SquirrelTree.PlaceOnGround(squirrel.transform, ground + Vector3.forward * Mathf.Min(spawnRadius, territoryRadius * 0.5f));
        }
        // Explicit initialization order: relocate both existing food and new food only after trees are known.
        foodManager.GenerateAroundTrees(trees);
    }
}
