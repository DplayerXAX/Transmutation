using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Play-mode checks for Fungal Hollow and its Mushroom Trees:
///  1. fixed caps of each launch speed, feet rise against the 0.95 v²/2g the reachability pass assumes;
///  2. a young tree roots where it lands, can be carried until grown and not after, and its cap bounces once grown;
///  3. essence on a grown cap condenses into a BounceShroom that drops to the ground;
///  4. eaten essence raises the jump, and the boost ends on time;
///  5. loose essence resting together condenses by itself.
/// The player's movement scripts are switched off for 1 so nothing but the cap moves the body.
/// </summary>
public sealed class FungalHollowTestDriver : MonoBehaviour
{
    public static string Report = "running";
    private readonly StringBuilder log = new StringBuilder();
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private void Check(bool ok, string what) => log.AppendLine((ok ? "PASS " : "FAIL ") + what);

    private IEnumerator Start()
    {
        Report = "running";
        var player = GameObject.Find("PlayerObjects").GetComponentInChildren<SmoothFirstPersonController>();
        var playerBody = player.GetComponent<Rigidbody>();
        var carrier = player.transform.root.GetComponentInChildren<PlayerCreatureCarrier>();
        var movers = player.GetComponentsInChildren<MonoBehaviour>();
        foreach (var behaviour in movers)
            if (behaviour is SmoothFirstPersonController || behaviour is HandClimber || behaviour is AdvancedFirstPersonTraversal)
                behaviour.enabled = false;
        yield return new WaitForSeconds(1f);
        float stand = playerBody.position.y - 9f; // the arrival ledge top is at 9 m
        var nursery = new System.Collections.Generic.Dictionary<BounceShroom, Vector3>();
        foreach (var s in FindObjectsByType<BounceShroom>(FindObjectsSortMode.None)) nursery[s] = s.transform.position;

        // 1. Fixed caps (trees and static pads alike).
        foreach (string name in new[] { "Spiral Bounce Cap 1", "East Sentinel", "Crown Puffball — big launch", "Bog Step Cap 1" })
        {
            var spring = GameObject.Find(name).GetComponentInChildren<BouncySurface>();
            var platform = spring.GetComponentInChildren<BoxCollider>();
            float top = platform.bounds.max.y;
            playerBody.linearVelocity = Vector3.zero;
            playerBody.position = new Vector3(platform.bounds.center.x, top + stand + 0.6f, platform.bounds.center.z);
            float peak = -999f;
            for (float t = 0f; t < 2.5f; t += Time.fixedDeltaTime)
            {
                yield return new WaitForFixedUpdate();
                peak = Mathf.Max(peak, playerBody.position.y);
                playerBody.linearVelocity = new Vector3(0f, playerBody.linearVelocity.y, 0f);
            }
            float rise = peak - stand - top, expected = 0.95f * spring.LaunchSpeed * spring.LaunchSpeed / 60f;
            Check(rise >= expected - 0.05f, $"{name}: launch {spring.LaunchSpeed}, feet rose {rise:0.00} m (pass assumes {expected:0.00})");
        }
        playerBody.linearVelocity = Vector3.zero;
        playerBody.position = new Vector3(0f, 9f + stand, -59.5f);
        foreach (var behaviour in movers) if (behaviour is SmoothFirstPersonController) behaviour.enabled = true;

        // 2. A young tree dropped on the floor: roots, grows, stops being carryable, bounces.
        var prefab = GameObject.Find("Young Mushroom Tree 1").GetComponent<MushroomTree>();
        var young = Instantiate(prefab, new Vector3(-8f, FungalHollowFloor(-8f, -34f) + 1.5f, -34f), Quaternion.Euler(0f, 0f, 20f));
        young.name = "Test Young Tree";
        typeof(MushroomTree).GetField("rooted", Private).SetValue(young, false);
        young.GetComponent<Rigidbody>().isKinematic = false;
        young.GetComponent<Rigidbody>().useGravity = true;
        Check(young.CanBeCarried, "young tree can be carried");
        float waited = 0f;
        while (!young.Rooted && waited < 6f) { waited += Time.deltaTime; yield return null; }
        Check(young.Rooted && Vector3.Angle(young.transform.up, Vector3.up) < 1f, $"young tree rooted upright after {waited:0.0} s");
        typeof(MushroomTree).GetField("growthDuration", Private).SetValue(young, 3f);
        waited = 0f;
        while (!young.IsGrown && waited < 6f) { waited += Time.deltaTime; yield return null; }
        Check(young.IsGrown && !young.CanBeCarried, $"grown after {waited:0.0} s and no longer carryable (cap top {young.CapTop - young.transform.position.y:0.0} m)");
        Check(young.GetComponent<BouncySurface>().IsBouncy, "grown green cap bounces");

        // 3. Essence on a grown cap condenses into a BounceShroom: it stays on a solid cap, drops off a bouncy one.
        var tree = GameObject.Find("West Blue Cap 1").GetComponent<MushroomTree>();
        int shroomsBefore = FindObjectsByType<BounceShroom>(FindObjectsSortMode.None).Length;
        MushroomEssence first = tree.SpawnEssence();
        Check(first != null && first.OnStalk && first.GetComponent<Rigidbody>().isKinematic && first.transform.position.y > tree.CapTop,
            "essence forms on the cap top and stays put");
        tree.SpawnEssence(); tree.SpawnEssence();
        yield return new WaitForSeconds(0.2f);
        int shroomsAfter = FindObjectsByType<BounceShroom>(FindObjectsSortMode.None).Length;
        Check(tree.EssenceOnCap == 0 && shroomsAfter == shroomsBefore + 1, "three essences condensed into one BounceShroom");
        yield return new WaitForSeconds(3f);
        Vector3 at = Condensed(tree).transform.position;
        Check(at.y > tree.CapTop - 0.5f, $"on the solid cap it stays on top ({at.y - tree.CapTop:0.0} m from the cap top)");
        BounceShroom onSolidCap = Condensed(tree);

        var bouncy = GameObject.Find("Spiral Bounce Cap 1").GetComponent<MushroomTree>();
        for (int i = 0; i < 3; i++) bouncy.SpawnEssence();
        yield return new WaitForSeconds(4f);
        at = Condensed(bouncy).transform.position;
        float away = new Vector2(at.x - bouncy.transform.position.x, at.z - bouncy.transform.position.z).magnitude;
        float above = at.y - FungalHollowFloor(at.x, at.z, at.y + 0.5f);
        Check(at.y < bouncy.CapTop - 2f && above < 1.5f && away < 12f,
            $"off the bouncy cap it drops to the ground nearby ({away:0.0} m from the trunk, {above:0.0} m above the ground under it)");

        // 4. Eating essence: jump multiplier while it lasts.
        var meal = tree.SpawnEssence();
        Check(carrier.TryCarryFromInteraction(meal) && meal.IsCarried && !meal.OnStalk, "the player can pick the essence");
        typeof(MushroomEssence).GetField("boostSeconds", Private).SetValue(meal, 2f);
        meal.ReceiveInteraction(new CreatureInteraction(player.gameObject, meal.transform.position, Vector3.forward));
        yield return null;
        Check(meal == null && player.JumpMultiplier > 1.4f, $"eaten: jump multiplier {player.JumpMultiplier:0.00}");
        yield return new WaitForSeconds(2.5f);
        Check(Mathf.Approximately(player.JumpMultiplier, 1f) && player.GetComponent<JumpBoostEffect>() == null, "boost wore off after its time");

        // 5. Loose essence piled on the floor condenses by itself.
        shroomsBefore = FindObjectsByType<BounceShroom>(FindObjectsSortMode.None).Length;
        for (int i = 0; i < 3; i++)
        {
            var loose = tree.SpawnEssence();
            carrier.TryCarryFromInteraction(loose);
            carrier.Drop();
            loose.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            loose.GetComponent<Rigidbody>().position = new Vector3(4f + i * 0.4f, FungalHollowFloor(4f, -40f) + 0.4f, -40f);
        }
        yield return new WaitForSeconds(3f);
        shroomsAfter = FindObjectsByType<BounceShroom>(FindObjectsSortMode.None).Length;
        Check(shroomsAfter == shroomsBefore + 1, "three loose essences on the floor condensed into a BounceShroom");

        // 6. Hopping like a slime: it hops, squashes, stays near home, and never hops off a cap.
        BounceShroom hopper = null;
        foreach (var pair in nursery) if (pair.Key != null && pair.Key.name.StartsWith("Hub")) { hopper = pair.Key; break; }
        float startY = hopper.transform.position.y, hopRise = 0f, minSquash = 1f, maxStretch = 1f;
        var hopperBlock = hopper.GetComponentInChildren<BoxCollider>().transform;
        float compactY = hopperBlock.localScale.y;
        for (float t = 0f; t < 8f; t += Time.fixedDeltaTime)
        {
            yield return new WaitForFixedUpdate();
            hopRise = Mathf.Max(hopRise, hopper.transform.position.y - startY);
            minSquash = Mathf.Min(minSquash, hopperBlock.localScale.y / compactY);
            maxStretch = Mathf.Max(maxStretch, hopperBlock.localScale.y / compactY);
        }
        Check(hopRise > 0.4f, $"a nursery BounceShroom hops (rose {hopRise:0.00} m within 8 s)");
        Check(minSquash < 0.85f && maxStretch > 1.05f, $"it squashes ({minSquash:0.00}) and stretches ({maxStretch:0.00})");
        float drift = 0f, lowestOnCap = float.MaxValue;
        foreach (var pair in nursery)
        {
            if (pair.Key == null) continue;
            if (pair.Key.name.StartsWith("Hub")) drift = Mathf.Max(drift, Vector3.Distance(pair.Key.transform.position, pair.Value));
            if (pair.Key.name.StartsWith("West Cap")) lowestOnCap = Mathf.Min(lowestOnCap, pair.Key.transform.position.y);
        }
        Check(drift < 8f, $"floor nursery BounceShrooms stay near home (at most {drift:0.0} m away)");
        Check(lowestOnCap > 19f && onSolidCap.transform.position.y > tree.CapTop - 0.5f,
            $"shrooms on cap tops never hop off (West cap nursery lowest at {lowestOnCap:0.0} m; the cap top is 19.5)");

        // The grip is for the compact shroom only: expanded, the platform is frictionless as before.
        BounceShroom sample = null;
        foreach (var pair in nursery) if (pair.Key != null && pair.Key.CurrentState == BounceShroom.ShroomState.Compact) { sample = pair.Key; break; }
        var block = sample.GetComponentInChildren<BoxCollider>();
        string compactName = block.sharedMaterial != null ? block.sharedMaterial.name : "none";
        sample.ReceiveInteraction(new CreatureInteraction(player.gameObject, sample.transform.position, Vector3.down));
        string expandedName = block.sharedMaterial != null ? block.sharedMaterial.name : "none";
        Check(compactName == "BounceShroomGrip" && expandedName == "BouncySurface",
            $"collider material: {compactName} while compact, {expandedName} once expanded");

        Report = log.ToString();
        Debug.Log("[FungalHollow]\n" + Report);
    }

    /// <summary>The condensed BounceShroom closest to this tree's trunk.</summary>
    private static BounceShroom Condensed(MushroomTree tree)
    {
        BounceShroom best = null;
        float bestDistance = float.MaxValue;
        foreach (var s in FindObjectsByType<BounceShroom>(FindObjectsSortMode.None))
        {
            if (!s.name.Contains("condensed")) continue;
            Vector3 d = s.transform.position - tree.transform.position;
            float distance = new Vector2(d.x, d.z).magnitude;
            if (distance < bestDistance) { best = s; bestDistance = distance; }
        }
        return best;
    }

    /// <summary>Ground under a point, cast from just above it (casting from high up would hit caps).</summary>
    private static float FungalHollowFloor(float x, float z, float from = 6f) =>
        Physics.Raycast(new Vector3(x, from, z), Vector3.down, out RaycastHit hit, 200f, 1 << 7, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
}
