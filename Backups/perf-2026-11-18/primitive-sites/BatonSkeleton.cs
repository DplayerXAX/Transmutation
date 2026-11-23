using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A giant skeleton lying in the deepest chamber of the dark tunnels, one arm raised with the conductor baton in its
/// fist. The fist opens only in light: when a Glim (carried or free) is within reach of the hand, the fingers uncurl;
/// walk up to the baton while it is open to take it. Built in code from the Bone Kiln's marble.
/// </summary>
public sealed class BatonSkeleton : MonoBehaviour
{
    [Tooltip("A Glim this close to the hand opens it.")]
    [SerializeField] private float lightReach = 3.6f;
    [Tooltip("The player takes the baton from this close while the hand is open.")]
    [SerializeField] private float takeReach = 2.2f;

    public bool HandOpen => open > 0.75f;
    public Transform Grip { get; private set; }
    public ConductorBaton Baton { get; set; }

    private readonly List<Transform> fingers = new List<Transform>();
    private readonly List<Quaternion> fingerOpen = new List<Quaternion>(), fingerClosed = new List<Quaternion>();
    private Transform hand;
    private float open;
    private Material bone;

    public void Build(Vector3 floorPoint, Vector3 facing)
    {
        transform.position = floorPoint;
        transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(facing, Vector3.up).normalized, Vector3.up);
        // Pale bone that keeps a faint glow of its own, so it shows at arm's length in the black chamber
        // (the fog still swallows it a few metres away) and lights up properly under a Glim's lamp.
        bone = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Skeleton bone" };
        bone.SetColor("_BaseColor", new Color(0.8f, 0.78f, 0.72f));
        bone.SetFloat("_Smoothness", 0.25f);
        bone.EnableKeyword("_EMISSION");
        bone.SetColor("_EmissionColor", new Color(0.09f, 0.085f, 0.08f));

        // Half sunk in the floor: skull, spine and ribs arching out of the rock.
        Bone(PrimitiveType.Sphere, new Vector3(-3.6f, 0.4f, 0.6f), Quaternion.identity, new Vector3(2.2f, 1.9f, 2.6f));
        Bone(PrimitiveType.Sphere, new Vector3(-3.1f, 0.2f, 1.75f), Quaternion.identity, new Vector3(1.5f, 0.7f, 1.2f)); // jaw
        var eyeMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Skeleton socket" };
        eyeMat.SetColor("_BaseColor", Color.black);
        for (int side = -1; side <= 1; side += 2)
            Part(PrimitiveType.Sphere, new Vector3(-3.6f + side * 0.5f, 0.75f, 1.7f), Quaternion.identity, new Vector3(0.55f, 0.6f, 0.4f), eyeMat);
        for (int i = 0; i < 14; i++)
        {
            float x = -2.2f + i * 0.55f;
            Bone(PrimitiveType.Capsule, new Vector3(x, -0.1f + Mathf.Sin(i * 0.5f) * 0.15f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.45f, 0.26f, 0.45f));
        }
        for (int i = 0; i < 7; i++)
        {
            float x = -1.6f + i * 0.62f;
            float reach = Mathf.Lerp(2.3f, 1.3f, i / 6f);
            for (int side = -1; side <= 1; side += 2) Rib(new Vector3(x, 0f, 0f), side, reach);
        }

