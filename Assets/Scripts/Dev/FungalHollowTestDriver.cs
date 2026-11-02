using System.Collections;
using System.Text;
using UnityEngine;

/// <summary>
/// Play-mode check for Fungal Hollow: drops the player onto fixed caps of each launch speed and measures
/// how high the feet rise above the cap top, against the 0.95 v²/2g the build's reachability pass assumes.
/// The player's movement scripts are switched off so nothing but the cap moves the body.
/// </summary>
public sealed class FungalHollowTestDriver : MonoBehaviour
{
    public static string Report = "running";
    private readonly StringBuilder log = new StringBuilder();

    private IEnumerator Start()
    {
        Report = "running";
        var player = GameObject.Find("PlayerObjects").GetComponentInChildren<SmoothFirstPersonController>().GetComponent<Rigidbody>();
        foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>())
            if (behaviour is SmoothFirstPersonController || behaviour is HandClimber || behaviour is AdvancedFirstPersonTraversal)
                behaviour.enabled = false;

        // Body height above whatever it stands on.
        yield return new WaitForSeconds(1f);
        float stand = player.position.y - 9f; // the arrival ledge top is at 9 m

        foreach (string name in new[] { "Spiral Bounce Cap 1", "East Sentinel", "Crown Puffball — big launch", "Bog Step Cap 1" })
        {
            var tree = GameObject.Find(name);
            var spring = tree.GetComponentInChildren<BouncySurface>();
            var platform = spring.GetComponentInChildren<BoxCollider>();
            float top = platform.bounds.max.y;
            player.linearVelocity = Vector3.zero;
            player.position = new Vector3(platform.bounds.center.x, top + stand + 0.6f, platform.bounds.center.z);
            float peak = -999f;
            for (float t = 0f; t < 2.5f; t += Time.fixedDeltaTime)
            {
                yield return new WaitForFixedUpdate();
                peak = Mathf.Max(peak, player.position.y);
                // Hold it over the cap so it lands back on it rather than drifting off.
                player.linearVelocity = new Vector3(0f, player.linearVelocity.y, 0f);
            }
            float rise = peak - stand - top, expected = 0.95f * spring.LaunchSpeed * spring.LaunchSpeed / 60f;
            log.AppendLine($"{name}: launch {spring.LaunchSpeed}, feet rose {rise:0.00} m over the cap (pass assumes {expected:0.00})");
        }
        Report = log.ToString();
        Debug.Log("[FungalHollow]\n" + Report);
    }
}
