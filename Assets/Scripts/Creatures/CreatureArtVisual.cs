using UnityEngine;

/// <summary>Shared art adapter, including species created by runtime ecology generators.</summary>
[DisallowMultipleComponent]
public sealed class CreatureArtVisual : MonoBehaviour
{
    private Creature owner;
    private Transform sculpture;
    private Mesh mesh;
    private Vector3 restingScale;
    private Renderer[] fur;
    private MaterialPropertyBlock block;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        Creature.Activated -= Attach;
        Creature.Activated += Attach;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Existing()
    {
        foreach(var creature in FindObjectsByType<Creature>(FindObjectsSortMode.None)) Attach(creature);
    }
    private static void Attach(Creature creature)
    {
        if(creature == null || creature is BoneChild || creature is ErosiveFluidDroplet ||
            creature is ProjectileCreature || creature is BlastCore || creature is MeleeAsura ||
            creature is RangedAsura || creature is GeneralAsura) return;
        if(!creature.TryGetComponent<CreatureArtVisual>(out _)) creature.gameObject.AddComponent<CreatureArtVisual>();
    }
    private void Start()
    {
        owner=GetComponent<Creature>();
        if(owner==null) return;
        if(owner is FuzzCreature)
        {
            fur=GetComponentsInChildren<Renderer>(); block=new MaterialPropertyBlock(); return;
        }
        bool time=owner is BirdCreature || owner is BirdBones || owner is SkySpider || owner is SkyWeb || owner is GeodeLarva;
        bool heat=owner is FireSource || owner is FireFlower || owner is FireEater || owner is FireBubble;
        bool light=owner is BubbleFlower || owner is TrappingBubble || owner is GlowBee || owner is GlowHoney || owner is GlowBeeNest || owner is OreColossus;
        Color color=time?CreatureArtPalette.Time:heat?CreatureArtPalette.Heat:light?CreatureArtPalette.Pearl:CreatureArtPalette.Organic;
        bool replace=owner is BounceShroom || owner is BirdCreature || owner is BirdBones || owner is SkySpider || owner is GooCreature || owner is FireEater || owner is Squirrel;
        var art=new CreatureSculpture();
        Transform parent=transform;
        if(owner is BounceShroom)
        {
            parent=transform.Find("Platform"); if(parent==null) return;
            art.Orb(new Vector3(0,-.12f,0),new Vector3(.12f,.37f,.12f),CreatureArtPalette.Pearl);
            art.Orb(new Vector3(0,.28f,0),new Vector3(.5f,.22f,.5f),color);
            for(int i=0;i<8;i++)
            {
                float a=i*Mathf.PI/4;
                art.Orb(new Vector3(Mathf.Cos(a)*.34f,.43f,Mathf.Sin(a)*.34f),new Vector3(.045f,.025f,.045f),CreatureArtPalette.Pearl);
            }
        }
        else if(owner is BirdCreature || owner is BirdBones)
        {
            for(int i=0;i<6;i++)
            {
                Vector3 p=new Vector3(0,0,(i-2)*.13f);
                art.Orb(p,new Vector3(.09f,.1f,.09f),CreatureArtPalette.Pearl);
                art.Ring(p,Quaternion.identity,.13f+i*.012f,.015f,color);
            }
            for(int side=-1;side<=1;side+=2) for(int i=0;i<5;i++)
                art.Tube(new Vector3(0,0,.1f),new Vector3(side*(.5f+i*.07f),.08f+i*.04f,-i*.11f),.018f,color);
        }
        else if(owner is SkySpider)
        {
            art.Orb(Vector3.zero,new Vector3(.32f,.2f,.37f),CreatureArtPalette.Ink);
            art.Ring(Vector3.zero,Quaternion.Euler(90,0,0),.37f,.025f,color);
            for(int i=0;i<8;i++)
            {
                float a=i*Mathf.PI/4;
                Vector3 knee=new Vector3(Mathf.Cos(a)*.55f,.22f,Mathf.Sin(a)*.55f);
                art.Tube(Vector3.zero,knee,.022f,color);
                art.Tube(knee,new Vector3(knee.x*1.4f,-.18f,knee.z*1.4f),.016f,CreatureArtPalette.Pearl);
            }
        }
        else if(owner is Squirrel)
        {
            art.Orb(new Vector3(0,.25f,0),new Vector3(.25f,.3f,.4f),color);
            art.Orb(new Vector3(0,.5f,.32f),new Vector3(.21f,.22f,.23f),CreatureArtPalette.OrganicLight);
            for(int s=-1;s<=1;s+=2)
            {
                art.Orb(new Vector3(s*.13f,.73f,.3f),new Vector3(.075f,.17f,.06f),color);
                art.Orb(new Vector3(s*.16f,.54f,.49f),Vector3.one*.045f,CreatureArtPalette.Ink);
                art.Orb(new Vector3(s*.22f,.06f,.05f),new Vector3(.08f,.07f,.23f),CreatureArtPalette.Pearl);
            }
            for(int i=0;i<8;i++) { float a=i*Mathf.PI/7; art.Orb(new Vector3(0,.3f+Mathf.Sin(a)*.55f,-.35f-Mathf.Cos(a)*.3f),Vector3.one*(.12f+i*.008f),color); }
        }
        else if(owner is GooCreature || owner is FireEater)
        {
            art.Orb(Vector3.zero,new Vector3(.4f,.28f,.45f),color);
            for(int i=0;i<6;i++)
            {
                float a=i*Mathf.PI/3;
                art.Orb(new Vector3(Mathf.Cos(a)*.28f,-.1f,Mathf.Sin(a)*.3f),new Vector3(.18f,.16f,.22f),i%2==0?color:CreatureArtPalette.Pearl);
            }
            art.Ring(new Vector3(0,.18f,0),Quaternion.Euler(90,0,0),.24f,.02f,CreatureArtPalette.Pearl);
        }
        else
        {
            // Preserve functional petals, limbs, crystal shards and bubble surfaces; add a distinct crown.
            if(owner is SkyWeb || owner is FireBubble || owner is TrappingBubble)
                art.Ring(Vector3.zero,Quaternion.Euler(90,0,0),.48f,.012f,color);
            else
            {
                float size=owner is OreColossus?2f:.22f;
                for(int i=0;i<5;i++)
                {
                    float a=i*Mathf.PI*2/5;
                    Vector3 tip=new Vector3(Mathf.Cos(a)*size,size*1.8f,Mathf.Sin(a)*size);
                    art.Tube(Vector3.zero,tip,size*.075f,color);
                    art.Orb(tip,Vector3.one*size*.23f,light?CreatureArtPalette.OrganicLight:CreatureArtPalette.Pearl);
                }
            }
        }
        if(replace) foreach(var r in parent.GetComponentsInChildren<Renderer>()) r.enabled=false;
        var go=art.Attach(parent,"Creature Art Sculpture"); sculpture=go.transform;
        mesh=go.GetComponent<MeshFilter>().sharedMesh; restingScale=sculpture.localScale;
    }
    private void LateUpdate()
    {
        if(owner==null) return;
        if(fur!=null)
        {
            float heat=Mathf.Clamp01(owner.Heat/15f);
            foreach(var r in fur)
            {
                if(r==null || r.sharedMaterial==null || !r.sharedMaterial.HasProperty("_RootColor")) continue;
                r.GetPropertyBlock(block);
                block.SetColor("_RootColor",CreatureArtPalette.Organic*.35f);
                block.SetColor("_TipColor",Color.Lerp(CreatureArtPalette.OrganicLight,CreatureArtPalette.Heat,heat));
                r.SetPropertyBlock(block);
            }
        }
        if(sculpture!=null && owner.SimulationEnabled && !(owner is BounceShroom))
            sculpture.localScale=restingScale*(1+Mathf.Sin(Time.time*1.7f+GetInstanceID())*.018f);
    }
    private void OnDestroy() { if(mesh!=null) Destroy(mesh); }
}