        // The raised arm: upper arm out of the ribs, forearm up, a fist at about head height.
        Vector3 shoulder = new Vector3(0.4f, 0.2f, 1.4f), elbow = new Vector3(0.9f, 1.4f, 2.6f), wrist = new Vector3(1.0f, 2.6f, 2.9f);
        Limb(shoulder, elbow, 0.32f);
        Limb(elbow, wrist, 0.26f);
        hand = new GameObject("Hand").transform;
        hand.SetParent(transform, false);
        hand.localPosition = wrist;
        hand.localRotation = Quaternion.Euler(-10f, 0f, 0f);
        Part(PrimitiveType.Sphere, Vector3.zero, Quaternion.identity, new Vector3(0.55f, 0.32f, 0.5f), bone, hand);
        Grip = new GameObject("Grip").transform;
        Grip.SetParent(hand, false);
        Grip.localPosition = new Vector3(0f, 0.32f, 0.08f);
        Grip.localRotation = Quaternion.Euler(0f, 0f, 70f);
        for (int i = 0; i < 5; i++)
        {
            bool thumb = i == 4;
            var root = new GameObject(thumb ? "Thumb" : "Finger").transform;
            root.SetParent(hand, false);
            root.localPosition = thumb ? new Vector3(-0.26f, 0.12f, 0.12f) : new Vector3(-0.2f + i * 0.13f, 0.14f, 0.24f);
            Quaternion closedRot = thumb ? Quaternion.Euler(-40f, 0f, -70f) : Quaternion.Euler(-150f, 0f, 0f);
            Quaternion openRot = thumb ? Quaternion.Euler(10f, 0f, -20f) : Quaternion.Euler(-25f, 0f, 0f);
            root.localRotation = closedRot;
            var seg = root;
            for (int k = 0; k < 2; k++)
            {
                Part(PrimitiveType.Capsule, new Vector3(0f, 0f, 0.13f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.09f, 0.13f, 0.09f), bone, seg);
                var next = new GameObject("Knuckle").transform;
                next.SetParent(seg, false);
                next.localPosition = new Vector3(0f, 0f, 0.26f);
                next.localRotation = thumb ? Quaternion.identity : Quaternion.Euler(-60f, 0f, 0f);
                fingers.Add(next);
                fingerClosed.Add(next.localRotation);
                fingerOpen.Add(Quaternion.Euler(thumb ? 0f : -8f, 0f, 0f));
                seg = next;
            }
            fingers.Add(root);
            fingerClosed.Add(closedRot);
            fingerOpen.Add(openRot);
        }
    }

    private void Update()
    {
        bool lit = false;
        if (hand != null)
            foreach (CaveGlowCreature c in CaveGlowCreature.All)
                if (c is GlimLantern g && !g.Hidden && Vector3.Distance(g.AimPoint, hand.position) < lightReach) { lit = true; break; }
        // Opens slowly in the light, closes slowly in the dark (only while it still holds the baton).
        bool holding = Baton != null && Baton.InFist;
        float target = lit || !holding ? 1f : 0f;
        open = Mathf.MoveTowards(open, target, Time.deltaTime * (target > open ? 0.6f : 0.35f));
        float t = Mathf.SmoothStep(0f, 1f, open);
        for (int i = 0; i < fingers.Count; i++) fingers[i].localRotation = Quaternion.Slerp(fingerClosed[i], fingerOpen[i], t);

        if (holding && HandOpen && Baton.PlayerDistance(Grip.position) < takeReach) Baton.Take();
    }

    private void Rib(Vector3 root, int side, float reach)
    {
        // A rib arch from the spine, out and up, then curving back down into the floor.
        Vector3 prev = root;
        for (int k = 1; k <= 6; k++)
        {
            float a = k / 6f * Mathf.PI * 0.95f;
            Vector3 p = root + new Vector3(0.05f * k, Mathf.Sin(a) * reach * 0.95f, side * (1f - Mathf.Cos(a)) * reach * 0.55f);
            Limb(prev, p, Mathf.Lerp(0.14f, 0.08f, k / 6f));
            prev = p;
        }
    }

    private void Limb(Vector3 a, Vector3 b, float radius)
    {
        Vector3 d = b - a;
        Bone(PrimitiveType.Capsule, (a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized), new Vector3(radius * 2f, d.magnitude * 0.5f + radius, radius * 2f));
    }

    private void Bone(PrimitiveType type, Vector3 local, Quaternion rotation, Vector3 scale) => Part(type, local, rotation, scale, bone);

    private GameObject Part(PrimitiveType type, Vector3 local, Quaternion rotation, Vector3 scale, Material material, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent != null ? parent : transform, false);
        go.transform.localPosition = local;
        go.transform.localRotation = rotation;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }
}
