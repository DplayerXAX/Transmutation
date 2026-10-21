using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Focused regression checks for the capture handoff, without needing the generated world.</summary>
public static class BoneChildCatchValidation
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run BoneChildCatchValidation in an isolated batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var staging = new GameObject("Bone catch validation").transform;
        Vector3 origin = new Vector3(10000f, 1000f, 10000f);
        int checks = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Bone catch: " + description);
            checks++;
        }
        BoneChild Child(Vector3 position)
        {
            var go = new GameObject("Test bone child");
            go.transform.SetParent(staging);
            go.transform.position = position;
            BoneChild child = go.AddComponent<BoneChild>();
            // Only the head/chest anchors are needed to exercise capture, not the generated visual meshes.
            var beads = (List<Transform>)typeof(BoneChild).GetField("beads", Private).GetValue(child);
            for (int i = 0; i < 4; i++)
            {
                Transform bead = new GameObject("Capture anchor").transform;
                bead.SetParent(go.transform, false);
                beads.Add(bead);
            }
            BoneChild.All.Add(child);
            return child;
        }
        FireBubble Bubble(Vector3 position)
        {
            var go = new GameObject("Zero Heat fire bubble");
            go.transform.SetParent(staging);
            go.transform.position = position;
            return go.AddComponent<FireBubble>();
        }
        void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);

        try
        {
            BoneChild child = Child(origin);
            FireBubble fire = Bubble(origin + Vector3.up);
            Check(fire.Heat == 0f, "fixture matches the authored bubble's zero Heat");
            Check(child.Catch(fire), "zero Heat FireBubble is accepted");
            Check(child.Cradled == fire && fire.IsBoundToBone, "capture hands ownership to the ribs");
            Check(fire.transform.IsChildOf(child.transform), "bubble follows its bone owner");
            Check(!fire.CanBeCarried && !fire.CanBeConsumed, "bound bubble cannot be taken away");
            Call(child, "UpdateHeat");
            Check((float)typeof(BoneChild).GetField("heat", Private).GetValue(child) >= child.heatForLift,
                "zero Heat FireBubble still supplies enough warmth to fly");
            Check(child.Catch(Bubble(origin)) && child.Catch(Bubble(origin)), "other rib pairs accept two reserve bubbles");
            Check(!child.Catch(Bubble(origin)) && child.Cradled == fire, "full ribs refuse a fourth bubble without replacing existing fuel");

            fire.LifetimeLimited = true;
            typeof(FireBubble).GetField("lifetimeSeconds", Private).SetValue(fire, 0f);
            typeof(FireBubble).GetField("inanimateToFlowerDelay", Private).SetValue(fire, 0f);
            for (int i = 0; i < 1000; i++) Call(fire, "FixedUpdate");
            Check(!fire.IsInanimate && !fire.GetComponent<Rigidbody>().useGravity,
                "bound bubble ignores expired flight and flower timers");
            Check(!fire.GetComponent<SphereCollider>().enabled, "bound bubble cannot collide with predators or traps");

            BoneChild thrownTo = Child(origin + Vector3.right * 20f);
            FireBubble thrown = Bubble(thrownTo.Chest - Vector3.right * 6f);
            Physics.SyncTransforms();
            Check(BoneChild.CatchAlongPath(thrown, thrown.transform.position, thrownTo.Chest + Vector3.right * 6f),
                "a fast throw crossing the reach is caught between frames");
            Check(thrownTo.Cradled == thrown, "swept catch selects the nearby bone");

            BoneChild behindWall = Child(origin + Vector3.right * 40f);
            FireBubble blocked = Bubble(behindWall.Chest - Vector3.right * 6f);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(staging);
            wall.layer = 7;
            wall.transform.position = behindWall.Chest - Vector3.right;
            wall.transform.localScale = new Vector3(0.4f, 10f, 10f);
            Physics.SyncTransforms();
            Check(!BoneChild.CatchAlongPath(blocked, blocked.transform.position, behindWall.Chest - Vector3.right * 2f),
                "a wall between the throw and ribs blocks capture");

            BoneChild crowded = Child(origin + Vector3.right * 60f);
            for (int i = 0; i < 64; i++)
            {
                var obstacle = new GameObject("Crowded overlap");
                obstacle.transform.SetParent(staging);
                obstacle.transform.position = crowded.Chest;
                obstacle.AddComponent<SphereCollider>().radius = 0.1f;
            }
            FireBubble dropped = Bubble(crowded.Chest + Vector3.up);
            Physics.SyncTransforms();
            Call(crowded, "UpdateHeat");
            Check(crowded.Cradled == dropped, "a dropped bubble is found even with more than 16 overlapping colliders");
            Debug.Log($"Bone child capture passed: {checks} assertions.");
        }
        finally
        {
            Object.DestroyImmediate(staging.gameObject);
        }
    }
}
