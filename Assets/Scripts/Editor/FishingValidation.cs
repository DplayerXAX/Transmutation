using System;
using UnityEngine;
using UnityEditor;

public static class FishingValidation
{
    [MenuItem("Tools/Creatures/Fishing/Validate Rules")]
    public static void ValidateFromMenu() { Debug.Log(ValidateRules()); }
    public static string ValidateRules()
    {
        var saved = UnityEngine.Random.state;
        try
        {
            var seen = new System.Collections.Generic.HashSet<Vector2Int>();
            for (int seed = 0; seed < 100; seed++)
            {
                UnityEngine.Random.InitState(seed);
                var s = new FishingStruggle(); s.Begin(12, 1, 2, new Vector2(1.5f,2.5f),.25f);
                seen.Add(s.CounterDirection);
                Check(Mathf.Approximately(s.Progress,3),"initial quarter progress");
                Check(!s.Pull(s.CounterDirection),"telegraph cannot score"); s.Tick(.25f);
                Check(!s.Pull(Vector2Int.zero),"neutral cannot score");
                Check(!s.Pull(-s.CounterDirection),"wrong direction cannot score");
                Check(s.Pull(s.CounterDirection) && s.Progress == 4,"correct pull adds one");
                for (int i=0;i<20;i++) s.Pull(s.CounterDirection);
                Check(s.Won && s.Progress == 12,"progress capped at win");
            }
            Check(seen.Count == 8,"all eight directions generated");
            var fail = new FishingStruggle(); fail.Begin(12,1,2,new Vector2(100,100),0);
            for(int i=0;i<39;i++) fail.Tick(.1f);
            Check(!fail.Lost && fail.Progress==0,"zero has grace");
            Check(fail.Pull(fail.CounterDirection) && fail.EmptyTime==0,"recovery resets grace");
            for(int i=0;i<40;i++) fail.Tick(.1f);
            Check(fail.Lost,"continuous zero fails");
            var pause = new FishingStruggle(); pause.Begin(28,1,2,new Vector2(2,2),0); pause.Tick(0);
            Check(pause.Progress==7,"pause does not drain");
            return "PASS: eight directions, telegraph, wrong/neutral input, scoring/cap, grace/recovery/failure, pause.";
        }
        finally { UnityEngine.Random.state=saved; }
    }
    static void Check(bool condition,string name) { if(!condition) throw new Exception("Fishing validation failed: " +name); }
}


