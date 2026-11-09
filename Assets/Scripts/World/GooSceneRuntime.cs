using System.Collections;
using UnityEngine;

/// <summary>Only sequences the two authored tutorial drops; the creature ecology remains autonomous.</summary>
public sealed class GooSceneRuntime : MonoBehaviour
{
    [SerializeField] private ProceduralWorld world;
    [SerializeField] private GooCreature[] gateDrops;
    public bool DropsReleased { get; private set; }

    private IEnumerator Start()
    {
        if (world == null || gateDrops == null) yield break;
        while (!world.IsRegionReady(transform.TransformPoint(new Vector3(0f, -8f, 32f)), 9f)) yield return null;
        foreach (GooCreature goo in gateDrops)
        {
            if (goo == null || goo.IsDead || goo.IsCarried) continue;
            Rigidbody body = goo.GetComponent<Rigidbody>();
            body.isKinematic = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.useGravity = true;
            body.WakeUp();
        }
        DropsReleased = true;
    }
}
