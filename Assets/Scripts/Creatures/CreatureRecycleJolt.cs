using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A brief visual nudge also works on kinematic creatures, without moving their collision or attachments.</summary>
[DisallowMultipleComponent]
public sealed class CreatureRecycleJolt : MonoBehaviour
{
    sealed class Part
    {
        internal Renderer source;
        internal Mesh mesh;
        internal Material[] materials;
        internal bool baked, enabled;
        internal readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
    }
    readonly List<Part> parts=new List<Part>();
    Creature target;
    float elapsed;
    bool playing;
    public static void Play(Creature creature)
    {
        if(creature==null || creature.IsDead)return;
        if(!creature.TryGetComponent<CreatureRecycleJolt>(out var jolt))jolt=creature.gameObject.AddComponent<CreatureRecycleJolt>();
        jolt.Begin(creature);
    }
    void Begin(Creature creature)
    {
        target=creature;elapsed=0f;
        if(playing)return;
        foreach(Renderer renderer in creature.GetComponentsInChildren<Renderer>())
        {
            if(!renderer.enabled || !CreatureBounds.BelongsTo(renderer.transform,creature.transform))continue;
            Mesh mesh=null;bool baked=false;
            if(renderer is SkinnedMeshRenderer skinned){mesh=new Mesh();skinned.BakeMesh(mesh);baked=true;}
            else if(renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter))mesh=filter.sharedMesh;
            if(mesh==null)continue;
            parts.Add(new Part{source=renderer,mesh=mesh,materials=renderer.sharedMaterials,baked=baked,enabled=renderer.enabled});
            renderer.enabled=false;
        }
        playing=true;enabled=true;
    }
    void LateUpdate()
    {
        if(!playing)return;
        if(target==null || target.IsDead){Restore();return;}
        if(target.SimulationEnabled)elapsed+=Time.deltaTime;
        if(elapsed>=0.24f){Restore();return;}
        Matrix4x4 offset=Matrix4x4.Translate(Vector3.up*(Mathf.Sin(elapsed/0.24f*Mathf.PI)*0.07f));
        foreach(Part part in parts)
        {
            if(part.source==null)continue;
            part.source.enabled=false;part.source.GetPropertyBlock(part.properties);
            for(int sub=0;sub<part.mesh.subMeshCount && part.materials.Length>0;sub++)
                if(part.materials[Mathf.Min(sub,part.materials.Length-1)]!=null)Graphics.DrawMesh(part.mesh,offset*part.source.localToWorldMatrix,part.materials[Mathf.Min(sub,part.materials.Length-1)],
                    part.source.gameObject.layer,null,sub,part.properties,part.source.shadowCastingMode,part.source.receiveShadows);
        }
    }
    void Restore()
    {
        if(!playing)return;
        playing=false;
        foreach(Part part in parts)
        {if(part.source!=null)part.source.enabled=part.enabled;if(part.baked && part.mesh!=null)Destroy(part.mesh);}
        parts.Clear();enabled=false;
    }
    void OnDisable()=>Restore();
    void OnDestroy()=>Restore();
}
