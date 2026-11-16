using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Opt-in Play Mode probe; never attached to gameplay scenes.</summary>
public sealed class FishingPlayModeProbe : MonoBehaviour
{
    public static string Result = "Not run";
    public static string CatchTimings = "";
    Mouse testMouse; Keyboard testKeyboard;
    void OnDestroy() { if(testMouse!=null) InputSystem.RemoveDevice(testMouse); if(testKeyboard!=null) InputSystem.RemoveDevice(testKeyboard); }
    IEnumerator Start()
    {
        Result = "Running"; CatchTimings = "";
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
            if(pass==0)
            {
                testKeyboard=InputSystem.AddDevice<Keyboard>();
                InputSystem.QueueStateEvent(testKeyboard,new KeyboardState(Key.W));
                yield return new WaitForSeconds(.15f);
                Check(Vector3.ProjectOnPlane(c.PlayerController.Body.linearVelocity,Vector3.up).magnitude>1,"walking before bite");
            }
            PlaceFish(pole,target);
            float deadline=Time.time+9;
            while(pole.State==FishingState.Waiting && Time.time<deadline) yield return null;
            Check(pole.State==FishingState.Reeling && pole.TargetFish==target,"bite selected fish");
            Check(c.PlayerController.FishingInputOwner==pole,"movement owned");
                        if(pass==0)
            {

                Vector3 bitePosition=c.transform.position;
                yield return new WaitForSeconds(.2f);
                Check(Vector3.ProjectOnPlane(c.PlayerController.Body.linearVelocity,Vector3.up).magnitude<.1f,"bite brakes walking while W remains held");
                Check(Vector3.Distance(bitePosition,c.transform.position)<.6f,"bite does not coast indefinitely");
                c.PlayerController.AddBlastImpulse(Vector3.right);
                yield return new WaitForFixedUpdate();
                Check(c.PlayerController.Body.linearVelocity.x>.5f,"fishing preserves external impulse");
                yield return new WaitForSeconds(.3f);
                testMouse=InputSystem.AddDevice<Mouse>();
                var d=pole.Struggle.CounterDirection; var keys=new System.Collections.Generic.List<Key>();
                if(d.x<0) keys.Add(Key.A); if(d.x>0) keys.Add(Key.D); if(d.y<0) keys.Add(Key.S); if(d.y>0) keys.Add(Key.W);
                InputSystem.QueueStateEvent(testKeyboard,new KeyboardState(keys.ToArray()));
                yield return new WaitForSeconds(.12f);
                Check(pole.SteeringAccepted && pole.GetComponent<FishingPoleVisuals>().SteeringFeedback>.9f,"useful steering feedback before clicking");
                System.IO.Directory.CreateDirectory("Temp/FishingValidation");
                ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath("Temp/FishingValidation/steering.png"));
                float before=pole.Struggle.Progress;
                InputSystem.QueueStateEvent(testMouse,new MouseState().WithButton(MouseButton.Left,true));
                yield return new WaitForSeconds(.4f);
                Check(pole.Struggle.Progress>before+1.2f && pole.Struggle.Progress<before+1.85f,"holding LMB scores once");
                var visuals=pole.GetComponent<FishingPoleVisuals>();
                Check(visuals.bendPivot!=null && Quaternion.Angle(visuals.bendPivot.localRotation,Quaternion.identity)>1,"actual steering bends pole");
                InputSystem.QueueStateEvent(testMouse,new MouseState());
                InputSystem.QueueStateEvent(testKeyboard,new KeyboardState());
                yield return new WaitForSeconds(.3f);
                Check(Quaternion.Angle(visuals.bendPivot.localRotation,Quaternion.identity)<10,"steering returns leaving only bounded load bend");
                InputSystem.RemoveDevice(testMouse); InputSystem.RemoveDevice(testKeyboard); testMouse=null;testKeyboard=null;
            }
            float biteDistance=Vector3.Distance(target.transform.position,c.CarryCamera.transform.position);
            int deaths=0; target.Died += _ => deaths++;
            int catchEvents=0; System.Action<SeaAngelCreature> onCatch = _ => catchEvents++;
            pole.CatchSucceeded += onCatch;
            float struggleStarted=Time.time;
            deadline=Time.time+15;
            while(pole.State==FishingState.Reeling && Time.time<deadline)
            {
                pole.RefreshCounterDirection();
                Vector3 beforeWin=target.transform.position;
                if(pole.Struggle.Required-pole.Struggle.Progress<=1.5f*(1+.2f*pole.NormalizedLoad))
                {
                    var movingBody=target.GetComponent<Rigidbody>(); movingBody.isKinematic=false;
                    movingBody.linearVelocity=Vector3.one*4; movingBody.angularVelocity=Vector3.one*2;
                }
                pole.Pull(pole.RequiredDirection);
                if(pole.State==FishingState.Landing)
                {
                    var body=target.GetComponent<Rigidbody>();
                    Check(target.IsDead && target.Materialized && target.CaughtRetrieval && deaths==1,"win commits one physical death");
                    Check(body.linearVelocity==Vector3.zero && body.angularVelocity==Vector3.zero && target.SwimSpeed==0,"win clears all momentum");
                    Check(target.transform.position==beforeWin,"win does not snap position");
                    Check(catchEvents==1,"one distinct success event");
                    Check(pole.transform.Find("Catch Dimensional Mist").GetComponent<ParticleSystem>().particleCount>0,"success mist emitted at fish");
                    break;
                }
                if(pass==0)
                {
                    yield return new WaitForSeconds(.08f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath("Temp/FishingValidation/pull.png"));
                    yield return new WaitForSeconds(.42f);
                }
                else
                yield return new WaitForSeconds(.5f);
            }
            CatchTimings += $"{target.BodyLength:F2}m: {Time.time-struggleStarted:F2}s; ";
            Check(pole.State==FishingState.Landing,"reeling wins");
            Check(Vector3.Distance(target.transform.position,c.CarryCamera.transform.position)>Mathf.Min(4.2f,biteDistance*.75f),"fight keeps fish distant for final retrieval");
            pole.CatchSucceeded -= onCatch;
            Check(c.PlayerController.FishingInputOwner==pole,"movement stays locked during final retrieval");
            Vector3 startRetrieve=target.transform.position;
            yield return new WaitForSeconds(.2f);
            Check(Quaternion.Angle(pole.GetComponent<FishingPoleVisuals>().bendPivot.localRotation,Quaternion.identity)>15,"win produces distinct pole flick");
            if(pass==0) ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath("Temp/FishingValidation/catch.png"));
            if(pass==1) c.transform.position+=Vector3.right*.3f;
            deadline=Time.time+5;
            while(pole.State==FishingState.Landing && Time.time<deadline) yield return null;
            Check(c.PlayerController.FishingInputOwner==null,"arrival releases movement lock");
            Check(Vector3.Distance(startRetrieve,target.transform.position)>.4f,"retrieval moves physical body");
            Check(target.IsDead && deaths==1 && target.GetComponent<Rigidbody>().useGravity,"corpse remains dead with gravity");
            float caughtY=target.transform.position.y;
            yield return new WaitForSeconds(.3f);
            Check(target.transform.position.y<=caughtY+.05f && target.SwimSpeed==0,"corpse falls instead of swimming away");
            Check(target.Materialized && target.CanBeCarried && target.CanBeCaptured && target.CanBeConsumed,"materialized permissions");
            Check(pole.State==FishingState.Idle && target.ReservedBy==null,"landing cleanup");
            foreach(var collider in target.GetComponentsInChildren<Collider>()) Check(collider.enabled,"physical colliders enabled");
            target.transform.position = new Vector3(30,10,30);
        }
        yield return Cast(pole, c);
        PlaceFish(pole,fish[1]);
        float until=Time.time+9;
        while(pole.State==FishingState.Waiting && Time.time<until) yield return null;
        Check(pole.State==FishingState.Reeling,"cancel test bite");
        Check(!pole.TryHandleCarriedRightMouse(c,true,true,false),"RMB falls through to shared throw");
        Check(pole.State==FishingState.Idle && c.PlayerController.FishingInputOwner==null && fish[1].ReservedBy==null,"RMB cleanup");
                // Failure and component removal must release both claims.
        for(int mode=0;mode<3;mode++)
        {
            yield return Cast(pole, c);
            PlaceFish(pole,fish[2+mode]);
            until=Time.time+9;
            while(pole.State==FishingState.Waiting && Time.time<until) yield return null;
            Check(pole.State==FishingState.Reeling,"interruption bite");
            if(mode==0)
            {
                float progress=pole.Struggle.Progress; Time.timeScale=0;
                yield return new WaitForSecondsRealtime(.1f);
                Check(pole.Struggle.Progress==progress,"pause freezes struggle"); Time.timeScale=1;
                until=Time.time+25;
                while(pole.State!=FishingState.Idle && Time.time<until) yield return null;
            }
            else if(mode==1) { fish[3].gameObject.SetActive(false); yield return null; }
            else { pole.enabled=false; yield return null; pole.enabled=true; }
            Check(pole.State==FishingState.Idle && c.PlayerController.FishingInputOwner==null,"failure/disable unlocks");
            Check(fish[2+mode].ReservedBy==null,"failure/disable releases reservation");
        }
        Check(c.TryCarryFromInteraction(pole),"reacquire disabled pole");
        yield return new WaitForSeconds(.5f);
        for(int transfer=0;transfer<3;transfer++)
        {
            var target=fish[transfer==0 ? 1 : transfer==1 ? 2 : 4];
            yield return Cast(pole,c); PlaceFish(pole,target);
            until=Time.time+9;
            while(pole.State==FishingState.Waiting && Time.time<until) yield return null;
            Check(pole.State==FishingState.Reeling,"retrieval transfer bite");
            while(pole.State==FishingState.Reeling) { pole.RefreshCounterDirection(); pole.Pull(pole.RequiredDirection); yield return null; }
            Check(target.IsDead && pole.State==FishingState.Landing,"retrieval transfer commits catch");
            if(transfer==0) pole.TryHandleCarriedRightMouse(c,true,true,false);
            else if(transfer==1)
            {
                // Existing bubble capture accepts dead remains and invokes the shared ownership hook.
                var bubbleRoot=new GameObject("Validation Bubble"); bubbleRoot.transform.position=target.transform.position;
                var bubble=bubbleRoot.AddComponent<TrappingBubble>();
                var capture=target.gameObject.AddComponent<BubbleCapture>();
                Check(capture.Attach(bubble),"bubble takes corpse ownership");
                Check(pole.State==FishingState.Idle,"capture releases retrieval immediately");
                capture.Release(); Destroy(bubbleRoot);
            }
            else
            {
                var obstruction=GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstruction.name="Validation Catch Space"; obstruction.transform.position=c.CarryCamera.transform.position;
                obstruction.transform.localScale=Vector3.one*5; Physics.SyncTransforms();
                yield return new WaitForFixedUpdate(); yield return null;
                Check(pole.State==FishingState.Idle,"no clear catch space releases corpse"); Destroy(obstruction);
            }
            Check(pole.State==FishingState.Idle && target.ReservedBy==null && !target.CaughtRetrieval && target.IsDead,"retrieval transfer preserves dead body and clears claims");
            target.transform.position=new Vector3(30,10,30);
        }
        yield return Cast(pole, c);
        PlaceFish(pole,fish[5]);
        until=Time.time+9;
        while(pole.State==FishingState.Waiting && Time.time<until) yield return null;
        while(pole.State==FishingState.Reeling) { pole.RefreshCounterDirection(); pole.Pull(pole.RequiredDirection); yield return null; }
        Check(pole.State==FishingState.Landing,"blocked physical retrieval setup");
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name="Validation Obstruction";
        wall.transform.position=c.CarryCamera.transform.position+c.CarryCamera.transform.forward;
        wall.transform.localScale=new Vector3(10,10,.1f); Physics.SyncTransforms();
        until=Time.time+5;
        while(pole.State==FishingState.Landing && Time.time<until) yield return null;
        Check(pole.State==FishingState.Idle && fish[5].Materialized && fish[5].IsDead,"blocked retrieval releases committed corpse");
        Check(fish[5].transform.position.z>wall.transform.position.z,"corpse cannot teleport through wall"); Destroy(wall);
        c.Release();
        Check(c.TryCarryFromInteraction(fish[0]),"caught fish pickup");
        c.GetComponent<CreatureThrower>().Throw(1);
        Check(!fish[0].IsCarried && !fish[0].SimulationEnabled,"caught fish shared throw");
        Result="PASS: head-first steering/approach, charged casting, small/large catches, dimensional gates, single death/momentum reset/physical retrieval/gravity, movement ownership, RMB cleanup, hold-click input, pause/failure/disable cleanup, blocked physical retrieval, caught fish pickup/throw.";
        Debug.Log("FISHING VALIDATION " + Result); Destroy(gameObject);
    }
    void PlaceFish(FishingPoleCreature pole, SeaAngelCreature selected)
    {
        foreach(var f in FindObjectsByType<SeaAngelCreature>(FindObjectsSortMode.None))
            if(!f.Materialized && f.ReservedBy==null) f.transform.position=new Vector3(30,10,30);
        selected.transform.position=pole.LurePosition;
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
