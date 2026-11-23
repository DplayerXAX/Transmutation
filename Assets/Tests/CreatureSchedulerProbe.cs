#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CreatureSchedulerProbe : MonoBehaviour
{
    public static string Result;
    public static string Measurements;
    readonly List<Creature> fixtures=new();
    IEnumerator Start()
    {
        Result="Running";
        var player=FindFirstObjectByType<PlayerCreatureCarrier>();
        var policy=FindFirstObjectByType<CreatureSleep>();
        Check(policy!=null,"distance policy installed");
        var scheduler=CreatureSimulationScheduler.For(player.gameObject.scene);
        int original=policy.CandidateCount;
        var stage=new GameObject("Scheduler fixtures");
        for(int i=0;i<512;i++)
        {
            var go=new GameObject("Dormant fixture "+i);go.transform.SetParent(stage.transform);
            go.transform.position=player.transform.position+new Vector3(500+i,0,0);
            fixtures.Add(go.AddComponent<CarryThrowValidationProbe>());
        }
        Check(policy.CandidateCount==original+512,"spawn registers without a discovery scan");
        float until=Time.time+2;int peakChecks=0,frames=0;double total=0,peak=0;
        while(Time.time<until)
        {
            yield return null;peakChecks=Mathf.Max(peakChecks,policy.ChecksLastFrame);
            total+=scheduler.LastWorkMicroseconds;peak=Math.Max(peak,scheduler.LastWorkMicroseconds);frames++;
        }
        foreach(var creature in fixtures)Check(!creature.SimulationEnabled && creature.SimulationPermitted,"far fixture becomes optimization-dormant");
        Check(peakChecks<=policy.ChecksPerFrame,"bounded relevance work");
        var subject=fixtures[0];var ownerA=new GameObject("Activity owner A");var ownerB=new GameObject("Activity owner B");
        var a=CreatureSimulation.KeepActive(subject,ownerA);var b=CreatureSimulation.KeepActive(subject,ownerB);
        Check(subject.SimulationEnabled,"activity wakes immediately between polling checks");
        a.Dispose();a.Dispose();Check(subject.SimulationEnabled,"independent owner survives release and duplicate disposal");
        subject.SetSimulationEnabled(false);Check(!subject.SimulationEnabled,"semantic pause takes priority over activity");
        b.Dispose();subject.SetSimulationEnabled(true);Check(!subject.SimulationEnabled,"released request restores optimization restriction");
        subject.transform.position=player.transform.position+Vector3.right*2;
        until=Time.time+1.5f;while(!subject.SimulationEnabled&&Time.time<until)yield return null;
        Check(subject.SimulationEnabled,"current position wakes after approaching player");
        var destroyedOwner=CreatureSimulation.KeepActive(fixtures[1],ownerA);
        Destroy(ownerA);yield return null;Check(!fixtures[1].SimulationEnabled,"destroyed activity owner does not pin forever");destroyedOwner.Dispose();
        policy.enabled=false;Check(fixtures[2].SimulationEnabled,"policy shutdown releases its own dormancy");
        subject.SetSimulationEnabled(false);policy.enabled=true;yield return null;
        Check(!subject.SimulationEnabled,"policy re-enable does not undo semantic pause");
        int removedFixtures=0;
        Action<Creature> removed=c=>{if(fixtures.Contains(c))removedFixtures++;};
        CreatureRegistry.Removed+=removed;
        Destroy(stage);Destroy(ownerB);yield return null;
        CreatureRegistry.Removed-=removed;
        Check(removedFixtures==fixtures.Count,"destroy unregisters fixtures");
        // A second scene owns a separate policy/clock and releases both on unload.
        int policiesBefore=scheduler.PolicyCount;
        int candidatesBefore=policy.CandidateCount;
        var secondary=SceneManager.CreateScene("Scheduler lifecycle fixture");
        var secondaryRoot=new GameObject("Secondary policy");
        SceneManager.MoveGameObjectToScene(secondaryRoot,secondary);
        var secondaryPolicy=secondaryRoot.AddComponent<CreatureSleep>();
        var secondaryCreature=secondaryRoot.AddComponent<CarryThrowValidationProbe>();
        var secondaryScheduler=CreatureSimulationScheduler.For(secondary);
        Check(secondaryScheduler!=scheduler && secondaryPolicy.CandidateCount==1 && policy.CandidateCount==candidatesBefore,
            "scene-local registration does not contaminate another policy");
        var activity=CreatureSimulation.KeepActive(secondaryCreature,this);
        yield return SceneManager.UnloadSceneAsync(secondary);
        Check(!secondaryScheduler && !secondaryCreature && scheduler.PolicyCount==policiesBefore,"scene unload cleans policy and scheduler");
        activity.Dispose(); // An outstanding handle remains safe after its entity was destroyed.
        Measurements=$"512 lifecycle-registered fixtures, peak {peakChecks} checks/frame (budget {policy.ChecksPerFrame}), {frames} sampled frames; scheduler mean {total/Math.Max(1,frames):F1}us / max {peak:F1}us in this Editor run";
        Result="PASS: lifecycle, bounded checks, source/semantic priority, owned activity, disposal, position wake, policy shutdown/re-enable, additive scene isolation/unload\n"+Measurements;
    }
    static void Check(bool ok,string label){if(ok)return;Result="FAIL: "+label;throw new InvalidOperationException(Result);}
}
#endif
