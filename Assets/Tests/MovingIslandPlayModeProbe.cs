using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>Explicit representative-behavior probe. Not attached to saved scenes.</summary>
public sealed class MovingIslandPlayModeProbe : MonoBehaviour
{
    public static string Result = "Not run";
    public static string Measurements = "";
    public GameObject squirrelPrefab, birdPrefab, rootedPrefab, eggPrefab;
    public float idleSeconds = 30;
    IEnumerator Start()
    {
        Result = "Running";
        yield return new WaitForSeconds(1);
        var frame = FindFirstObjectByType<MovingFrame>();
        var player = FindFirstObjectByType<SmoothFirstPersonController>();
        var pole = FindFirstObjectByType<FishingPoleCreature>();
        var fish = FindFirstObjectByType<SeaAngelCreature>();
        Check(frame && frame.Members.Count >= 9, "registered island passengers");
        pole.gameObject.SetActive(false);
        foreach (var stone in FindObjectsByType<DimensionStone>(FindObjectsSortMode.None)) stone.gameObject.SetActive(false);
        yield return new WaitForSeconds(3);
        Check(!CreatureSimulation.Allows(fish), "fish source dormancy");
        Vector3 fishLocal = frame.transform.InverseTransformPoint(fish.transform.position);
        Vector3 playerLocal = frame.transform.InverseTransformPoint(player.Body.position);
        float startDistance = frame.Travelled;
        yield return new WaitForSeconds(idleSeconds);
        float fishDrift = Vector3.Distance(fishLocal, frame.transform.InverseTransformPoint(fish.transform.position));
        float playerDrift = Vector3.Distance(playerLocal, frame.transform.InverseTransformPoint(player.Body.position));
        Measurements = $"{idleSeconds}s: player local drift {playerDrift:F4}m, sleeping fish drift {fishDrift:F4}m, island travel {frame.Travelled-startDistance:F2}m";
        Check(playerDrift < .1f, "idle player transport: " + Measurements);
        Check(fishDrift < .1f, "sleeping fish transport: " + Measurements);
        Check(frame.Travelled-startDistance > idleSeconds * 2, "physics advances");
        pole.gameObject.SetActive(true);
        fish.transform.position = frame.transform.TransformPoint(new Vector3(0,3,-8));
        DimensionalSchool.For(fish).Reindex(fish);
        Check(fish.TryReserve(pole), "sleeping fish can be reserved after travel");
        fish.ReleaseReservation(pole);

        var squirrel = Spawn(squirrelPrefab, frame, new Vector3(-8,2,-10));
        var motor = squirrel.GetComponent<SquirrelMotor>();
        typeof(Squirrel).GetField("activity", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(squirrel, Squirrel.Activity.Fighting);
        typeof(Squirrel).GetField("territoryRadius", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(squirrel, 10000f);
        Vector3 squirrelStart = frame.transform.InverseTransformPoint(squirrel.transform.position);
        float until = Time.time + 3;
        while (Time.time < until)
        { motor.SetIntent(frame.transform.right * 1.5f); yield return new WaitForFixedUpdate(); }
        motor.Stop();
        Vector3 squirrelEnd = frame.transform.InverseTransformPoint(squirrel.transform.position);
        Check(squirrelEnd.x - squirrelStart.x > 1, "squirrel motor can walk relative to moving deck");
        CreatureSimulation.DistanceDormant(squirrel, this, true);
        var rooted = Spawn(rootedPrefab, frame, new Vector3(8,0,8));
        Vector3 rootLocal = frame.transform.InverseTransformPoint(rooted.transform.position);
        var bird = Spawn(birdPrefab, frame, new Vector3(0,12,0));
        var egg = Spawn(eggPrefab, frame, new Vector3(-12,.3f,8)) as CeilingBatBugEgg;
        yield return new WaitForSeconds(4);
        Check(Vector3.Distance(rootLocal, frame.transform.InverseTransformPoint(rooted.transform.position)) < .1f, "rooted presentation transport");
        Check(bird.GetComponent<Rigidbody>().linearVelocity.magnitude > 1 && bird.GetComponent<MovementFrameMember>().Frame == frame, "flying flock member remains active aboard");
        Check(egg.IsAdhered, "egg acquires deck support");
        Vector3 eggLocal = frame.transform.InverseTransformPoint(egg.transform.position);
        yield return new WaitForSeconds(2);
        Check(Vector3.Distance(eggLocal, frame.transform.InverseTransformPoint(egg.transform.position)) < .15f, "surface-local attachment follows deck");
        CreatureSimulation.DistanceDormant(squirrel, this, false);
        Check(squirrel.SimulationEnabled, "ground creature resumes");

        var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        Check(carrier.TryCarryFromInteraction(pole), "carry fishing pole aboard");
        player.SetLook(frame.transform.eulerAngles.y, 0);
        pole.BeginCastCharge(); pole.ReleaseCast();
        yield return new WaitForSeconds(.6f);
        Check(pole.State == FishingState.Waiting, "cast follows frame into waiting");
        fish.transform.position = pole.LurePosition;
        DimensionalSchool.For(fish).Reindex(fish);
        until = Time.time + 10;
        while (pole.State != FishingState.Reeling && Time.time < until) yield return null;
        Check(pole.State == FishingState.Reeling, "fish approaches moving lure");
        until = Time.time + 10;
        while (pole.State == FishingState.Reeling && Time.time < until)
        { pole.RefreshCounterDirection(); pole.Pull(pole.RequiredDirection); yield return null; }
        Check(fish.IsDead && fish.Materialized, "catch materializes into physical retrieval");
        until = Time.time + 8;
        while (pole.State == FishingState.Landing && Time.time < until) yield return null;
        Check(pole.State == FishingState.Idle, "retrieval releases fishing movement lock");
        carrier.Release();
        Check(carrier.TryCarryFromInteraction(squirrel), "carry walking creature");
        var thrower = carrier.GetComponent<CreatureThrower>() ?? carrier.gameObject.AddComponent<CreatureThrower>();
        thrower.Throw(.4f);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Check(CreatureThrower.IsFlying(squirrel) && squirrel.GetComponent<MovementFrameMember>().Frame == null, "throw retains motion authority inside habitat");
        Vector3 beforeJump = frame.transform.InverseTransformPoint(player.Body.position);
        typeof(SmoothFirstPersonController).GetMethod("Jump", BindingFlags.Instance|BindingFlags.NonPublic).Invoke(player, null);
        yield return new WaitForSeconds(.15f);
        Vector3 afterJump = frame.transform.InverseTransformPoint(player.Body.position);
        Check(afterJump.y > beforeJump.y + .2f && Mathf.Abs(afterJump.x-beforeJump.x) < .3f, "jump remains relative to habitat");
        yield return new WaitForSeconds(1.5f);
        var meshObject = new GameObject("Baked mesh compatibility probe"); meshObject.transform.SetParent(frame.transform, false);
        meshObject.transform.localPosition = new Vector3(15,2,-24);
        var mesh = new Mesh(); mesh.vertices = new[] { new Vector3(-2,0,-2), new Vector3(-2,0,2), new Vector3(2,0,2), new Vector3(2,0,-2) };
        mesh.triangles = new[] { 0,1,2,0,2,3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var meshCollider = meshObject.AddComponent<MeshCollider>(); meshCollider.sharedMesh = mesh;
        Physics.SyncTransforms();
        Check(meshCollider.attachedRigidbody == frame.GetComponent<Rigidbody>() && meshCollider.Raycast(new Ray(meshObject.transform.position+Vector3.up*3, Vector3.down), out var meshHit, 5), "baked concave mesh collision on kinematic frame");
        Destroy(meshObject); Destroy(mesh);
        var playerMember = player.GetComponent<MovementFrameMember>();
        Vector3 oldPosition = player.Body.position;
        player.Body.position = frame.transform.TransformPoint(new Vector3(100,10,0));
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Check(playerMember.Frame == null, "boundary exit releases habitat");
        player.Body.position = frame.transform.TransformPoint(new Vector3(0,3,-15));
        player.Body.linearVelocity = frame.VelocityAt(player.Body.position);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Check(playerMember.Frame == frame, "habitat reentry");
        frame.ResetFlight();
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Check(player.Body.linearVelocity.magnitude < 10, "reset does not impart teleport velocity");
        Result = "PASS: idle riding, dormant fish, wake/reserve, squirrel walking, rooted transport, bird flight, attached egg, full moving catch, throw, jump, mesh collision, boundary exit/reentry, flight reset";
    }
    Creature Spawn(GameObject prefab, MovingFrame frame, Vector3 local)
    {
        var go = Instantiate(prefab, frame.transform.TransformPoint(local), Quaternion.identity);
        // This is an explicit placement aboard, rather than a world-stationary object swept up by the habitat.
        var body = go.GetComponent<Rigidbody>();
        if (body && !body.isKinematic) body.linearVelocity += frame.VelocityAt(body.position);
        var member = go.GetComponent<MovementFrameMember>() ?? go.AddComponent<MovementFrameMember>(); member.Join(frame);
        return go.GetComponent<Creature>();
    }
    static void Check(bool value, string label)
    {
        if (value) return;
        Result = "FAIL: " + label;
        throw new InvalidOperationException(Result);
    }
}
