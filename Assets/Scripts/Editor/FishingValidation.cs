using System;
using UnityEngine;
using UnityEditor;

public static class FishingValidation
{
    [MenuItem("Tools/Creatures/Fishing/Validate Rules")]
    public static void ValidateFromMenu() { Debug.Log(ValidateRules()); }
    public static string ValidateRules()
    {
        Vector2Int Direction(Vector3 position, Vector2Int previous = default) => FishingCounterPull.Calculate(Vector3.zero, position, Vector3.right, Vector3.up, previous);
        Check(Direction(new Vector3(2,0,5)) == Vector2Int.left, "right fish needs left irrespective of velocity");
        Check(Direction(new Vector3(1,1,5)) == new Vector2Int(-1,-1), "upper right needs down-left");
        Check(Direction(new Vector3(.3f,0,5)) == Vector2Int.zero, "neutral persists below outer threshold");
        Check(Direction(new Vector3(.3f,0,5), Vector2Int.left) == Vector2Int.left, "active direction persists above inner threshold");
        Check(Direction(new Vector3(.2f,0,5), Vector2Int.left) == Vector2Int.zero, "inner threshold enters neutral");
        Check(Direction(new Vector3(-.3f,0,5), Vector2Int.left) == Vector2Int.zero, "crossing through neutral");
        Check(Direction(new Vector3(-.5f,0,5), Vector2Int.left) == Vector2Int.right, "opposite threshold reverses direction");
        var rotated = Quaternion.Euler(0,90,0);
        Check(FishingCounterPull.Calculate(Vector3.zero,new Vector3(0,0,-2),rotated*Vector3.right,rotated*Vector3.up,default)==Vector2Int.left,"view basis rotates steering");
        Check(FishingCounterPull.Calculate(new Vector3(5,3,0),new Vector3(5,3,8),Vector3.right,Vector3.up,default)==Vector2Int.zero,"origin follows player body");
        for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++)
        {
            var required=Direction(new Vector3(x,y,5));
            var s=new FishingStruggle(); s.Begin(12,1,2); s.SetCounterDirection(required);
            Check(s.Progress==3,"quarter progress");
            Check(!s.Pull(new Vector2Int(2,2)),"incorrect direction rejected");
            Check(s.Pull(required) && s.Progress==4.5f,"all eight directions and neutral score correctly");
            for(int ix=-1;ix<=1;ix++) for(int iy=-1;iy<=1;iy++)
            {
                var input = new Vector2Int(ix,iy);
                bool expected = required == Vector2Int.zero ? input == Vector2Int.zero :
                    input != Vector2Int.zero && Vector2.Dot(((Vector2)input).normalized,((Vector2)required).normalized) >= .5f;
                Check(s.Accepts(input)==expected,"all 81 steering combinations");
            }
            for(int i=0;i<20;i++) s.Pull(required);
            Check(s.Won && s.Progress==12,"winning cap");
        }
        var fail=new FishingStruggle(); fail.Begin(12,1,2);
        for(int i=0;i<39;i++) fail.Tick(.1f);
        Check(!fail.Lost && fail.Progress==0,"failure grace");
        Check(fail.Pull(Vector2Int.zero) && fail.EmptyTime==0,"zero-center click recovers");
        for(int i=0;i<40;i++) fail.Tick(.1f);
        Check(fail.Lost,"continuous zero fails");
        var paused=new FishingStruggle(); paused.Begin(28,1,2); paused.Tick(0);
        Check(paused.Progress==7,"pause preserves progress");
        var bonus=new FishingStruggle(); bonus.Begin(28,.35f,2); bonus.SetCounterDirection(Vector2Int.left);
        Check(!bonus.Pull(Vector2Int.right,1) && bonus.Progress==7,"load cannot rescue wrong direction");
        bonus.Pull(Vector2Int.left,10); Check(Mathf.Abs(bonus.Progress-8.8f)<.001f,"load reward capped at twenty percent");
        bonus.Pull(Vector2Int.left,-1); Check(Mathf.Abs(bonus.Progress-10.3f)<.001f,"unloading retains base reward");
        foreach(float required in new[]{12f,28f}) foreach(float interval in new[]{.5f,1f})
        {
            var rhythm=new FishingStruggle(); rhythm.Begin(required,.35f,2);
            float elapsed=0; int clicks=0;
            while(!rhythm.Won && elapsed<30)
            {
                rhythm.Tick(interval); elapsed+=interval;
                // One mistake out of four at two clicks/sec; one/sec must also comfortably win.
                if(interval==1 || ++clicks%4!=0) rhythm.Pull(Vector2Int.zero);
            }
            Check(rhythm.Won && elapsed<= (interval==.5f ? 12 : 20),"comfortable rhythm small/large catch");
        }
        ValidateLoad();
        return "PASS: positional/hysteresis steering, 81 input combinations, capped load, one/two-click rhythm, load motion/teleport/pause, grace and progress.";
    }
    static void ValidateLoad()
    {
        var load=new FishingLoad(); Vector3 fish=new Vector3(0,0,5), rest=Vector3.forward;
        load.Sample(Vector3.zero,rest,Vector3.zero,fish,Vector3.zero,Vector3.forward,.02f);
        for(int i=0;i<60;i++)
        {
            load.Sample(Vector3.zero,rest,Vector3.zero,fish,Vector3.forward*.04f,Vector3.forward,.02f);
            fish+=Vector3.forward*.04f; load.CommitFishPosition(fish);
        }
        Check(load.Normalized>.99f,"outward swimming builds load");
        float paused=load.Normalized; load.Sample(Vector3.zero,rest,Vector3.zero,fish,Vector3.back,Vector3.forward,0);
        Check(load.Normalized==paused,"pause freezes load");
        for(int i=0;i<60;i++)
        {
            load.Sample(Vector3.zero,rest,Vector3.zero,fish,Vector3.back*.04f,Vector3.forward,.02f);
            fish+=Vector3.back*.04f; load.CommitFishPosition(fish);
        }
        Check(load.Normalized<.01f,"inward swimming unloads");
        load.Sample(Vector3.one*10,rest,Vector3.zero,fish,Vector3.zero,Vector3.forward,.02f);
        Check(load.Normalized==0,"teleport clears load");
        load.Reset(); fish=new Vector3(0,0,5);
        for(int i=0;i<60;i++)
        {
            Vector3 body=Vector3.forward*i*.01f;
            load.Sample(body,body+rest,Vector3.zero,fish,Vector3.zero,Vector3.forward,.02f);
            load.CommitFishPosition(fish);
        }
        Check(load.Normalized==0,"walking alone cannot manufacture load");
        load.Reset();
        for(int i=0;i<60;i++)
        {
            load.Sample(Vector3.zero,rest+Vector3.back*i*.02f,Vector3.zero,fish,Vector3.zero,Vector3.forward,.02f);
            load.CommitFishPosition(fish);
        }
        Check(load.Normalized>.45f && load.Normalized<.51f,"tip movement away loads line");
        load.Reset();
        for(int i=0;i<60;i++)
        {
            load.Sample(Vector3.zero,rest+Vector3.back*i*.1f,Vector3.zero,fish,Vector3.zero,Vector3.back,.02f);
            load.CommitFishPosition(fish);
        }
        Check(load.Normalized==0,"backward look disables camera bonus");
        load.Reset();
        for(int i=0;i<60;i++)
        {
            load.Sample(Vector3.zero,rest+Vector3.back*i*.1f,Vector3.zero,fish,Vector3.zero,Vector3.forward,.02f);
            load.CommitFishPosition(fish);
        }
        Check(load.Normalized<=1,"fast camera motion stays bounded");
    }
    static void Check(bool condition,string name) { if(!condition) throw new Exception("Fishing validation failed: "+name); }
}
