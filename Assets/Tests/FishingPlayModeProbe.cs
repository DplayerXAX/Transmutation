using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Opt-in Play Mode probe; never attached to gameplay scenes.</summary>
public sealed class FishingPlayModeProbe : MonoBehaviour
{
    public static string Result = "Not run";
    Mouse testMouse; Keyboard testKeyboard;
    void OnDestroy() { if(testMouse!=null) InputSystem.RemoveDevice(testMouse); if(testKeyboard!=null) InputSystem.RemoveDevice(testKeyboard); }
    IEnumerator Start()
    {
        Result = "Running";
        yield return new WaitForSeconds(.5f);
        var c = FindFirstObjectByType<PlayerCreatureCarrier>();
        var pole = FindFirstObjectByType<FishingPoleCreature>();
        var fish = FindObjectsByType<SeaAngelCreature>(FindObjectsSortMode.None);
        Array.Sort(fish,(a,b)=>a.BodyLength.CompareTo(b.BodyLength));
        Check(fish.Length==8,"eight fish");
        foreach(var f in fish)
        {
            Check(!f.CanBeCarried && !f.CanBeCaptured && !f.CanBeConsumed && !f.CanBeAITargeted,"dimensional eligibility");
            Check(!f.ApplyDamage(1),"dimensional damage rejected");
            foreach(var collider in f.GetComponentsInChildren<Collider>()) Check(!collider.enabled,"dimensional colliders disabled");
            f.transform.position = new Vector3(30,10,30);
        }
        c.PlayerController.SetLook(0,0);
        Check(c.TryCarryFromInteraction(pole),"pickup pole"); yield return new WaitForSeconds(.5f);
        // Exercise actual press/hold/release through the shared item interactor.
        testMouse=InputSystem.AddDevice<Mouse>();
        InputSystem.QueueStateEvent(testMouse,new MouseState().WithButton(MouseButton.Left,true));
        yield return new WaitForSeconds(.12f);
        Check(pole.State==FishingState.Charging && pole.CastCharge01>0 && pole.CastCharge01<.25f,"short hold charges without casting");
        Vector3 castOrigin=c.CarryCamera.transform.position;
        InputSystem.QueueStateEvent(testMouse,new MouseState());
        yield return new WaitForSeconds(.6f);
        float shortDistance=Vector3.Distance(castOrigin,pole.LurePosition);
        Check(pole.State==FishingState.Waiting && shortDistance>=pole.CastDistance(0)-.05f && shortDistance<4,"short release casts near minimum");
        pole.CancelFishing();
        InputSystem.QueueStateEvent(testMouse,new MouseState().WithButton(MouseButton.Left,true));
        yield return new WaitForSeconds(1.4f);
        Check(pole.State==FishingState.Charging && pole.CastCharge01==1,"full charge waits for release");
        InputSystem.QueueStateEvent(testMouse,new MouseState());
        yield return new WaitForSeconds(.6f);
        Check(pole.State==FishingState.Waiting && Mathf.Abs(Vector3.Distance(castOrigin,pole.LurePosition)-pole.CastDistance(1))<.1f,"full release reaches maximum");
        pole.CancelFishing(); InputSystem.RemoveDevice(testMouse); testMouse=null;
        // The mesh head is +Y locally, transformed to the root's swimming forward.
        var swimmer = fish[6];
        Check(Vector3.Dot(swimmer.transform.Find("Visual").up, swimmer.transform.forward) > .999f, "head axis faces swimming direction");
        Check(swimmer.TryReserve(pole), "reserve motion probe");
        Vector3 bait = swimmer.transform.position + Vector3.forward * 3;
        swimmer.transform.rotation = Quaternion.LookRotation(Vector3.back);
        Vector3 beforeNotice = swimmer.transform.position;
        swimmer.NoticeBait(bait, .1f);
        Check(Vector3.Distance(swimmer.transform.position, beforeNotice) < .05f, "notice turns before moving");
        bool arrived = false;
        for(int step=0;step<600;step++)
        {
            Vector3 before = swimmer.transform.position;
            arrived = swimmer.ApproachBait(bait, 1f/60);
            Vector3 travel = swimmer.transform.position-before;
            Check(travel.magnitude < .016f, "approach speed bounded");
            if(travel.sqrMagnitude > .0000001f) Check(Vector3.Dot(travel.normalized, swimmer.transform.forward) > .99f, "approach swims head first");
            if(arrived) break;
        }
        Check(arrived, "turning approach reaches bait");
        swimmer.ReleaseReservation(pole); swimmer.transform.position = new Vector3(30,10,30);
        for(int pass=0;pass<2;pass++)
        {
            var target=pass==0?fish[0]:fish[7];
            yield return Cast(pole, c);
            Check(pole.State==FishingState.Waiting,"cast waiting");
            target.transform.position=pole.LurePosition;
            float deadline=Time.time+9;
            while(pole.State==FishingState.Waiting && Time.time<deadline) yield return null;
            Check(pole.State==FishingState.Reeling && pole.TargetFish==target,"bite selected fish");
            Check(c.PlayerController.FishingInputOwner==pole,"movement owned");
                        if(pass==0)
            {
                while(pole.Struggle.Telegraphing) yield return null;
                testMouse=InputSystem.AddDevice<Mouse>(); testKeyboard=InputSystem.AddDevice<Keyboard>();
                var d=pole.Struggle.CounterDirection; var keys=new System.Collections.Generic.List<Key>();
                if(d.x<0) keys.Add(Key.A); if(d.x>0) keys.Add(Key.D); if(d.y<0) keys.Add(Key.S); if(d.y>0) keys.Add(Key.W);
                InputSystem.QueueStateEvent(testKeyboard,new KeyboardState(keys.ToArray()));
                yield return null;
                float before=pole.Struggle.Progress;
                InputSystem.QueueStateEvent(testMouse,new MouseState().WithButton(MouseButton.Left,true));
                yield return new WaitForSeconds(.4f);
                Check(pole.Struggle.Progress>before+.3f && pole.Struggle.Progress<before+1.1f,"holding LMB scores once");
                InputSystem.QueueStateEvent(testMouse,new MouseState());
                InputSystem.QueueStateEvent(testKeyboard,new KeyboardState());
                yield return null;
                InputSystem.RemoveDevice(testMouse); InputSystem.RemoveDevice(testKeyboard); testMouse=null;testKeyboard=null;
            }
            float biteDistance=Vector3.Distance(target.transform.position,c.CarryCamera.transform.position);
            deadline=Time.time+15;
            while(pole.State==FishingState.Reeling && Time.time<deadline)
            {
                pole.Pull(pole.Struggle.CounterDirection);
                yield return new WaitForSeconds(.08f);
            }
            Check(pole.State==FishingState.Landing,"reeling wins");
            Check(Vector3.Distance(target.transform.position,c.CarryCamera.transform.position)<biteDistance-.5f,"progress physically shortens tether");
            Check(c.PlayerController.FishingInputOwner==null,"success unlocks");
            yield return new WaitForSeconds(1);
            Check(target.Materialized && target.CanBeCarried && target.CanBeCaptured && target.CanBeConsumed,"materialized permissions");
            Check(pole.State==FishingState.Idle && target.ReservedBy==null,"landing cleanup");
            foreach(var collider in target.GetComponentsInChildren<Collider>()) Check(collider.enabled,"physical colliders enabled");
            target.transform.position = new Vector3(30,10,30);
        }
        yield return Cast(pole, c);
        fish[1].transform.position=pole.LurePosition;
        float until=Time.time+9;
        while(pole.State==FishingState.Waiting && Time.time<until) yield return null;
        Check(pole.State==FishingState.Reeling,"cancel test bite");
        Check(!pole.TryHandleCarriedRightMouse(c,true,true,false),"RMB falls through to shared throw");
        Check(pole.State==FishingState.Idle && c.PlayerController.FishingInputOwner==null && fish[1].ReservedBy==null,"RMB cleanup");
                // Failure and component removal must release both claims.
        for(int mode=0;mode<3;mode++)
        {
            yield return Cast(pole, c);
            fish[2+mode].transform.position=pole.LurePosition;
            until=Time.time+9;
            while(pole.State==FishingState.Waiting && Time.time<until) yield return null;
            Check(pole.State==FishingState.Reeling,"interruption bite");
            if(mode==0)
            {
                float progress=pole.Struggle.Progress; Time.timeScale=0;
                yield return new WaitForSecondsRealtime(.1f);
                Check(pole.Struggle.Progress==progress,"pause freezes struggle"); Time.timeScale=1;
                until=Time.time+12;
                while(pole.State!=FishingState.Idle && Time.time<until) yield return null;
            }
            else if(mode==1) { fish[3].gameObject.SetActive(false); yield return null; }
            else { pole.enabled=false; yield return null; pole.enabled=true; }
            Check(pole.State==FishingState.Idle && c.PlayerController.FishingInputOwner==null,"failure/disable unlocks");
            Check(fish[2+mode].ReservedBy==null,"failure/disable releases reservation");
        }
        Check(c.TryCarryFromInteraction(pole),"reacquire disabled pole");
        yield return new WaitForSeconds(.5f);
        yield return Cast(pole, c);
        fish[5].transform.position=pole.LurePosition;
        until=Time.time+9;
        while(pole.State==FishingState.Waiting && Time.time<until) yield return null;
        while(pole.State==FishingState.Reeling) { pole.Pull(pole.Struggle.CounterDirection); yield return null; }
        Check(pole.State==FishingState.Landing,"blocked landing setup");
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name="Validation Obstruction";
        wall.transform.position=c.CarryCamera.transform.position+c.CarryCamera.transform.forward;
        wall.transform.localScale=new Vector3(10,10,.1f); Physics.SyncTransforms();
        yield return null; yield return null;
        Check(pole.State==FishingState.Idle && !fish[5].Materialized,"wall blocks materialization"); Destroy(wall);
        c.Release();
        Check(c.TryCarryFromInteraction(fish[0]),"caught fish pickup");
        c.GetComponent<CreatureThrower>().Throw(1);
        Check(!fish[0].IsCarried && !fish[0].SimulationEnabled,"caught fish shared throw");
        Result="PASS: head-first steering/approach, charged casting, small/large catches, dimensional gates, materialization/colliders, movement ownership, RMB cleanup, hold-click input, pause/failure/disable cleanup, blocked landing, caught fish pickup/throw.";
        Debug.Log("FISHING VALIDATION " + Result); Destroy(gameObject);
    }
    IEnumerator Cast(FishingPoleCreature pole, PlayerCreatureCarrier carrier)
    {
        pole.BeginCastCharge();
        Check(pole.State==FishingState.Charging, "cast begins charging");
        pole.UpdateCastCharge(true, false, 2);
        Check(pole.CastCharge01==1, "cast charge caps");
        pole.ReleaseCast();
        Check(pole.State==FishingState.Casting, "release launches lure");
        float deadline=Time.time+2;
        while(pole.State==FishingState.Casting && Time.time<deadline) yield return null;
        Check(pole.State==FishingState.Waiting, "lure settles before attraction");
    }
    void Check(bool condition,string message)
    {
        if(condition) return;
        Result="FAIL: "+message; Debug.LogError("FISHING VALIDATION "+Result); throw new Exception(Result);
    }
}

