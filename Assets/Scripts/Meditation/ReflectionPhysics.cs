using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Routes runtime queries to the visit's physics scene while world roots are suspended.</summary>
internal static class ReflectionPhysics
{
    internal static Scene Visit;
    internal static bool SwitchingWorldRoots;
    private static PhysicsScene Current => Visit.IsValid() ? Visit.GetPhysicsScene() : Physics.defaultPhysicsScene;
    private static readonly Collider[] CheckHits = new Collider[1];
    private const int Layers = Physics.DefaultRaycastLayers;
    private const QueryTriggerInteraction Triggers = QueryTriggerInteraction.UseGlobal;

    public static bool Raycast(Vector3 p, Vector3 d, float distance = float.PositiveInfinity, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        Current.Raycast(p, d, distance, mask, q);
    public static bool Raycast(Vector3 p, Vector3 d, out RaycastHit hit, float distance = float.PositiveInfinity, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        Current.Raycast(p, d, out hit, distance, mask, q);
    public static int RaycastNonAlloc(Vector3 p, Vector3 d, RaycastHit[] hits, float distance = float.PositiveInfinity, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        Current.Raycast(p, d, hits, distance, mask, q);
    public static int RaycastNonAlloc(Ray ray, RaycastHit[] hits, float distance = float.PositiveInfinity, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        RaycastNonAlloc(ray.origin, ray.direction, hits, distance, mask, q);
    public static bool Linecast(Vector3 a, Vector3 b, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        Raycast(a, b - a, (b - a).magnitude, mask, q);
    public static bool SphereCast(Vector3 p, float radius, Vector3 d, out RaycastHit hit, float distance = float.PositiveInfinity, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        Current.SphereCast(p, radius, d, out hit, distance, mask, q);
    public static int SphereCastNonAlloc(Vector3 p, float radius, Vector3 d, RaycastHit[] hits, float distance = float.PositiveInfinity, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        Current.SphereCast(p, radius, d, hits, distance, mask, q);
    public static int SphereCastNonAlloc(Ray ray, float radius, RaycastHit[] hits, float distance = float.PositiveInfinity, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        SphereCastNonAlloc(ray.origin, radius, ray.direction, hits, distance, mask, q);
    public static bool CapsuleCast(Vector3 a, Vector3 b, float radius, Vector3 d, out RaycastHit hit, float distance = float.PositiveInfinity, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        Current.CapsuleCast(a, b, radius, d, out hit, distance, mask, q);
    public static int CapsuleCastNonAlloc(Vector3 a, Vector3 b, float radius, Vector3 d, RaycastHit[] hits, float distance = float.PositiveInfinity, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        Current.CapsuleCast(a, b, radius, d, hits, distance, mask, q);
    public static bool BoxCast(Vector3 p, Vector3 half, Vector3 d, out RaycastHit hit, Quaternion rotation, float distance = float.PositiveInfinity, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        Current.BoxCast(p, half, d, out hit, rotation, distance, mask, q);
    public static int BoxCastNonAlloc(Vector3 p, Vector3 half, Vector3 d, RaycastHit[] hits, Quaternion rotation, float distance = float.PositiveInfinity, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        Current.BoxCast(p, half, d, hits, rotation, distance, mask, q);
    public static bool CheckSphere(Vector3 p, float radius, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        Current.OverlapSphere(p, radius, CheckHits, mask, q) > 0;
    public static bool CheckCapsule(Vector3 a, Vector3 b, float radius, int mask = Layers, QueryTriggerInteraction q = Triggers) =>
        Current.OverlapCapsule(a, b, radius, CheckHits, mask, q) > 0;
    public static int OverlapSphereNonAlloc(Vector3 p, float radius, Collider[] hits, int mask = Physics.AllLayers, QueryTriggerInteraction q = Triggers) =>
        Current.OverlapSphere(p, radius, hits, mask, q);
    public static int OverlapCapsuleNonAlloc(Vector3 a, Vector3 b, float radius, Collider[] hits, int mask = Physics.AllLayers, QueryTriggerInteraction q = Triggers) =>
        Current.OverlapCapsule(a, b, radius, hits, mask, q);
    public static Collider[] OverlapSphere(Vector3 p, float radius, int mask = Physics.AllLayers, QueryTriggerInteraction q = Triggers) =>
        Visit.IsValid() ? All<Collider>(hits => OverlapSphereNonAlloc(p, radius, hits, mask, q)) : Physics.OverlapSphere(p, radius, mask, q);
    public static RaycastHit[] RaycastAll(Vector3 p, Vector3 d, float distance, int mask, QueryTriggerInteraction q) =>
        Visit.IsValid() ? All<RaycastHit>(hits => RaycastNonAlloc(p, d, hits, distance, mask, q)) : Physics.RaycastAll(p, d, distance, mask, q);
    public static RaycastHit[] SphereCastAll(Vector3 p, float radius, Vector3 d, float distance, int mask, QueryTriggerInteraction q) =>
        Visit.IsValid() ? All<RaycastHit>(hits => SphereCastNonAlloc(p, radius, d, hits, distance, mask, q)) : Physics.SphereCastAll(p, radius, d, distance, mask, q);

    public static void SyncTransforms() => Physics.SyncTransforms();

    private static T[] All<T>(Func<T[], int> query)
    {
        var hits = new T[16];
        int count;
        while ((count = query(hits)) == hits.Length) Array.Resize(ref hits, hits.Length * 2);
        Array.Resize(ref hits, count);
        return hits;
    }
}
